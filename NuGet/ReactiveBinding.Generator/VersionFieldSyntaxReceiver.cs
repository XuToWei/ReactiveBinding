using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ReactiveBinding.Generator;

internal class VersionFieldClassData
{
    public INamedTypeSymbol ClassSymbol { get; set; } = null!;
    public ClassDeclarationSyntax ClassDeclaration { get; set; } = null!;
    public List<VersionFieldData> Fields { get; } = new();
}

/// <summary>How a synced field is serialized.</summary>
internal enum VersionSyncKind
{
    None,
    Scalar,      // bool/byte/.../string/enum
    SyncObject,  // nested concrete type that implements IVersionSync
    Container    // VersionList/VersionDictionary/VersionHashSet
}

internal class VersionFieldData
{
    public string FieldName { get; set; } = "";
    public string PropertyName { get; set; } = "";
    public ITypeSymbol TypeSymbol { get; set; } = null!;
    public Location Location { get; set; } = Location.None;
    public bool IsPrivate { get; set; }
    public bool IsStatic { get; set; }
    public bool IsReadOnly { get; set; }
    public bool IsConst { get; set; }
    public bool IsVersionType { get; set; }
    public List<string> PropertyAttributes { get; } = new();

    /// <summary>Field participates in sync (set by the generator when the class implements IVersionSync).</summary>
    public bool IsSynced { get; set; }
    /// <summary>Field id on the wire (assigned by the generator over synced valid fields; written as a byte).</summary>
    public int SyncSlot { get; set; }
    /// <summary>Serialization category (resolved by the generator).</summary>
    public VersionSyncKind SyncKind { get; set; }
}

internal sealed class VersionFieldKnownSymbols
{
    public INamedTypeSymbol? VersionFieldAttribute { get; }
    public INamedTypeSymbol? VersionFieldPropertyAttribute { get; }
    public INamedTypeSymbol? IVersion { get; }
    public INamedTypeSymbol? IVersionSync { get; }
    public INamedTypeSymbol? VersionSyncList { get; }
    public INamedTypeSymbol? VersionSyncDictionary { get; }
    public INamedTypeSymbol? VersionSyncHashSet { get; }

    private VersionFieldKnownSymbols(Compilation compilation)
    {
        VersionFieldAttribute = compilation.GetTypeByMetadataName("ReactiveBinding.VersionFieldAttribute");
        VersionFieldPropertyAttribute = compilation.GetTypeByMetadataName("ReactiveBinding.VersionFieldPropertyAttribute");
        IVersion = compilation.GetTypeByMetadataName("ReactiveBinding.IVersion");
        IVersionSync = compilation.GetTypeByMetadataName("ReactiveBinding.IVersionSync");
        VersionSyncList = compilation.GetTypeByMetadataName("ReactiveBinding.VersionSyncList`1");
        VersionSyncDictionary = compilation.GetTypeByMetadataName("ReactiveBinding.VersionSyncDictionary`2");
        VersionSyncHashSet = compilation.GetTypeByMetadataName("ReactiveBinding.VersionSyncHashSet`1");
    }

    public static VersionFieldKnownSymbols Create(Compilation compilation) => new(compilation);
}

/// <summary>Collects attributed fields syntactically; semantic work is deferred until generator execution.</summary>
internal sealed class VersionFieldSyntaxReceiver : ISyntaxReceiver
{
    private readonly List<FieldDeclarationSyntax> _candidates = new();

    public void OnVisitSyntaxNode(SyntaxNode node)
    {
        if (node is FieldDeclarationSyntax { AttributeLists.Count: > 0 } fieldDeclaration)
            _candidates.Add(fieldDeclaration);
    }

