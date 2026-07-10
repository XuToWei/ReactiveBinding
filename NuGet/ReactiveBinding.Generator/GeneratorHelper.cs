using System.Collections.Generic;
using System.Linq;
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

    public static bool IsOrImplementsInterface(ITypeSymbol type, string fullyQualifiedInterfaceName)
    {
        return type.ToDisplayString() == fullyQualifiedInterfaceName
            || type.AllInterfaces.Any(i => i.ToDisplayString() == fullyQualifiedInterfaceName);
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
            parts.Add(ns);
        }
        foreach (var outer in GetContainingTypes(classSymbol))
        {
            parts.Add(outer.MetadataName.Replace('`', '_'));
        }
        parts.Add(classSymbol.MetadataName.Replace('`', '_'));
        return string.Join(".", parts);
    }
}
