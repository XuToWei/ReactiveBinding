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

        // Collect all local variable names to handle shadowing
        var localVariables = CollectLocalVariables(body);

        foreach (var node in body.DescendantNodesAndSelf())
        {
            string? memberName = null;

            switch (node)
            {
                case IdentifierNameSyntax identifier:
                    // Direct access like: Health, _health
                    memberName = TryResolveMember(identifier, semanticModel, containingType, localVariables);
                    break;

                case MemberAccessExpressionSyntax memberAccess when memberAccess.Expression is ThisExpressionSyntax:
                    // this.Health, this._health
                    memberName = TryResolveMember(memberAccess.Name, semanticModel, containingType, localVariables);
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

    /// <summary>
    /// Collects all local variable names declared in the given syntax node.
    /// </summary>
    private static HashSet<string> CollectLocalVariables(SyntaxNode body)
    {
        var localVariables = new HashSet<string>();

        foreach (var node in body.DescendantNodes())
        {
            switch (node)
            {
                case VariableDeclaratorSyntax variableDeclarator:
                    localVariables.Add(variableDeclarator.Identifier.Text);
                    break;

                case SingleVariableDesignationSyntax designation:
                    localVariables.Add(designation.Identifier.Text);
                    break;

                case ForEachStatementSyntax forEach:
                    localVariables.Add(forEach.Identifier.Text);
                    break;

                case ParameterSyntax parameter:
                    // Lambda/local function parameters
                    localVariables.Add(parameter.Identifier.Text);
                    break;
            }
        }

        return localVariables;
    }

    /// <summary>
    /// Tries to resolve an identifier to a member of the containing type.
    /// Returns null if it's a local variable, parameter, or not a member.
    /// </summary>
    private static string? TryResolveMember(
        SimpleNameSyntax nameSyntax,
        SemanticModel semanticModel,
        INamedTypeSymbol containingType,
        HashSet<string> localVariables)
    {
        var name = nameSyntax.Identifier.Text;

        // Skip if it's a local variable (shadowing)
        if (localVariables.Contains(name))
        {
            return null;
        }

        var symbolInfo = semanticModel.GetSymbolInfo(nameSyntax);
        var symbol = symbolInfo.Symbol;

        if (symbol == null)
        {
            return null;
        }

        // Check if this is a member of the containing type
        if (!SymbolEqualityComparer.Default.Equals(symbol.ContainingType, containingType))
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

        if (symbol == null)
        {
            return null;
        }

        // Check if this is a method of the containing type
        if (!SymbolEqualityComparer.Default.Equals(symbol.ContainingType, containingType))
        {
            return null;
        }

        return symbol.Name;
    }
}
