using System;
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
    public INamedTypeSymbol? IVersion { get; }
    public INamedTypeSymbol? IVersionSync { get; }
    public INamedTypeSymbol? VersionSyncList { get; }
    public INamedTypeSymbol? VersionSyncDictionary { get; }
    public INamedTypeSymbol? VersionSyncHashSet { get; }

    private VersionFieldKnownSymbols(Compilation compilation)
    {
        VersionFieldAttribute = compilation.GetTypeByMetadataName("ReactiveBinding.VersionFieldAttribute");
        IVersion = compilation.GetTypeByMetadataName("ReactiveBinding.IVersion");
        IVersionSync = compilation.GetTypeByMetadataName("ReactiveBinding.IVersionSync");
        VersionSyncList = compilation.GetTypeByMetadataName("ReactiveBinding.VersionSyncList`1");
        VersionSyncDictionary = compilation.GetTypeByMetadataName("ReactiveBinding.VersionSyncDictionary`2");
        VersionSyncHashSet = compilation.GetTypeByMetadataName("ReactiveBinding.VersionSyncHashSet`1");
    }

    public static VersionFieldKnownSymbols Create(Compilation compilation) => new(compilation);
}

/// <summary>Builds the complete VersionField model for one logical class from all partial declarations.</summary>
internal static class VersionFieldSyntaxReceiver
{
    public static VersionFieldClassData? BuildClassData(
        Compilation compilation,
        INamedTypeSymbol classSymbol,
        VersionFieldKnownSymbols knownSymbols,
        Action<Diagnostic> reportDiagnostic,
        System.Threading.CancellationToken cancellationToken)
    {
        VersionFieldClassData? classData = null;

        foreach (var declaration in IncrementalGeneratorHelper.GetOrderedDeclarations(
                     classSymbol, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var semanticModel = compilation.GetSemanticModel(declaration.SyntaxTree);
            foreach (var fieldDeclaration in declaration.Members
                         .OfType<FieldDeclarationSyntax>()
                         .Where(static field => field.AttributeLists.Count > 0))
            {
                cancellationToken.ThrowIfCancellationRequested();
                classData = ProcessFieldDeclaration(
                    semanticModel,
                    fieldDeclaration,
                    knownSymbols,
                    classData,
                    reportDiagnostic,
                    cancellationToken);
            }
        }

        return classData;
    }

    private static VersionFieldClassData? ProcessFieldDeclaration(
        SemanticModel semanticModel,
        FieldDeclarationSyntax fieldDeclaration,
        VersionFieldKnownSymbols knownSymbols,
        VersionFieldClassData? classData,
        Action<Diagnostic> reportDiagnostic,
        System.Threading.CancellationToken cancellationToken)
    {
        foreach (var variable in fieldDeclaration.Declaration.Variables)
        {
            if (semanticModel.GetDeclaredSymbol(variable, cancellationToken) is not IFieldSymbol fieldSymbol)
                continue;

            var attributes = fieldSymbol.GetAttributes();
            if (!attributes.Any(attribute => SymbolEqualityComparer.Default.Equals(
                    attribute.AttributeClass, knownSymbols.VersionFieldAttribute)))
                continue;

            var classDeclaration = GetClassDeclaration(fieldDeclaration);
            if (classDeclaration == null) continue;

            classData ??= new VersionFieldClassData
            {
                ClassSymbol = fieldSymbol.ContainingType,
                ClassDeclaration = classDeclaration
            };

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

            // Collect the preferred [VersionProperty: Attribute(...)] target syntax. C# does not
            // include an unrecognized target list in IFieldSymbol.GetAttributes(), so bind it from
            // AttributeSyntax and render its values into self-contained generated source.
            foreach (var attributeList in fieldDeclaration.AttributeLists)
            {
                if (!VersionPropertyAttributeFormatter.IsVersionPropertyTarget(attributeList))
                    continue;

                foreach (var attribute in attributeList.Attributes)
                {
                    if (VersionPropertyAttributeFormatter.TryFormat(
                            semanticModel,
                            attribute,
                            out var propertyAttribute,
                            out var error))
                    {
                        fieldData.PropertyAttributes.Add(propertyAttribute);
                    }
                    else
                    {
                        reportDiagnostic(Diagnostic.Create(
                            DiagnosticDescriptors.VF10013_InvalidVersionPropertyAttribute,
                            attribute.GetLocation(),
                            attribute.ToString(),
                            propertyName,
                            error));
                    }
                }
            }

            classData.Fields.Add(fieldData);
        }

        return classData;
    }

    private static ClassDeclarationSyntax? GetClassDeclaration(SyntaxNode node)
    {
        return node.Parent as ClassDeclarationSyntax;
    }
}
