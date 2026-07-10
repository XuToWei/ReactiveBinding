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
    private const string IReactiveObserverName = "ReactiveBinding.IReactiveObserver";
    private const string IVersionInterfaceName = "ReactiveBinding.IVersion";

    public List<ReactiveClassData> ClassDataList { get; } = new();

    private readonly Dictionary<INamedTypeSymbol, ReactiveClassData> _classDataMap = new(SymbolEqualityComparer.Default);

    public void OnVisitSyntaxNode(GeneratorSyntaxContext context)
    {
        var node = context.Node;

        // Track reactive classes even when they have no attributed members.
        if (node is ClassDeclarationSyntax classDeclaration)
        {
            ProcessClassDeclaration(context.SemanticModel, classDeclaration);
        }

        // Attributed members can contribute sources and bindings.
        if (node is FieldDeclarationSyntax { AttributeLists.Count: > 0 } fieldDeclaration)
        {
            ProcessFieldDeclaration(context.SemanticModel, fieldDeclaration);
        }
        else if (node is PropertyDeclarationSyntax { AttributeLists.Count: > 0 } propertyDeclaration)
        {
            ProcessPropertyDeclaration(context.SemanticModel, propertyDeclaration);
        }
        else if (node is MethodDeclarationSyntax { AttributeLists.Count: > 0 } methodDeclaration)
        {
            ProcessMethodDeclaration(context.SemanticModel, methodDeclaration);
        }
    }

    private void ProcessClassDeclaration(SemanticModel semanticModel, ClassDeclarationSyntax classDeclaration)
    {
        if (semanticModel.GetDeclaredSymbol(classDeclaration) is not INamedTypeSymbol classSymbol)
        {
            return;
        }

        // Check if class implements IReactiveObserver (ensure we generate ObserveChanges even without markers)
        bool implementsInterface = classSymbol.AllInterfaces.Any(i =>
            i.ToDisplayString() == IReactiveObserverName);

        if (implementsInterface)
        {
            // Ensure class is tracked even without any reactive markers
            var classData = GetOrCreateClassData(classSymbol, classDeclaration);
            classData.HasReactiveBase = HasBaseWithReactiveObserver(classSymbol);
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

    private void ProcessFieldDeclaration(SemanticModel semanticModel, FieldDeclarationSyntax fieldDeclaration)
    {
        foreach (var variable in fieldDeclaration.Declaration.Variables)
        {
            if (semanticModel.GetDeclaredSymbol(variable) is not IFieldSymbol fieldSymbol)
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

    private void ProcessPropertyDeclaration(SemanticModel semanticModel, PropertyDeclarationSyntax propertyDeclaration)
    {
        if (semanticModel.GetDeclaredSymbol(propertyDeclaration) is not IPropertySymbol propertySymbol)
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

    private void ProcessMethodDeclaration(SemanticModel semanticModel, MethodDeclarationSyntax methodDeclaration)
    {
        if (semanticModel.GetDeclaredSymbol(methodDeclaration) is not IMethodSymbol methodSymbol)
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
                    HasUnsupportedSignature = methodSymbol.IsGenericMethod
                        || methodSymbol.Parameters.Any(p => p.RefKind != RefKind.None),
                    UsesNameof = usesNameof,
                    IsAutoInferred = isAutoInferred,
                    MethodSyntax = isAutoInferred ? methodDeclaration : null
                });
            }
        }
    }

    private bool CheckUsesNameof(MethodDeclarationSyntax methodDeclaration, AttributeData bindAttr)
    {
        // AttributeData identifies the exact ReactiveBind instance even when its syntax is qualified or aliased.
        if (bindAttr.ApplicationSyntaxReference?.GetSyntax() is not AttributeSyntax attribute)
            return true;

        var args = attribute.ArgumentList?.Arguments;
        if (args == null || args.Value.Count == 0)
            return true;

        foreach (var arg in args.Value)
        {
            if (arg.Expression is not InvocationExpressionSyntax invocation
                || invocation.Expression is not IdentifierNameSyntax invocationName
                || invocationName.Identifier.ValueText != "nameof")
            {
                return false;
            }
        }
        return true;
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

    private static bool HasBaseWithReactiveObserver(INamedTypeSymbol classSymbol)
    {
        var baseType = classSymbol.BaseType;
        while (baseType != null && baseType.SpecialType != SpecialType.System_Object)
        {
            if (baseType.AllInterfaces.Any(i => i.ToDisplayString() == IReactiveObserverName))
            {
                return true;
            }
            baseType = baseType.BaseType;
        }
        return false;
    }

    private static bool IsVersionContainer(ITypeSymbol typeSymbol)
    {
        return GeneratorHelper.IsOrImplementsInterface(typeSymbol, IVersionInterfaceName);
    }
}
