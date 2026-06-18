using System.Collections.Immutable;
using System.Linq;
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
        ImmutableArray.Create(DiagnosticDescriptors.VF3003_FieldHasInitializer);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeFieldDeclaration, SyntaxKind.FieldDeclaration);
    }

    private void AnalyzeFieldDeclaration(SyntaxNodeAnalysisContext context)
    {
        var fieldDeclaration = (FieldDeclarationSyntax)context.Node;

        foreach (var variable in fieldDeclaration.Declaration.Variables)
        {
            if (variable.Initializer == null)
                continue;

            if (context.SemanticModel.GetDeclaredSymbol(variable) is not IFieldSymbol fieldSymbol)
                continue;

            var hasVersionFieldAttr = fieldSymbol.GetAttributes()
                .Any(a => a.AttributeClass?.ToDisplayString() == VersionFieldAttributeName);

            if (!hasVersionFieldAttr)
                continue;

            var propertyName = ConvertToPropertyName(fieldSymbol.Name);
            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.VF3003_FieldHasInitializer,
                variable.Initializer.GetLocation(),
                fieldSymbol.Name,
                propertyName));
        }
    }

    private static string ConvertToPropertyName(string fieldName)
    {
        if (fieldName.StartsWith("m_") && fieldName.Length > 2)
        {
            return char.ToUpper(fieldName[2]) + fieldName.Substring(3);
        }
        return fieldName;
    }
}
