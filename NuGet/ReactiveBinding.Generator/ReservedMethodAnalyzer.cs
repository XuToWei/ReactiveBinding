using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ReactiveBinding.Generator;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class ReservedMethodAnalyzer : DiagnosticAnalyzer
{
    private const string IReactiveObserverName = "ReactiveBinding.IReactiveObserver";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            DiagnosticDescriptors.RB1005_ManualObserveChanges,
            DiagnosticDescriptors.RB1006_ManualResetChanges);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeMethodDeclaration, SyntaxKind.MethodDeclaration);
    }

    private void AnalyzeMethodDeclaration(SyntaxNodeAnalysisContext context)
    {
        var methodDeclaration = (MethodDeclarationSyntax)context.Node;
        var methodName = methodDeclaration.Identifier.Text;

        if (methodName != "ObserveChanges" && methodName != "ResetChanges")
            return;

        // Must be parameterless
        if (methodDeclaration.ParameterList.Parameters.Count > 0)
            return;

        // Check containing class implements IReactiveObserver (directly or inherited)
        var containingClass = methodDeclaration.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault();
        if (containingClass == null)
            return;

        var classSymbol = context.SemanticModel.GetDeclaredSymbol(containingClass);
        if (classSymbol == null)
            return;

        if (!classSymbol.AllInterfaces.Any(i => i.ToDisplayString() == IReactiveObserverName))
            return;

        var descriptor = methodName == "ObserveChanges"
            ? DiagnosticDescriptors.RB1005_ManualObserveChanges
            : DiagnosticDescriptors.RB1006_ManualResetChanges;

        context.ReportDiagnostic(Diagnostic.Create(
            descriptor,
            methodDeclaration.Identifier.GetLocation(),
            methodName,
            classSymbol.Name));
    }
}
