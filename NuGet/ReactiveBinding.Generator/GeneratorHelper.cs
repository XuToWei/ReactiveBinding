using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ReactiveBinding.Generator;

internal static class GeneratorHelper
{
    public static string EscapeIdentifier(string identifier)
    {
        return SyntaxFacts.GetKeywordKind(identifier) != SyntaxKind.None
            || SyntaxFacts.GetContextualKeywordKind(identifier) != SyntaxKind.None
            ? "@" + identifier
            : identifier;
    }

    public static string ConvertVersionFieldToPropertyName(string fieldName)
    {
        if (fieldName.StartsWith("__") && fieldName.Length > 2)
            return char.ToUpperInvariant(fieldName[2]) + fieldName.Substring(3);
        return fieldName;
    }

    public static bool IsOrImplementsInterface(ITypeSymbol type, string fullyQualifiedInterfaceName)
    {
        static string NonNullableName(ITypeSymbol symbol)
            => symbol.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString();

        return NonNullableName(type) == fullyQualifiedInterfaceName
            || type.AllInterfaces.Any(i => NonNullableName(i) == fullyQualifiedInterfaceName);
    }

    public static bool IsOrImplementsInterface(ITypeSymbol type, INamedTypeSymbol? interfaceSymbol)
    {
        if (interfaceSymbol == null)
            return false;

        return SymbolEqualityComparer.Default.Equals(type, interfaceSymbol)
            || type.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, interfaceSymbol));
    }

    public static bool HasAttribute(ISymbol symbol, INamedTypeSymbol? attributeSymbol)
    {
        if (attributeSymbol == null)
            return false;

        return symbol.GetAttributes().Any(attribute =>
            SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, attributeSymbol));
    }

    public static bool IsPartial(INamedTypeSymbol type)
    {
        return type.DeclaringSyntaxReferences.Length > 0
            && type.DeclaringSyntaxReferences.All(r =>
                r.GetSyntax() is TypeDeclarationSyntax declaration
                && declaration.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)));
    }

    public static Location GetIdentifierLocation(INamedTypeSymbol type, Location fallback)
    {
        foreach (var syntaxReference in type.DeclaringSyntaxReferences)
        {
            if (syntaxReference.GetSyntax() is TypeDeclarationSyntax declaration)
                return declaration.Identifier.GetLocation();
        }
        return fallback;
    }

    public static string GetTypeDeclaration(INamedTypeSymbol type)
    {
        bool isRecord = type.DeclaringSyntaxReferences.Any(r => r.GetSyntax() is RecordDeclarationSyntax);
        string kind = isRecord ? "record" : type.TypeKind switch
        {
            TypeKind.Struct => "struct",
            TypeKind.Interface => "interface",
            _ => "class"
        };

        var parameters = type.TypeParameters.Length == 0
            ? ""
            : "<" + string.Join(", ", type.TypeParameters.Select(p => EscapeIdentifier(p.Name))) + ">";
        return $"{kind} {EscapeIdentifier(type.Name)}{parameters}";
    }

    public static IEnumerable<string> GetTypeParameterConstraints(INamedTypeSymbol type)
    {
        foreach (var parameter in type.TypeParameters)
        {
            var constraints = new List<string>();
            if (parameter.HasUnmanagedTypeConstraint)
                constraints.Add("unmanaged");
            else if (parameter.HasValueTypeConstraint)
                constraints.Add("struct");
            else if (parameter.HasReferenceTypeConstraint)
                constraints.Add(parameter.ReferenceTypeConstraintNullableAnnotation == NullableAnnotation.Annotated
                    ? "class?"
                    : "class");
            else if (parameter.HasNotNullConstraint)
                constraints.Add("notnull");

            constraints.AddRange(parameter.ConstraintTypes.Select(t => t.ToDisplayString()));
            if (parameter.HasConstructorConstraint && !parameter.HasValueTypeConstraint && !parameter.HasUnmanagedTypeConstraint)
                constraints.Add("new()");

            if (constraints.Count > 0)
                yield return $"where {EscapeIdentifier(parameter.Name)} : {string.Join(", ", constraints)}";
        }
    }

    public static string GetNonNullableTypeName(ITypeSymbol type)
    {
        var format = new SymbolDisplayFormat(
            globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
            genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
            miscellaneousOptions: SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers
                                | SymbolDisplayMiscellaneousOptions.UseSpecialTypes);
        return type.ToDisplayString(format);
    }

    public static List<INamedTypeSymbol> GetContainingTypes(INamedTypeSymbol classSymbol)
    {
        var containingTypes = new List<INamedTypeSymbol>();
        var current = classSymbol.ContainingType;
        while (current != null)
        {
            containingTypes.Add(current);
            current = current.ContainingType;
        }
        containingTypes.Reverse();
        return containingTypes;
    }

    public static string GetFullTypeName(INamedTypeSymbol classSymbol)
    {
        var parts = new List<string>();
        var ns = classSymbol.ContainingNamespace.ToDisplayString();
        if (!string.IsNullOrEmpty(ns) && ns != "<global namespace>")
        {
            parts.Add("N_" + EncodeHintNameSegment(ns));
        }
        foreach (var outer in GetContainingTypes(classSymbol))
        {
            parts.Add("T_" + EncodeHintNameSegment(outer.MetadataName));
        }
        parts.Add("T_" + EncodeHintNameSegment(classSymbol.MetadataName));
        return string.Join(".", parts);
    }

    private static string EncodeHintNameSegment(string value)
    {
        var result = new StringBuilder(value.Length);
        foreach (char c in value)
        {
            if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9'))
                result.Append(c);
            else
                result.Append('_').Append(((int)c).ToString("X4"));
        }
        return result.ToString();
    }
}
