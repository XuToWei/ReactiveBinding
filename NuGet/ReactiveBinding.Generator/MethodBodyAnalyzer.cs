using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ReactiveBinding.Generator;

/// <summary>
/// Analyzes method bodies to find references to ReactiveSource members.
/// </summary>
internal static class MethodBodyAnalyzer
{
    /// <summary>
    /// Finds all ReactiveSource members referenced in the method body.
    /// </summary>
    /// <param name="methodSyntax">The method declaration to analyze</param>
    /// <param name="semanticModel">The semantic model for symbol resolution</param>
    /// <param name="sourceNames">Set of valid ReactiveSource member names</param>
    /// <param name="containingType">The containing type symbol</param>
    /// <returns>List of referenced source names, in order of first appearance</returns>
    public static List<string> FindReferencedSources(
        MethodDeclarationSyntax methodSyntax,
        SemanticModel semanticModel,
        HashSet<string> sourceNames,
        INamedTypeSymbol containingType)
    {
        var referencedSources = new List<string>();
        var seenSources = new HashSet<string>();

        // Get the method body - either block body or expression body
        SyntaxNode? body = methodSyntax.Body ?? (SyntaxNode?)methodSyntax.ExpressionBody?.Expression;

        if (body == null)
        {
            return referencedSources;
        }

        foreach (var node in body.DescendantNodesAndSelf())
        {
            if (IsInsideNameof(node)) continue;
            string? memberName = null;

            switch (node)
            {
                case IdentifierNameSyntax identifier
                    when !(identifier.Parent is MemberAccessExpressionSyntax access && access.Name == identifier)
                      && identifier.Parent is not MemberBindingExpressionSyntax:
                    // Direct access like: Health, _health
                    memberName = TryResolveMember(identifier, semanticModel, containingType);
                    break;

                case MemberAccessExpressionSyntax memberAccess
                    when memberAccess.Expression is ThisExpressionSyntax or BaseExpressionSyntax:
                    // this.Health, base.Health
                    memberName = TryResolveMember(memberAccess.Name, semanticModel, containingType);
                    break;

                case InvocationExpressionSyntax invocation:
                    // Method calls: GetHealth(), this.GetHealth()
                    memberName = TryResolveMethodCall(invocation, semanticModel, containingType);
                    break;
            }

            if (memberName != null && sourceNames.Contains(memberName) && !seenSources.Contains(memberName))
            {
                seenSources.Add(memberName);
                referencedSources.Add(memberName);
            }
        }

        return referencedSources;
    }

    private static bool IsInsideNameof(SyntaxNode node)
    {
        for (var current = node; current != null; current = current.Parent)
        {
            if (current is InvocationExpressionSyntax invocation
                && invocation.Expression is IdentifierNameSyntax name
                && name.Identifier.ValueText == "nameof") return true;
        }
        return false;
    }

    /// <summary>
    /// Tries to resolve an identifier to a member of the containing type.
    /// Returns null if it's a local variable, parameter, or not a member.
    /// </summary>
    private static string? TryResolveMember(
        SimpleNameSyntax nameSyntax,
        SemanticModel semanticModel,
        INamedTypeSymbol containingType)
    {
        var symbolInfo = semanticModel.GetSymbolInfo(nameSyntax);
        var symbol = symbolInfo.Symbol;

        // Fallback: if Symbol is null, try CandidateSymbols (can happen in compilations with errors)
        if (symbol == null && symbolInfo.CandidateSymbols.Length == 1)
        {
            symbol = symbolInfo.CandidateSymbols[0];
        }

        if (symbol == null)
        {
            return null;
        }

        // Check if this is a member of the containing type (or its base types)
        if (!IsMemberOfType(symbol, containingType))
        {
            return null;
        }

        // Return the member name for fields and properties
        return symbol switch
        {
            IFieldSymbol fieldSymbol => fieldSymbol.Name,
            IPropertySymbol propertySymbol => propertySymbol.Name,
            _ => null
        };
    }

    /// <summary>
    /// Checks if the symbol is a member of the given type or any of its base types.
    /// Uses both SymbolEqualityComparer and name-based fallback for robustness
    /// across different compilation phases.
    /// </summary>
    private static bool IsMemberOfType(ISymbol symbol, INamedTypeSymbol containingType)
    {
        var memberContainingType = symbol.ContainingType;
        if (memberContainingType == null)
        {
            return false;
        }

        // Walk the type hierarchy
        var current = containingType;
        while (current != null && current.SpecialType != SpecialType.System_Object)
        {
            if (SymbolEqualityComparer.Default.Equals(memberContainingType, current))
            {
                return true;
            }

            // Name-based fallback for cross-phase symbol comparison
            if (memberContainingType.ToDisplayString() == current.ToDisplayString())
            {
                return true;
            }

            current = current.BaseType;
        }

        return false;
    }

    /// <summary>
    /// Tries to resolve a method invocation to a method of the containing type.
    /// </summary>
    private static string? TryResolveMethodCall(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        INamedTypeSymbol containingType)
    {
        // Get the method being called
        var expression = invocation.Expression;

        // Handle: GetHealth() or this.GetHealth()
        SimpleNameSyntax? methodName = expression switch
        {
            IdentifierNameSyntax identifier => identifier,
            MemberAccessExpressionSyntax memberAccess when memberAccess.Expression is ThisExpressionSyntax
                => memberAccess.Name as SimpleNameSyntax,
            _ => null
        };

        if (methodName == null)
        {
            return null;
        }

        var symbolInfo = semanticModel.GetSymbolInfo(invocation);
        var symbol = symbolInfo.Symbol as IMethodSymbol;

        // Fallback: if Symbol is null, try CandidateSymbols
        if (symbol == null && symbolInfo.CandidateSymbols.Length == 1)
        {
            symbol = symbolInfo.CandidateSymbols[0] as IMethodSymbol;
        }

        if (symbol == null)
        {
            return null;
        }

        // Check if this is a method of the containing type (or its base types)
        if (!IsMemberOfType(symbol, containingType))
        {
            return null;
        }

        return symbol.Name;
    }
}
