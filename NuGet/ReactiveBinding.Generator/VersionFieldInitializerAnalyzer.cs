using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ReactiveBinding.Generator;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class VersionFieldInitializerAnalyzer : DiagnosticAnalyzer
{
    private const string VersionFieldAttributeName = "ReactiveBinding.VersionFieldAttribute";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.VF10011_FieldHasInitializer);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(startContext =>
        {
            var versionFieldAttribute = startContext.Compilation.GetTypeByMetadataName(VersionFieldAttributeName);
            if (versionFieldAttribute == null)
                return;

            startContext.RegisterSyntaxNodeAction(
                syntaxContext => AnalyzeFieldDeclaration(syntaxContext, versionFieldAttribute),
                SyntaxKind.FieldDeclaration);
        });
    }

    private static void AnalyzeFieldDeclaration(
        SyntaxNodeAnalysisContext context,
        INamedTypeSymbol versionFieldAttribute)
    {
        var fieldDeclaration = (FieldDeclarationSyntax)context.Node;

        foreach (var variable in fieldDeclaration.Declaration.Variables)
        {
            if (variable.Initializer == null)
                continue;

            if (context.SemanticModel.GetDeclaredSymbol(variable) is not IFieldSymbol fieldSymbol)
                continue;

            if (!GeneratorHelper.HasAttribute(fieldSymbol, versionFieldAttribute))
                continue;

            var propertyName = GeneratorHelper.ConvertVersionFieldToPropertyName(fieldSymbol.Name);
            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.VF10011_FieldHasInitializer,
                variable.Initializer.GetLocation(),
                fieldSymbol.Name,
                propertyName));
        }
    }

}
