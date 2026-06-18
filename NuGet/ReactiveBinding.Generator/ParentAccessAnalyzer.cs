using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ReactiveBinding.Generator;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class ParentAccessAnalyzer : DiagnosticAnalyzer
{
    private const string IVersionInterfaceName = "ReactiveBinding.IVersion";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.VF3001_ParentAccessNotAllowed);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeMemberAccess, SyntaxKind.SimpleMemberAccessExpression);
    }

    private void AnalyzeMemberAccess(SyntaxNodeAnalysisContext context)
    {
        var memberAccess = (MemberAccessExpressionSyntax)context.Node;

        // Check if accessing "Parent" property
        if (memberAccess.Name.Identifier.Text != "Parent")
            return;

        // Get the type of the expression being accessed
        var typeInfo = context.SemanticModel.GetTypeInfo(memberAccess.Expression);
        var type = typeInfo.Type;

        if (type == null)
            return;

        // Check if the type implements IVersion
        if (!ImplementsIVersion(type))
            return;

        // Check if this is in generated code (allow generated code to access Parent)
        var sourceTree = memberAccess.SyntaxTree;
        var filePath = sourceTree.FilePath ?? "";
        if (filePath.EndsWith(".g.cs") || filePath.Contains("Generated"))
            return;

        // Check if this is inside a class that implements IVersion or its nested classes (internal use allowed)
        foreach (var containingClass in memberAccess.Ancestors().OfType<ClassDeclarationSyntax>())
        {
            var classSymbol = context.SemanticModel.GetDeclaredSymbol(containingClass);
            if (classSymbol != null && ImplementsIVersion(classSymbol))
                return;

            // Also allow in VersionList/VersionHashSet/VersionDictionary classes
            var className = containingClass.Identifier.Text;
            if (className == "VersionList" || className == "VersionHashSet" || className == "VersionDictionary")
                return;
        }

        // Report diagnostic
        var diagnostic = Diagnostic.Create(
            DiagnosticDescriptors.VF3001_ParentAccessNotAllowed,
            memberAccess.GetLocation());

        context.ReportDiagnostic(diagnostic);
    }

    private bool ImplementsIVersion(ITypeSymbol type)
    {
        // Check if type itself is IVersion
        if (type.ToDisplayString() == IVersionInterfaceName)
            return true;

        // Check interfaces
        foreach (var iface in type.AllInterfaces)
        {
            if (iface.ToDisplayString() == IVersionInterfaceName)
                return true;
        }

        return false;
    }
}
