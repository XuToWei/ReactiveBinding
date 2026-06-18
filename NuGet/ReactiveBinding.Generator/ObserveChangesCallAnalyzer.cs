using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ReactiveBinding.Generator;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class ObserveChangesCallAnalyzer : DiagnosticAnalyzer
{
    private const string IReactiveObserverName = "ReactiveBinding.IReactiveObserver";
    private const string ReactiveObserveIgnoreName = "ReactiveBinding.ReactiveObserveIgnoreAttribute";
    private const string ReactiveSourceAttributeName = "ReactiveBinding.ReactiveSourceAttribute";
    private const string ReactiveBindAttributeName = "ReactiveBinding.ReactiveBindAttribute";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.RB0003_ObserveChangesNotCalled);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeClassDeclaration, SyntaxKind.ClassDeclaration);
    }

    private void AnalyzeClassDeclaration(SyntaxNodeAnalysisContext context)
    {
        var classDeclaration = (ClassDeclarationSyntax)context.Node;
        var classSymbol = context.SemanticModel.GetDeclaredSymbol(classDeclaration);
        if (classSymbol == null)
            return;

        // Only process the first declaration to avoid duplicate diagnostics for partial classes
        var firstDeclaration = classSymbol.DeclaringSyntaxReferences.FirstOrDefault();
        if (firstDeclaration?.GetSyntax(context.CancellationToken) != classDeclaration)
            return;

        // Must implement IReactiveObserver
        if (!classSymbol.AllInterfaces.Any(i => i.ToDisplayString() == IReactiveObserverName))
            return;

        // Skip if has [ReactiveObserveIgnore]
        if (classSymbol.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == ReactiveObserveIgnoreName))
            return;

        // Skip if base class implements IReactiveObserver (base is responsible for calling)
        if (HasBaseWithReactiveObserver(classSymbol))
            return;

        // Must have reactive members (ReactiveSource or ReactiveBind)
        if (!HasReactiveMembers(classSymbol))
            return;

        // Check all partial declarations for ObserveChanges() invocation
        foreach (var syntaxRef in classSymbol.DeclaringSyntaxReferences)
        {
            var syntax = syntaxRef.GetSyntax(context.CancellationToken);
            if (HasObserveChangesCall(syntax))
                return;
        }

        // No ObserveChanges() call found
        context.ReportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.RB0003_ObserveChangesNotCalled,
            classDeclaration.Identifier.GetLocation(),
            classSymbol.Name));
    }

    private static bool HasBaseWithReactiveObserver(INamedTypeSymbol classSymbol)
    {
        var baseType = classSymbol.BaseType;
        while (baseType != null && baseType.SpecialType != SpecialType.System_Object)
        {
            if (baseType.AllInterfaces.Any(i => i.ToDisplayString() == IReactiveObserverName))
                return true;
            baseType = baseType.BaseType;
        }
        return false;
    }

    private static bool HasReactiveMembers(INamedTypeSymbol classSymbol)
    {
        foreach (var member in classSymbol.GetMembers())
        {
            if (member.GetAttributes().Any(a =>
                a.AttributeClass?.ToDisplayString() == ReactiveSourceAttributeName ||
                a.AttributeClass?.ToDisplayString() == ReactiveBindAttributeName))
                return true;
        }
        return false;
    }

    private static bool HasObserveChangesCall(SyntaxNode classSyntax)
    {
        // Search for ObserveChanges() invocations, but skip nested class declarations
        foreach (var node in classSyntax.DescendantNodes(n => n is not ClassDeclarationSyntax || n == classSyntax))
        {
            if (node is InvocationExpressionSyntax invocation)
            {
                var name = invocation.Expression switch
                {
                    IdentifierNameSyntax id => id.Identifier.Text,
                    MemberAccessExpressionSyntax ma => ma.Name.Identifier.Text,
                    _ => null
                };
                if (name == "ObserveChanges")
                    return true;
            }
        }
        return false;
    }
}
