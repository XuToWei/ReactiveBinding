using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ReactiveBinding.Generator;

internal class ReactiveSyntaxReceiver : ISyntaxContextReceiver
{
    private const string ReactiveSourceAttributeName = "ReactiveBinding.ReactiveSourceAttribute";
    private const string ReactiveBindAttributeName = "ReactiveBinding.ReactiveBindAttribute";
    private const string ReactiveThrottleAttributeName = "ReactiveBinding.ReactiveThrottleAttribute";
    private const string IVersionInterfaceName = "ReactiveBinding.IVersion";

    public List<ReactiveClassData> ClassDataList { get; } = new();

    private readonly Dictionary<INamedTypeSymbol, ReactiveClassData> _classDataMap = new(SymbolEqualityComparer.Default);

    public void OnVisitSyntaxNode(GeneratorSyntaxContext context)
    {
        var node = context.Node;

        // Check for class with ReactiveThrottle attribute
        if (node is ClassDeclarationSyntax classDeclaration)
        {
            ProcessClassDeclaration(context, classDeclaration);
        }

        // Check for ReactiveSource on fields, properties, and methods
        if (node is FieldDeclarationSyntax fieldDeclaration)
        {
            ProcessFieldDeclaration(context, fieldDeclaration);
        }
        else if (node is PropertyDeclarationSyntax propertyDeclaration)
        {
            ProcessPropertyDeclaration(context, propertyDeclaration);
        }
        else if (node is MethodDeclarationSyntax methodDeclaration)
        {
            ProcessMethodDeclaration(context, methodDeclaration);
        }
    }

    private void ProcessClassDeclaration(GeneratorSyntaxContext context, ClassDeclarationSyntax classDeclaration)
    {
        if (context.SemanticModel.GetDeclaredSymbol(classDeclaration) is not INamedTypeSymbol classSymbol)
        {
            return;
        }

        // Check for ReactiveThrottle attribute
        var throttleAttr = classSymbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == ReactiveThrottleAttributeName);

