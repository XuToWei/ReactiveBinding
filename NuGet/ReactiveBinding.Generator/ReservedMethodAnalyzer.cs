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
            DiagnosticDescriptors.RB10005_ManualObserveChanges,
            DiagnosticDescriptors.RB10006_ManualResetChanges);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(startContext =>
        {
            var reactiveObserver = startContext.Compilation.GetTypeByMetadataName(IReactiveObserverName);
            if (reactiveObserver == null)
                return;

            startContext.RegisterSyntaxNodeAction(
                syntaxContext => AnalyzeMethodDeclaration(syntaxContext, reactiveObserver),
                SyntaxKind.MethodDeclaration);
        });
    }

    private static void AnalyzeMethodDeclaration(
        SyntaxNodeAnalysisContext context,
        INamedTypeSymbol reactiveObserver)
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

        if (!GeneratorHelper.IsOrImplementsInterface(classSymbol, reactiveObserver))
            return;

        var descriptor = methodName == "ObserveChanges"
            ? DiagnosticDescriptors.RB10005_ManualObserveChanges
            : DiagnosticDescriptors.RB10006_ManualResetChanges;

        context.ReportDiagnostic(Diagnostic.Create(
            descriptor,
            methodDeclaration.Identifier.GetLocation(),
            methodName,
            classSymbol.Name));
    }
}
