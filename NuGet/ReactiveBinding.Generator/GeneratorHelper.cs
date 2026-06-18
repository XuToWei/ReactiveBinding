using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace ReactiveBinding.Generator;

internal static class GeneratorHelper
{
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
            parts.Add(outer.Name);
        }
        parts.Add(classSymbol.Name);
        return string.Join(".", parts);
    }
}
