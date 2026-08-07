using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ReactiveBinding.Generator;

/// <summary>Rejects IVersion reference types used as keys of ReactiveBinding version dictionaries.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class VersionDictionaryKeyAnalyzer : DiagnosticAnalyzer
{
    private const string IVersionInterfaceName = "ReactiveBinding.IVersion";
    private const string VersionDictionaryName = "ReactiveBinding.VersionDictionary`2";
    private const string VersionSyncDictionaryName = "ReactiveBinding.VersionSyncDictionary`2";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.VF10014_VersionDictionaryKeyCannotBeVersionReference);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(startContext =>
        {
            var versionType = startContext.Compilation.GetTypeByMetadataName(IVersionInterfaceName);
            var versionDictionary = startContext.Compilation.GetTypeByMetadataName(VersionDictionaryName);
            var versionSyncDictionary = startContext.Compilation.GetTypeByMetadataName(VersionSyncDictionaryName);
            if (versionType == null || (versionDictionary == null && versionSyncDictionary == null))
                return;

            startContext.RegisterSyntaxNodeAction(
                syntaxContext => AnalyzeGenericName(
                    syntaxContext,
                    versionType,
                    versionDictionary,
                    versionSyncDictionary),
                SyntaxKind.GenericName);
        });
    }

    private static void AnalyzeGenericName(
        SyntaxNodeAnalysisContext context,
        INamedTypeSymbol versionType,
        INamedTypeSymbol? versionDictionary,
        INamedTypeSymbol? versionSyncDictionary)
    {
        var genericName = (GenericNameSyntax)context.Node;
        if (genericName.TypeArgumentList.Arguments.Count != 2
            || context.SemanticModel.GetTypeInfo(genericName, context.CancellationToken).Type is not INamedTypeSymbol constructedType)
            return;

        var definition = constructedType.OriginalDefinition;
        if (!SymbolEqualityComparer.Default.Equals(definition, versionDictionary)
            && !SymbolEqualityComparer.Default.Equals(definition, versionSyncDictionary))
            return;

        var keyType = constructedType.TypeArguments[0];
        if (!keyType.IsReferenceType || !GeneratorHelper.IsOrImplementsInterface(keyType, versionType))
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.VF10014_VersionDictionaryKeyCannotBeVersionReference,
            genericName.TypeArgumentList.Arguments[0].GetLocation(),
            keyType.ToDisplayString(),
            definition.Name));
    }
}
