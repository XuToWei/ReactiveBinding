using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ReactiveBinding.Generator;

internal sealed class ReactiveKnownSymbols
{
    public INamedTypeSymbol? ReactiveSourceAttribute { get; }
    public INamedTypeSymbol? ReactiveBindAttribute { get; }
    public INamedTypeSymbol? ReactiveThrottleAttribute { get; }
    public INamedTypeSymbol? ReactiveObserveIgnoreAttribute { get; }
    public INamedTypeSymbol? IReactiveObserver { get; }
    public INamedTypeSymbol? IVersion { get; }

    private ReactiveKnownSymbols(Compilation compilation)
    {
        ReactiveSourceAttribute = compilation.GetTypeByMetadataName("ReactiveBinding.ReactiveSourceAttribute");
        ReactiveBindAttribute = compilation.GetTypeByMetadataName("ReactiveBinding.ReactiveBindAttribute");
        ReactiveThrottleAttribute = compilation.GetTypeByMetadataName("ReactiveBinding.ReactiveThrottleAttribute");
        ReactiveObserveIgnoreAttribute = compilation.GetTypeByMetadataName("ReactiveBinding.ReactiveObserveIgnoreAttribute");
        IReactiveObserver = compilation.GetTypeByMetadataName("ReactiveBinding.IReactiveObserver");
        IVersion = compilation.GetTypeByMetadataName("ReactiveBinding.IVersion");
    }

    public static ReactiveKnownSymbols Create(Compilation compilation) => new(compilation);
}

/// <summary>Collects syntax candidates; all semantic work is deferred until generator execution.</summary>
internal sealed class ReactiveSyntaxReceiver : ISyntaxReceiver
{
    private readonly List<SyntaxNode> _candidates = new();

    public void OnVisitSyntaxNode(SyntaxNode node)
    {
        switch (node)
        {
            case ClassDeclarationSyntax declaration
                when declaration.BaseList != null || declaration.AttributeLists.Count > 0:
                _candidates.Add(declaration);
                break;
            case FieldDeclarationSyntax { AttributeLists.Count: > 0 }:
            case PropertyDeclarationSyntax { AttributeLists.Count: > 0 }:
            case MethodDeclarationSyntax { AttributeLists.Count: > 0 }:
                _candidates.Add(node);
                break;
        }
    }

    public IReadOnlyList<ReactiveClassData> BuildClassData(
        Compilation compilation,
        ReactiveKnownSymbols knownSymbols)
    {
        var classDataList = new List<ReactiveClassData>();
        var classDataMap = new Dictionary<INamedTypeSymbol, ReactiveClassData>(SymbolEqualityComparer.Default);
        var semanticModels = new Dictionary<SyntaxTree, SemanticModel>();

        SemanticModel GetSemanticModel(SyntaxNode node)
        {
            var tree = node.SyntaxTree;
            if (!semanticModels.TryGetValue(tree, out var model))
            {
                model = compilation.GetSemanticModel(tree);
                semanticModels.Add(tree, model);
            }
            return model;
        }

        ReactiveClassData GetOrCreate(
            INamedTypeSymbol classSymbol,
            ClassDeclarationSyntax classDeclaration)
        {
            if (!classDataMap.TryGetValue(classSymbol, out var classData))
            {
                classData = new ReactiveClassData
                {
                    ClassSymbol = classSymbol,
                    ClassDeclaration = classDeclaration
                };
                classDataMap.Add(classSymbol, classData);
                classDataList.Add(classData);
            }
            return classData;
        }

        foreach (var candidate in _candidates)
        {
            var semanticModel = GetSemanticModel(candidate);
            switch (candidate)
            {
                case ClassDeclarationSyntax declaration:
                    ProcessClassDeclaration(semanticModel, declaration, knownSymbols, GetOrCreate);
                    break;
                case FieldDeclarationSyntax declaration:
                    ProcessFieldDeclaration(semanticModel, declaration, knownSymbols, GetOrCreate);
                    break;
                case PropertyDeclarationSyntax declaration:
                    ProcessPropertyDeclaration(semanticModel, declaration, knownSymbols, GetOrCreate);
                    break;
                case MethodDeclarationSyntax declaration:
                    ProcessMethodDeclaration(semanticModel, declaration, knownSymbols, GetOrCreate);
                    break;
            }
        }
        return classDataList;
    }

    private static void ProcessClassDeclaration(
        SemanticModel semanticModel,
        ClassDeclarationSyntax classDeclaration,
        ReactiveKnownSymbols knownSymbols,
        System.Func<INamedTypeSymbol, ClassDeclarationSyntax, ReactiveClassData> getOrCreate)
    {
        if (semanticModel.GetDeclaredSymbol(classDeclaration) is not INamedTypeSymbol classSymbol)
            return;

        bool implementsInterface = GeneratorHelper.IsOrImplementsInterface(
            classSymbol, knownSymbols.IReactiveObserver);
        var throttleAttr = FindAttribute(classSymbol.GetAttributes(), knownSymbols.ReactiveThrottleAttribute);
        if (!implementsInterface && throttleAttr == null)
            return;

        var classData = getOrCreate(classSymbol, classDeclaration);
        classData.HasReactiveBase = classSymbol.BaseType != null
            && GeneratorHelper.IsOrImplementsInterface(classSymbol.BaseType, knownSymbols.IReactiveObserver);

        if (throttleAttr?.ConstructorArguments is { Length: > 0 }
            && throttleAttr.ConstructorArguments[0].Value is int callCount)
            classData.ThrottleCallCount = callCount;
    }

