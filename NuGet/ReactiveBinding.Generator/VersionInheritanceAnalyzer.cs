using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ReactiveBinding.Generator;

/// <summary>Rejects IVersion/IVersionSync inheritance even when the derived class declares no VersionField.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class VersionInheritanceAnalyzer : DiagnosticAnalyzer
{
    private const string IVersionInterfaceName = "ReactiveBinding.IVersion";
    private const string VersionFieldAttributeName = "ReactiveBinding.VersionFieldAttribute";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.VF10003_VersionInheritanceNotSupported);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(startContext =>
        {
            var versionType = startContext.Compilation.GetTypeByMetadataName(IVersionInterfaceName);
            var versionFieldAttribute = startContext.Compilation.GetTypeByMetadataName(VersionFieldAttributeName);
            if (versionType == null || versionFieldAttribute == null)
                return;

            startContext.RegisterSymbolAction(
                symbolContext => AnalyzeNamedType(symbolContext, versionType, versionFieldAttribute),
                SymbolKind.NamedType);
        });
    }

    private static void AnalyzeNamedType(
        SymbolAnalysisContext context,
        INamedTypeSymbol versionType,
        INamedTypeSymbol versionFieldAttribute)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (type.TypeKind != TypeKind.Class || type.Locations.All(l => !l.IsInSource))
            return;

        var baseType = type.BaseType;
        if (baseType == null || baseType.SpecialType == SpecialType.System_Object
            || !GeneratorHelper.IsOrImplementsInterface(baseType, versionType))
            return;

        // VersionFieldGenerator reports the same diagnostic for generated classes. This analyzer covers the
        // otherwise invisible case where a derived class declares no fields, without producing duplicates.
        bool hasVersionField = type.GetMembers().OfType<IFieldSymbol>().Any(field =>
            GeneratorHelper.HasAttribute(field, versionFieldAttribute));
        if (hasVersionField) return;

        var location = type.Locations.First(l => l.IsInSource);
        context.ReportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.VF10003_VersionInheritanceNotSupported,
            location,
            type.Name,
            baseType.ToDisplayString()));
    }
}
