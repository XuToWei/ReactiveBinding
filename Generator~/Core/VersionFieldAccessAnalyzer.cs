using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ReactiveBinding.Generator;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class VersionFieldAccessAnalyzer : DiagnosticAnalyzer
{
    private const string VersionFieldAttributeName = "ReactiveBinding.VersionFieldAttribute";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.VF3002_DirectFieldAccess);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeIdentifier, SyntaxKind.IdentifierName);
    }

    private void AnalyzeIdentifier(SyntaxNodeAnalysisContext context)
    {
        var identifierName = (IdentifierNameSyntax)context.Node;

        // Quick filter: only check m_ prefixed identifiers
        var name = identifierName.Identifier.Text;
        if (!name.StartsWith("m_") || name.Length <= 2)
            return;

        // Skip if this is part of a field declaration (the declaration itself is fine)
        if (identifierName.Parent is VariableDeclaratorSyntax)
            return;

        // Skip if this is the attribute target (e.g., [VersionField] on the field)
        if (identifierName.Parent is AttributeSyntax)
            return;

        // Resolve the symbol
        var symbolInfo = context.SemanticModel.GetSymbolInfo(identifierName);
        var symbol = symbolInfo.Symbol;
        if (symbol is not IFieldSymbol fieldSymbol)
            return;

        // Check if the field has [VersionField] attribute
        var hasVersionFieldAttr = fieldSymbol.GetAttributes()
            .Any(a => a.AttributeClass?.ToDisplayString() == VersionFieldAttributeName);

        if (!hasVersionFieldAttr)
            return;

        // This is a direct access to a [VersionField] field in user code - report it
        var propertyName = ConvertToPropertyName(name);
        context.ReportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.VF3002_DirectFieldAccess,
            identifierName.GetLocation(),
            name,
            propertyName));
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
