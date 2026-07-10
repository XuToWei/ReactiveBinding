using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ReactiveBinding.Generator;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class ParentAccessAnalyzer : DiagnosticAnalyzer
{
    private const string IVersionInterfaceName = "ReactiveBinding.IVersion";
    private const string VersionOwnershipTypeName = "ReactiveBinding.VersionOwnership";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.VF30001_ParentAccessNotAllowed);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(
            AnalyzeMemberAccess,
            SyntaxKind.SimpleMemberAccessExpression,
            SyntaxKind.MemberBindingExpression);
    }

    private void AnalyzeMemberAccess(SyntaxNodeAnalysisContext context)
    {
        SimpleNameSyntax name;
        ExpressionSyntax? receiver;
        if (context.Node is MemberAccessExpressionSyntax memberAccess)
        {
            name = memberAccess.Name;
            receiver = memberAccess.Expression;
        }
        else if (context.Node is MemberBindingExpressionSyntax memberBinding)
        {
            name = memberBinding.Name;
            receiver = FindConditionalReceiver(memberBinding);
        }
        else
        {
            return;
        }

        if (name.Identifier.ValueText != "__Parent" || receiver == null)
            return;

        var receiverType = context.SemanticModel.GetTypeInfo(receiver, context.CancellationToken).Type;
        if (receiverType == null || !ImplementsIVersion(receiverType))
            return;

        // IVersion implementations and their nested helper types are allowed to maintain their own ownership links.
        for (var containingType = context.ContainingSymbol?.ContainingType;
             containingType != null;
             containingType = containingType.ContainingType)
        {
            if (ImplementsIVersion(containingType)
                || containingType.ToDisplayString() == VersionOwnershipTypeName)
                return;
        }

        var diagnostic = Diagnostic.Create(
            DiagnosticDescriptors.VF30001_ParentAccessNotAllowed,
            name.GetLocation());

        context.ReportDiagnostic(diagnostic);
    }

    private static ExpressionSyntax? FindConditionalReceiver(MemberBindingExpressionSyntax binding)
    {
        SyntaxNode? node = binding;
        while (node?.Parent != null)
        {
            if (node.Parent is ConditionalAccessExpressionSyntax conditional
                && conditional.WhenNotNull.Span.Contains(binding.Span))
                return conditional.Expression;
            node = node.Parent;
        }
        return null;
    }

    private static bool ImplementsIVersion(ITypeSymbol type)
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
