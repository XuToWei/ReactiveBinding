using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ReactiveBinding.Generator;

/// <summary>
/// Reports VS2002 when a field is marked [VersionSync] but not [VersionField].
/// Synchronization hooks the generated property setter, which only exists for [VersionField] fields.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class VersionSyncFieldAnalyzer : DiagnosticAnalyzer
{
    private const string VersionFieldAttributeName = "ReactiveBinding.VersionFieldAttribute";
    private const string VersionSyncAttributeName = "ReactiveBinding.VersionSyncAttribute";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.VS2002_SyncWithoutVersionField);

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
            if (context.SemanticModel.GetDeclaredSymbol(variable) is not IFieldSymbol fieldSymbol)
                continue;

            var attrs = fieldSymbol.GetAttributes();
            bool hasSync = attrs.Any(a => a.AttributeClass?.ToDisplayString() == VersionSyncAttributeName);
            if (!hasSync)
                continue;

            bool hasVersionField = attrs.Any(a => a.AttributeClass?.ToDisplayString() == VersionFieldAttributeName);
            if (hasVersionField)
                continue;

            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.VS2002_SyncWithoutVersionField,
                variable.Identifier.GetLocation(),
                fieldSymbol.Name));
        }
    }
}