        if (throttleAttr != null)
        {
            var classData = GetOrCreateClassData(classSymbol, classDeclaration);

            if (throttleAttr.ConstructorArguments.Length > 0 &&
                throttleAttr.ConstructorArguments[0].Value is int callCount)
            {
                classData.ThrottleCallCount = callCount;
            }
        }
    }

    private void ProcessFieldDeclaration(GeneratorSyntaxContext context, FieldDeclarationSyntax fieldDeclaration)
    {
        foreach (var variable in fieldDeclaration.Declaration.Variables)
        {
            if (context.SemanticModel.GetDeclaredSymbol(variable) is not IFieldSymbol fieldSymbol)
            {
                continue;
            }

            var sourceAttr = fieldSymbol.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == ReactiveSourceAttributeName);

            if (sourceAttr == null)
            {
                continue;
            }

            var classSymbol = fieldSymbol.ContainingType;
            var classDeclaration = GetClassDeclaration(fieldDeclaration);
            if (classDeclaration == null) continue;

            var classData = GetOrCreateClassData(classSymbol, classDeclaration);

            classData.Sources.Add(new ReactiveSourceData
            {
                MemberName = fieldSymbol.Name,
                MemberKind = ReactiveSourceKind.Field,
                TypeSymbol = fieldSymbol.Type,
                Location = variable.Identifier.GetLocation(),
                HasGetter = true,
                HasParameters = false,
                IsVersionContainer = IsVersionContainer(fieldSymbol.Type)
            });
        }
    }

    private void ProcessPropertyDeclaration(GeneratorSyntaxContext context, PropertyDeclarationSyntax propertyDeclaration)
    {
        if (context.SemanticModel.GetDeclaredSymbol(propertyDeclaration) is not IPropertySymbol propertySymbol)
        {
            return;
        }

        var sourceAttr = propertySymbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == ReactiveSourceAttributeName);

        if (sourceAttr == null)
        {
            return;
        }

        var classSymbol = propertySymbol.ContainingType;
        var classDeclaration = GetClassDeclaration(propertyDeclaration);
        if (classDeclaration == null) return;

        var classData = GetOrCreateClassData(classSymbol, classDeclaration);

        classData.Sources.Add(new ReactiveSourceData
        {
            MemberName = propertySymbol.Name,
            MemberKind = ReactiveSourceKind.Property,
            TypeSymbol = propertySymbol.Type,
            Location = propertyDeclaration.Identifier.GetLocation(),
            HasGetter = propertySymbol.GetMethod != null,
            HasParameters = false,
            IsVersionContainer = IsVersionContainer(propertySymbol.Type)
        });
    }

    private void ProcessMethodDeclaration(GeneratorSyntaxContext context, MethodDeclarationSyntax methodDeclaration)
    {
        if (context.SemanticModel.GetDeclaredSymbol(methodDeclaration) is not IMethodSymbol methodSymbol)
        {
            return;
        }

        // Check for ReactiveSource
        var sourceAttr = methodSymbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == ReactiveSourceAttributeName);

        if (sourceAttr != null)
        {
            var classSymbol = methodSymbol.ContainingType;
            var classDeclaration = GetClassDeclaration(methodDeclaration);
            if (classDeclaration != null)
            {
                var classData = GetOrCreateClassData(classSymbol, classDeclaration);

                classData.Sources.Add(new ReactiveSourceData
                {
                    MemberName = methodSymbol.Name,
                    MemberKind = ReactiveSourceKind.Method,
                    TypeSymbol = methodSymbol.ReturnType,
                    Location = methodDeclaration.Identifier.GetLocation(),
                    HasGetter = true,
                    HasParameters = methodSymbol.Parameters.Length > 0,
                    IsVersionContainer = IsVersionContainer(methodSymbol.ReturnType)
                });
            }
        }

        // Check for ReactiveBind
        var bindAttr = methodSymbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == ReactiveBindAttributeName);

        if (bindAttr != null)
        {
            var classSymbol = methodSymbol.ContainingType;
            var classDeclaration = GetClassDeclaration(methodDeclaration);
            if (classDeclaration != null)
            {
                var classData = GetOrCreateClassData(classSymbol, classDeclaration);

                // Get reactive ids from attribute
                var reactiveIds = new List<string>();
                if (bindAttr.ConstructorArguments.Length > 0)
                {
                    var args = bindAttr.ConstructorArguments[0];
                    if (args.Kind == TypedConstantKind.Array)
                    {
                        reactiveIds.AddRange(args.Values
                            .Where(v => v.Value is string)
                            .Select(v => (string)v.Value!));
                    }
                }

                // Check if nameof() is used
                bool usesNameof = CheckUsesNameof(methodDeclaration, bindAttr);

                // When no reactive ids are specified, mark as auto-inferred
                bool isAutoInferred = reactiveIds.Count == 0;

                classData.Bindings.Add(new ReactiveBindData
                {
                    MethodName = methodSymbol.Name,
                    ReactiveIds = reactiveIds.ToArray(),
                    ParameterTypes = methodSymbol.Parameters.Select(p => p.Type).ToArray(),
                    Location = methodDeclaration.Identifier.GetLocation(),
                    IsStatic = methodSymbol.IsStatic,
                    ReturnsVoid = methodSymbol.ReturnsVoid,
                    UsesNameof = usesNameof,
                    IsAutoInferred = isAutoInferred,
                    MethodSyntax = isAutoInferred ? methodDeclaration : null
                });
            }
        }
    }

    private bool CheckUsesNameof(MethodDeclarationSyntax methodDeclaration, AttributeData bindAttr)
    {
        // Find the attribute syntax
        var attributeLists = methodDeclaration.AttributeLists;
        foreach (var attributeList in attributeLists)
        {
            foreach (var attribute in attributeList.Attributes)
            {
                var name = attribute.Name.ToString();
                if (name == "ReactiveBind" || name == "ReactiveBindAttribute")
                {
                    // Check argument list
                    var args = attribute.ArgumentList?.Arguments;
                    if (args == null || args.Value.Count == 0)
                    {
                        return true; // No arguments means empty, which is valid
                    }

                    // Check each argument
                    foreach (var arg in args)
                    {
                        var expr = arg.Expression;
                        // Must be InvocationExpression with nameof
                        if (expr is InvocationExpressionSyntax invocation)
                        {
                            var invocationName = invocation.Expression.ToString();
                            if (invocationName != "nameof")
                            {
                                return false;
                            }
                        }
                        else
                        {
                            // Not using nameof()
                            return false;
                        }
                    }

                    return true;
                }
            }
        }

        return true; // Default to true if we can't find the attribute
    }

    private ReactiveClassData GetOrCreateClassData(INamedTypeSymbol classSymbol, ClassDeclarationSyntax classDeclaration)
    {
        if (!_classDataMap.TryGetValue(classSymbol, out var classData))
        {
            classData = new ReactiveClassData
            {
                ClassSymbol = classSymbol,
                ClassDeclaration = classDeclaration
            };
            _classDataMap[classSymbol] = classData;
            ClassDataList.Add(classData);
        }

        return classData;
    }

    private static ClassDeclarationSyntax? GetClassDeclaration(SyntaxNode node)
    {
        var parent = node.Parent;
        while (parent != null)
        {
            if (parent is ClassDeclarationSyntax classDeclaration)
            {
                return classDeclaration;
            }
            parent = parent.Parent;
        }
        return null;
    }

    private static bool IsVersionContainer(ITypeSymbol typeSymbol)
    {
        return typeSymbol.AllInterfaces.Any(i => i.ToDisplayString() == IVersionInterfaceName);
    }
}