    private static void ProcessFieldDeclaration(
        SemanticModel semanticModel,
        FieldDeclarationSyntax fieldDeclaration,
        ReactiveKnownSymbols knownSymbols,
        System.Func<INamedTypeSymbol, ClassDeclarationSyntax, ReactiveClassData> getOrCreate)
    {
        foreach (var variable in fieldDeclaration.Declaration.Variables)
        {
            if (semanticModel.GetDeclaredSymbol(variable) is not IFieldSymbol fieldSymbol
                || !GeneratorHelper.HasAttribute(fieldSymbol, knownSymbols.ReactiveSourceAttribute))
                continue;

            var classDeclaration = GetClassDeclaration(fieldDeclaration);
            if (classDeclaration == null) continue;

            getOrCreate(fieldSymbol.ContainingType, classDeclaration).Sources.Add(new ReactiveSourceData
            {
                MemberName = fieldSymbol.Name,
                MemberKind = ReactiveSourceKind.Field,
                TypeSymbol = fieldSymbol.Type,
                Location = variable.Identifier.GetLocation(),
                HasGetter = true,
                HasParameters = false,
                IsVersionContainer = GeneratorHelper.IsOrImplementsInterface(fieldSymbol.Type, knownSymbols.IVersion)
            });
        }
    }

    private static void ProcessPropertyDeclaration(
        SemanticModel semanticModel,
        PropertyDeclarationSyntax propertyDeclaration,
        ReactiveKnownSymbols knownSymbols,
        System.Func<INamedTypeSymbol, ClassDeclarationSyntax, ReactiveClassData> getOrCreate)
    {
        if (semanticModel.GetDeclaredSymbol(propertyDeclaration) is not IPropertySymbol propertySymbol
            || !GeneratorHelper.HasAttribute(propertySymbol, knownSymbols.ReactiveSourceAttribute))
            return;

        var classDeclaration = GetClassDeclaration(propertyDeclaration);
        if (classDeclaration == null) return;

        getOrCreate(propertySymbol.ContainingType, classDeclaration).Sources.Add(new ReactiveSourceData
        {
            MemberName = propertySymbol.Name,
            MemberKind = ReactiveSourceKind.Property,
            TypeSymbol = propertySymbol.Type,
            Location = propertyDeclaration.Identifier.GetLocation(),
            HasGetter = propertySymbol.GetMethod != null,
            HasParameters = false,
            IsVersionContainer = GeneratorHelper.IsOrImplementsInterface(propertySymbol.Type, knownSymbols.IVersion)
        });
    }

    private static void ProcessMethodDeclaration(
        SemanticModel semanticModel,
        MethodDeclarationSyntax methodDeclaration,
        ReactiveKnownSymbols knownSymbols,
        System.Func<INamedTypeSymbol, ClassDeclarationSyntax, ReactiveClassData> getOrCreate)
    {
        if (semanticModel.GetDeclaredSymbol(methodDeclaration) is not IMethodSymbol methodSymbol)
            return;

        var attributes = methodSymbol.GetAttributes();
        var sourceAttr = FindAttribute(attributes, knownSymbols.ReactiveSourceAttribute);
        var bindAttr = FindAttribute(attributes, knownSymbols.ReactiveBindAttribute);
        if (sourceAttr == null && bindAttr == null)
            return;

        var classDeclaration = GetClassDeclaration(methodDeclaration);
        if (classDeclaration == null) return;

        var classData = getOrCreate(methodSymbol.ContainingType, classDeclaration);
        if (sourceAttr != null)
        {
            classData.Sources.Add(new ReactiveSourceData
            {
                MemberName = methodSymbol.Name,
                MemberKind = ReactiveSourceKind.Method,
                TypeSymbol = methodSymbol.ReturnType,
                Location = methodDeclaration.Identifier.GetLocation(),
                HasGetter = true,
                HasParameters = methodSymbol.Parameters.Length > 0,
                IsVersionContainer = GeneratorHelper.IsOrImplementsInterface(methodSymbol.ReturnType, knownSymbols.IVersion)
            });
        }

        if (bindAttr == null)
            return;

        var reactiveIds = new List<string>();
        if (bindAttr.ConstructorArguments.Length > 0)
        {
            var args = bindAttr.ConstructorArguments[0];
            if (args.Kind == TypedConstantKind.Array)
                reactiveIds.AddRange(args.Values.Where(v => v.Value is string).Select(v => (string)v.Value!));
        }

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
            UsesNameof = CheckUsesNameof(bindAttr),
            IsAutoInferred = isAutoInferred,
            MethodSyntax = isAutoInferred ? methodDeclaration : null
        });
    }

    private static AttributeData? FindAttribute(
        IEnumerable<AttributeData> attributes,
        INamedTypeSymbol? attributeSymbol)
    {
        if (attributeSymbol == null) return null;
        return attributes.FirstOrDefault(attribute =>
            SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, attributeSymbol));
    }

    private static bool CheckUsesNameof(AttributeData bindAttr)
    {
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
                return false;
        }
        return true;
    }

    private static ClassDeclarationSyntax? GetClassDeclaration(SyntaxNode node)
    {
        for (var parent = node.Parent; parent != null; parent = parent.Parent)
        {
            if (parent is ClassDeclarationSyntax classDeclaration)
                return classDeclaration;
        }
        return null;
    }
}
