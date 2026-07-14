using System.Collections.Immutable;
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
        ImmutableArray.Create(DiagnosticDescriptors.VF10010_DirectFieldAccess);

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
                syntaxContext => AnalyzeIdentifier(syntaxContext, versionFieldAttribute),
                SyntaxKind.IdentifierName);
        });
    }

    private static void AnalyzeIdentifier(
        SyntaxNodeAnalysisContext context,
        INamedTypeSymbol versionFieldAttribute)
    {
        var identifierName = (IdentifierNameSyntax)context.Node;

        // Quick filter: only check __ prefixed identifiers
        var name = identifierName.Identifier.ValueText;
        if (!name.StartsWith("__") || name.Length <= 2)
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
        if (!GeneratorHelper.HasAttribute(fieldSymbol, versionFieldAttribute))
            return;

        // This is a direct access to a [VersionField] field in user code - report it
        var propertyName = GeneratorHelper.ConvertVersionFieldToPropertyName(name);
        context.ReportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.VF10010_DirectFieldAccess,
            identifierName.GetLocation(),
            name,
            propertyName));
    }

}
