using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace ReactiveBinding.Generator;

/// <summary>Protects ReactiveBinding's generated double-underscore protocol surface.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class VersionProtocolAccessAnalyzer : DiagnosticAnalyzer
{
    private const string IVersionName = "ReactiveBinding.IVersion";
    private const string IVersionSyncName = "ReactiveBinding.IVersionSync";
    private const string IReactiveObserverName = "ReactiveBinding.IReactiveObserver";
    private const string VersionCounterName = "ReactiveBinding.VersionCounter";
    private const string VersionFieldAttributeName = "ReactiveBinding.VersionFieldAttribute";

    private static readonly string[] AllowedCallerNames =
    {
        "ReactiveBinding.IVersion",
        "ReactiveBinding.IVersionSync",
        "ReactiveBinding.VersionOwnership",
        "ReactiveBinding.SyncContext",
        "ReactiveBinding.VersionList`1",
        "ReactiveBinding.VersionDictionary`2",
        "ReactiveBinding.VersionHashSet`1",
        "ReactiveBinding.VersionSyncList`1",
        "ReactiveBinding.VersionSyncDictionary`2",
        "ReactiveBinding.VersionSyncHashSet`1",
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.VF10012_InternalProtocolMemberAccess);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(startContext =>
        {
            var versionType = startContext.Compilation.GetTypeByMetadataName(IVersionName);
            var syncType = startContext.Compilation.GetTypeByMetadataName(IVersionSyncName);
            var reactiveObserverType = startContext.Compilation.GetTypeByMetadataName(IReactiveObserverName);
            var versionCounterType = startContext.Compilation.GetTypeByMetadataName(VersionCounterName);
            if (versionType == null && reactiveObserverType == null && versionCounterType == null) return;

            var versionFieldAttribute = startContext.Compilation.GetTypeByMetadataName(VersionFieldAttributeName);
            var reactiveGeneratedTrees = startContext.Compilation.SyntaxTrees
                .Where(IsReactiveBindGeneratedTree)
                .ToImmutableHashSet();
            var allowedCallers = AllowedCallerNames
                .Select(startContext.Compilation.GetTypeByMetadataName)
                .Where(type => type != null)
                .Cast<INamedTypeSymbol>()
                .ToImmutableArray();
            var protocolMembers = (versionType?.GetMembers() ?? ImmutableArray<ISymbol>.Empty)
                .Concat(syncType?.GetMembers() ?? ImmutableArray<ISymbol>.Empty)
                .Where(member => member.Name.StartsWith("__", StringComparison.Ordinal))
                .ToImmutableArray();

            startContext.RegisterOperationAction(c =>
            {
                var operation = (IFieldReferenceOperation)c.Operation;
                if (!HasVersionFieldAttribute(operation.Field, versionFieldAttribute))
                    Analyze(c, operation, operation.Field, operation.Instance?.Type,
                        versionType, versionCounterType, reactiveObserverType, protocolMembers,
                        reactiveGeneratedTrees, allowedCallers);
            }, OperationKind.FieldReference);
            startContext.RegisterOperationAction(c =>
            {
                var operation = (IPropertyReferenceOperation)c.Operation;
                Analyze(c, operation, operation.Property, operation.Instance?.Type,
                    versionType, versionCounterType, reactiveObserverType, protocolMembers,
                    reactiveGeneratedTrees, allowedCallers);
            }, OperationKind.PropertyReference);
            startContext.RegisterOperationAction(c =>
            {
                var operation = (IInvocationOperation)c.Operation;
                Analyze(c, operation, operation.TargetMethod, operation.Instance?.Type,
                    versionType, versionCounterType, reactiveObserverType, protocolMembers,
                    reactiveGeneratedTrees, allowedCallers);
            }, OperationKind.Invocation);
            startContext.RegisterOperationAction(c =>
            {
                var operation = (IMethodReferenceOperation)c.Operation;
                Analyze(c, operation, operation.Method, operation.Instance?.Type,
                    versionType, versionCounterType, reactiveObserverType, protocolMembers,
                    reactiveGeneratedTrees, allowedCallers);
            }, OperationKind.MethodReference);
        });
    }

    private static void Analyze(
        OperationAnalysisContext context,
        IOperation operation,
        ISymbol member,
        ITypeSymbol? receiverType,
        INamedTypeSymbol? versionType,
        INamedTypeSymbol? versionCounterType,
        INamedTypeSymbol? reactiveObserverType,
        ImmutableArray<ISymbol> protocolMembers,
        ImmutableHashSet<SyntaxTree> reactiveGeneratedTrees,
        ImmutableArray<INamedTypeSymbol> allowedCallers)
    {
        if (!member.Name.StartsWith("__", StringComparison.Ordinal)
            || IsInsideNameOf(operation)
            || IsAllowedCaller(context.ContainingSymbol, allowedCallers)
            || !IsReservedProtocolMember(member, receiverType, versionType,
                versionCounterType, reactiveObserverType, protocolMembers, reactiveGeneratedTrees))
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.VF10012_InternalProtocolMemberAccess,
            GetMemberLocation(operation),
            member.Name));
    }

    private static bool IsReservedProtocolMember(
        ISymbol member,
        ITypeSymbol? receiverType,
        INamedTypeSymbol? versionType,
        INamedTypeSymbol? versionCounterType,
        INamedTypeSymbol? reactiveObserverType,
        ImmutableArray<ISymbol> protocolMembers,
        ImmutableHashSet<SyntaxTree> reactiveGeneratedTrees)
        => IsVersionMember(member, receiverType, versionType, protocolMembers)
            || IsVersionCounterMember(member, versionCounterType)
            || IsReactiveGeneratedMember(member, reactiveObserverType, reactiveGeneratedTrees);

    private static bool IsVersionCounterMember(
        ISymbol member,
        INamedTypeSymbol? versionCounterType)
        => versionCounterType != null
            && member.ContainingType != null
            && SymbolEqualityComparer.Default.Equals(
                member.ContainingType.OriginalDefinition,
                versionCounterType.OriginalDefinition);

    private static bool IsVersionMember(
        ISymbol member,
        ITypeSymbol? receiverType,
        INamedTypeSymbol? versionType,
        ImmutableArray<ISymbol> protocolMembers)
        => versionType != null
            && member.ContainingType != null
                && GeneratorHelper.IsOrImplementsInterface(member.ContainingType, versionType)
            || MapsToProtocolMember(receiverType, member, protocolMembers);

    private static bool IsReactiveGeneratedMember(
        ISymbol member,
        INamedTypeSymbol? reactiveObserverType,
        ImmutableHashSet<SyntaxTree> reactiveGeneratedTrees)
        => reactiveObserverType != null
            && member.ContainingType != null
            && GeneratorHelper.IsOrImplementsInterface(member.ContainingType, reactiveObserverType)
            && member.DeclaringSyntaxReferences.Any(reference =>
                reactiveGeneratedTrees.Contains(reference.SyntaxTree));

    private static bool IsReactiveBindGeneratedTree(SyntaxTree tree)
    {
        var path = tree.FilePath ?? string.Empty;
        var separator = Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\'));
        var fileName = separator >= 0 ? path.Substring(separator + 1) : path;
        if (!fileName.StartsWith(ReactiveBindGenerator.GeneratedFilePrefix, StringComparison.Ordinal)
            || !fileName.EndsWith(ReactiveBindGenerator.GeneratedFileSuffix, StringComparison.Ordinal))
            return false;

        var text = tree.GetText();
        if (text.Lines.Count == 0) return false;
        var firstLine = text.ToString(text.Lines[0].Span);
        return firstLine.IndexOf("<auto-generated", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool MapsToProtocolMember(
        ITypeSymbol? receiverType,
        ISymbol referencedMember,
        ImmutableArray<ISymbol> protocolMembers)
    {
        foreach (var receiver in ReceiverCandidates(receiverType))
        foreach (var protocolMember in protocolMembers)
        {
            var implementation = receiver.FindImplementationForInterfaceMember(protocolMember);
            if (implementation != null && MembersMatch(implementation, referencedMember))
                return true;
        }

        return false;
    }

    private static IEnumerable<INamedTypeSymbol> ReceiverCandidates(ITypeSymbol? receiverType)
    {
        if (receiverType is INamedTypeSymbol named)
        {
            yield return named;
        }
        else if (receiverType is ITypeParameterSymbol parameter)
        {
            foreach (var constraint in parameter.ConstraintTypes.OfType<INamedTypeSymbol>())
                yield return constraint;
        }
    }

    private static bool MembersMatch(ISymbol left, ISymbol right)
    {
        if (SymbolEqualityComparer.Default.Equals(left.OriginalDefinition, right.OriginalDefinition))
            return true;

        if (left is IMethodSymbol leftMethod && right is IMethodSymbol rightMethod)
        {
            for (var current = leftMethod.OverriddenMethod; current != null; current = current.OverriddenMethod)
                if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, rightMethod.OriginalDefinition))
                    return true;
            for (var current = rightMethod.OverriddenMethod; current != null; current = current.OverriddenMethod)
                if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, leftMethod.OriginalDefinition))
                    return true;
        }
        else if (left is IPropertySymbol leftProperty && right is IPropertySymbol rightProperty)
        {
            for (var current = leftProperty.OverriddenProperty; current != null; current = current.OverriddenProperty)
                if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, rightProperty.OriginalDefinition))
                    return true;
            for (var current = rightProperty.OverriddenProperty; current != null; current = current.OverriddenProperty)
                if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, leftProperty.OriginalDefinition))
                    return true;
        }

        return false;
    }

    private static bool IsAllowedCaller(
        ISymbol containingSymbol,
        ImmutableArray<INamedTypeSymbol> allowedCallers)
    {
        for (var type = containingSymbol.ContainingType; type != null; type = type.ContainingType)
        foreach (var allowed in allowedCallers)
            if (SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, allowed.OriginalDefinition))
                return true;
        return false;
    }

    private static bool HasVersionFieldAttribute(
        IFieldSymbol field,
        INamedTypeSymbol? versionFieldAttribute)
        => versionFieldAttribute != null && field.GetAttributes().Any(attribute =>
            SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, versionFieldAttribute));

    private static bool IsInsideNameOf(IOperation operation)
    {
        for (var current = operation.Parent; current != null; current = current.Parent)
            if (current is INameOfOperation) return true;
        return false;
    }

    private static Location GetMemberLocation(IOperation operation)
    {
        SyntaxNode syntax = operation.Syntax;
        if (syntax is InvocationExpressionSyntax invocation) syntax = invocation.Expression;
        return syntax switch
        {
            MemberAccessExpressionSyntax access => access.Name.GetLocation(),
            MemberBindingExpressionSyntax binding => binding.Name.GetLocation(),
            SimpleNameSyntax name => name.GetLocation(),
            _ => syntax.GetLocation(),
        };
    }
}