    public IReadOnlyList<VersionFieldClassData> BuildClassData(
        Compilation compilation,
        VersionFieldKnownSymbols knownSymbols)
    {
        var classDataList = new List<VersionFieldClassData>();
        var classDataMap = new Dictionary<INamedTypeSymbol, VersionFieldClassData>(
            SymbolEqualityComparer.Default);
        var semanticModels = new Dictionary<SyntaxTree, SemanticModel>();

        foreach (var fieldDeclaration in _candidates)
        {
            var syntaxTree = fieldDeclaration.SyntaxTree;
            if (!semanticModels.TryGetValue(syntaxTree, out var semanticModel))
            {
                semanticModel = compilation.GetSemanticModel(syntaxTree);
                semanticModels.Add(syntaxTree, semanticModel);
            }

            ProcessFieldDeclaration(
                semanticModel,
                fieldDeclaration,
                knownSymbols,
                classDataMap,
                classDataList);
        }

        return classDataList;
    }

    private static void ProcessFieldDeclaration(
        SemanticModel semanticModel,
        FieldDeclarationSyntax fieldDeclaration,
        VersionFieldKnownSymbols knownSymbols,
        IDictionary<INamedTypeSymbol, VersionFieldClassData> classDataMap,
        ICollection<VersionFieldClassData> classDataList)
    {
        foreach (var variable in fieldDeclaration.Declaration.Variables)
        {
            if (semanticModel.GetDeclaredSymbol(variable) is not IFieldSymbol fieldSymbol)
                continue;

            var attributes = fieldSymbol.GetAttributes();
            if (!attributes.Any(attribute => SymbolEqualityComparer.Default.Equals(
                    attribute.AttributeClass, knownSymbols.VersionFieldAttribute)))
                continue;

            var classSymbol = fieldSymbol.ContainingType;
            var classDeclaration = GetClassDeclaration(fieldDeclaration);
            if (classDeclaration == null) continue;

            if (!classDataMap.TryGetValue(classSymbol, out var classData))
            {
                classData = new VersionFieldClassData
                {
                    ClassSymbol = classSymbol,
                    ClassDeclaration = classDeclaration
                };
                classDataMap.Add(classSymbol, classData);
                classDataList.Add(classData);
            }

            string fieldName = fieldSymbol.Name;
            string propertyName = GeneratorHelper.ConvertVersionFieldToPropertyName(fieldName);

            // Check if field type implements IVersion
            bool isVersionType = GeneratorHelper.IsOrImplementsInterface(
                fieldSymbol.Type, knownSymbols.IVersion);

            var fieldData = new VersionFieldData
            {
                FieldName = fieldName,
                PropertyName = propertyName,
                TypeSymbol = fieldSymbol.Type,
                Location = variable.Identifier.GetLocation(),
                IsPrivate = fieldSymbol.DeclaredAccessibility == Accessibility.Private,
                IsStatic = fieldSymbol.IsStatic,
                IsReadOnly = fieldSymbol.IsReadOnly,
                IsConst = fieldSymbol.IsConst,
                IsVersionType = isVersionType
            };

            // Collect [VersionProperty] attributes
            foreach (var attr in attributes)
            {
                if (!SymbolEqualityComparer.Default.Equals(
                        attr.AttributeClass, knownSymbols.VersionFieldPropertyAttribute))
                    continue;

                if (attr.ConstructorArguments.Length == 0)
                    continue;

                var arg = attr.ConstructorArguments[0];
                if (arg.Value is INamedTypeSymbol attrTypeSymbol)
                {
                    fieldData.PropertyAttributes.Add(FormatAttributeName(attrTypeSymbol));
                }
                else if (arg.Value is string attrText)
                {
                    fieldData.PropertyAttributes.Add(attrText);
                }
            }

            classData.Fields.Add(fieldData);
        }
    }

    private static string FormatAttributeName(INamedTypeSymbol typeSymbol)
    {
        return typeSymbol.ToDisplayString();
    }

    private static ClassDeclarationSyntax? GetClassDeclaration(SyntaxNode node)
    {
        var parent = node.Parent;
        while (parent != null)
        {
            if (parent is ClassDeclarationSyntax classDeclaration)
                return classDeclaration;
            parent = parent.Parent;
        }
        return null;
    }
}
