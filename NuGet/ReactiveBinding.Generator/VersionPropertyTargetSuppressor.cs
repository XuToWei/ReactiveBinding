using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ReactiveBinding.Generator;

/// <summary>
/// Suppresses the compiler's unknown attribute-target warning for the
/// <c>[VersionProperty: ...]</c> syntax owned by <see cref="VersionFieldGenerator"/>.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class VersionPropertyTargetSuppressor : DiagnosticSuppressor
{
    private const string VersionFieldAttributeName = "ReactiveBinding.VersionFieldAttribute";

    private static readonly SuppressionDescriptor VersionPropertyTarget = new(
        id: "SPR1001",
        suppressedDiagnosticId: "CS0658",
        justification: "VersionProperty target lists are consumed by the ReactiveBinding source generator");

    public override ImmutableArray<SuppressionDescriptor> SupportedSuppressions
        => ImmutableArray.Create(VersionPropertyTarget);

    public override void ReportSuppressions(SuppressionAnalysisContext context)
    {
        var versionFieldAttribute = context.Compilation.GetTypeByMetadataName(VersionFieldAttributeName);
        if (versionFieldAttribute == null)
            return;

        foreach (var diagnostic in context.ReportedDiagnostics)
        {
            if (diagnostic.Id != "CS0658" || !diagnostic.Location.IsInSource)
                continue;

            var syntaxTree = diagnostic.Location.SourceTree;
            if (syntaxTree == null)
                continue;

            var root = syntaxTree.GetRoot(context.CancellationToken);
            var target = root.FindToken(diagnostic.Location.SourceSpan.Start)
                .Parent?
                .AncestorsAndSelf()
                .OfType<AttributeTargetSpecifierSyntax>()
                .FirstOrDefault();

            if (target == null || !string.Equals(
                    target.Identifier.ValueText,
                    VersionPropertyAttributeFormatter.TargetName,
                    System.StringComparison.Ordinal))
            {
                continue;
            }

            if (target.Parent is not AttributeListSyntax { Parent: FieldDeclarationSyntax field })
                continue;

            if (!VersionFieldSyntaxReceiver.IsSupportedFieldDeclaration(field))
                continue;

            var semanticModel = context.GetSemanticModel(syntaxTree);
            bool hasVersionField = field.Declaration.Variables.Any(variable =>
                semanticModel.GetDeclaredSymbol(variable, context.CancellationToken) is IFieldSymbol fieldSymbol
                && fieldSymbol.GetAttributes().Any(attribute =>
                    SymbolEqualityComparer.Default.Equals(
                        attribute.AttributeClass,
                        versionFieldAttribute)));

            if (hasVersionField)
                context.ReportSuppression(Suppression.Create(VersionPropertyTarget, diagnostic));
        }
    }
}
