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
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.RB10009_ObserveChangesNotCalled);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(startContext =>
        {
            var knownSymbols = ReactiveKnownSymbols.Create(startContext.Compilation);
            if (knownSymbols.IReactiveObserver == null)
                return;

            startContext.RegisterSyntaxNodeAction(
                syntaxContext => AnalyzeClassDeclaration(syntaxContext, knownSymbols),
                SyntaxKind.ClassDeclaration);
        });
    }

    private static void AnalyzeClassDeclaration(
        SyntaxNodeAnalysisContext context,
        ReactiveKnownSymbols knownSymbols)
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
        if (!GeneratorHelper.IsOrImplementsInterface(classSymbol, knownSymbols.IReactiveObserver))
            return;

        // Skip if has [ReactiveObserveIgnore]
        if (GeneratorHelper.HasAttribute(classSymbol, knownSymbols.ReactiveObserveIgnoreAttribute))
            return;

        // Skip if base class implements IReactiveObserver (base is responsible for calling)
        if (HasBaseWithReactiveObserver(classSymbol, knownSymbols.IReactiveObserver!))
            return;

        // Must have reactive members (ReactiveSource or ReactiveBind)
        if (!HasReactiveMembers(classSymbol, knownSymbols))
            return;

        var observeChangesMethod = knownSymbols.IReactiveObserver!
            .GetMembers("ObserveChanges")
            .OfType<IMethodSymbol>()
            .FirstOrDefault(method => method.Parameters.Length == 0 && method.ReturnsVoid);
        if (observeChangesMethod == null)
            return;

        // Check all partial declarations for ObserveChanges() invocation
        foreach (var syntaxRef in classSymbol.DeclaringSyntaxReferences)
        {
            var syntax = syntaxRef.GetSyntax(context.CancellationToken);
            // SyntaxNodeAnalysisContext only owns the current tree's SemanticModel. Other partial
            // declarations retain the exact syntax/arity check without requesting models ad hoc.
            var semanticModel = syntax.SyntaxTree == context.Node.SyntaxTree
                ? context.SemanticModel
                : null;
            if (HasObserveChangesCall(
                    syntax,
                    semanticModel,
                    classSymbol,
                    observeChangesMethod,
                    context.CancellationToken))
                return;
        }

        // No ObserveChanges() call found
        context.ReportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.RB10009_ObserveChangesNotCalled,
            classDeclaration.Identifier.GetLocation(),
            classSymbol.Name));
    }

    private static bool HasBaseWithReactiveObserver(
        INamedTypeSymbol classSymbol,
        INamedTypeSymbol reactiveObserver)
    {
        var baseType = classSymbol.BaseType;
        while (baseType != null && baseType.SpecialType != SpecialType.System_Object)
        {
            if (GeneratorHelper.IsOrImplementsInterface(baseType, reactiveObserver))
                return true;
            baseType = baseType.BaseType;
        }
        return false;
    }

    private static bool HasReactiveMembers(
        INamedTypeSymbol classSymbol,
        ReactiveKnownSymbols knownSymbols)
    {
        foreach (var member in classSymbol.GetMembers())
        {
            if (GeneratorHelper.HasAttribute(member, knownSymbols.ReactiveSourceAttribute)
                || GeneratorHelper.HasAttribute(member, knownSymbols.ReactiveBindAttribute))
                return true;
        }
        return false;
    }

    private static bool HasObserveChangesCall(
        SyntaxNode classSyntax,
        SemanticModel? semanticModel,
        INamedTypeSymbol classSymbol,
        IMethodSymbol observeChangesMethod,
        System.Threading.CancellationToken cancellationToken)
    {
        // Search for ObserveChanges() invocations, but skip nested class declarations
        foreach (var node in classSyntax.DescendantNodes(n => n is not ClassDeclarationSyntax || n == classSyntax))
        {
            if (node is InvocationExpressionSyntax invocation)
            {
                // A lambda/local-function body is not evidence that the call will run.
                if (invocation.Ancestors().Any(a => a is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax))
                    continue;

                if (invocation.ArgumentList.Arguments.Count != 0)
                    continue;

                bool callsCurrentInstance = invocation.Expression switch
                {
                    IdentifierNameSyntax id => id.Identifier.ValueText == "ObserveChanges",
                    MemberAccessExpressionSyntax ma => ma.Name.Identifier.ValueText == "ObserveChanges"
                        && ma.Expression is ThisExpressionSyntax,
                    _ => false
                };
                if (!callsCurrentInstance)
                    continue;

                if (semanticModel == null)
                    return true;

                var symbolInfo = semanticModel.GetSymbolInfo(invocation, cancellationToken);
                if (symbolInfo.Symbol is IMethodSymbol calledMethod)
                {
                    if (IsObserveChangesImplementation(classSymbol, observeChangesMethod, calledMethod))
                        return true;
                    continue;
                }

                if (symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().Any(candidate =>
                        IsObserveChangesImplementation(classSymbol, observeChangesMethod, candidate)))
                    return true;

                // Analyzer-only hosts may run before the source generator has supplied the method.
                // With no competing symbol, the exact parameterless current-instance syntax is valid evidence.
                if (symbolInfo.CandidateSymbols.Length == 0)
                    return true;
            }
        }
        return false;
    }

    private static bool IsObserveChangesImplementation(
        INamedTypeSymbol classSymbol,
        IMethodSymbol observeChangesMethod,
        IMethodSymbol calledMethod)
    {
        if (calledMethod.IsStatic || calledMethod.Parameters.Length != 0 || !calledMethod.ReturnsVoid)
            return false;

        var implementation = classSymbol.FindImplementationForInterfaceMember(observeChangesMethod) as IMethodSymbol;
        return implementation != null
            && SymbolEqualityComparer.Default.Equals(
                calledMethod.OriginalDefinition,
                implementation.OriginalDefinition);
    }
}
