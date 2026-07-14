using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace ReactiveBinding.Generator;

/// <summary>Protects the double-underscore protocol surface exposed by IVersion implementations.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class VersionProtocolAccessAnalyzer : DiagnosticAnalyzer
{
    private const string IVersionName = "ReactiveBinding.IVersion";
    private const string IVersionSyncName = "ReactiveBinding.IVersionSync";
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
        ImmutableArray.Create(DiagnosticDescriptors.VF10012_InternalVersionMemberAccess);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(startContext =>
        {
            var versionType = startContext.Compilation.GetTypeByMetadataName(IVersionName);
            if (versionType == null) return;

            var syncType = startContext.Compilation.GetTypeByMetadataName(IVersionSyncName);
            var versionFieldAttribute = startContext.Compilation.GetTypeByMetadataName(VersionFieldAttributeName);
            var allowedCallers = AllowedCallerNames
                .Select(startContext.Compilation.GetTypeByMetadataName)
                .Where(type => type != null)
                .Cast<INamedTypeSymbol>()
                .ToImmutableArray();
            var protocolMembers = versionType.GetMembers()
                .Concat(syncType?.GetMembers() ?? ImmutableArray<ISymbol>.Empty)
                .Where(member => member.Name.StartsWith("__", StringComparison.Ordinal))
                .ToImmutableArray();

            startContext.RegisterOperationAction(c =>
            {
                var operation = (IFieldReferenceOperation)c.Operation;
                if (!HasVersionFieldAttribute(operation.Field, versionFieldAttribute))
                    Analyze(c, operation, operation.Field, operation.Instance?.Type,
                        versionType, protocolMembers, allowedCallers);
            }, OperationKind.FieldReference);
            startContext.RegisterOperationAction(c =>
            {
                var operation = (IPropertyReferenceOperation)c.Operation;
                Analyze(c, operation, operation.Property, operation.Instance?.Type,
                    versionType, protocolMembers, allowedCallers);
            }, OperationKind.PropertyReference);
            startContext.RegisterOperationAction(c =>
            {
                var operation = (IInvocationOperation)c.Operation;
                Analyze(c, operation, operation.TargetMethod, operation.Instance?.Type,
                    versionType, protocolMembers, allowedCallers);
            }, OperationKind.Invocation);
            startContext.RegisterOperationAction(c =>
            {
                var operation = (IMethodReferenceOperation)c.Operation;
                Analyze(c, operation, operation.Method, operation.Instance?.Type,
                    versionType, protocolMembers, allowedCallers);
            }, OperationKind.MethodReference);
        });
    }

    private static void Analyze(
        OperationAnalysisContext context,
        IOperation operation,
        ISymbol member,
        ITypeSymbol? receiverType,
        INamedTypeSymbol versionType,
        ImmutableArray<ISymbol> protocolMembers,
        ImmutableArray<INamedTypeSymbol> allowedCallers)
    {
        if (!member.Name.StartsWith("__", StringComparison.Ordinal)
            || IsInsideNameOf(operation)
            || IsAllowedCaller(context.ContainingSymbol, allowedCallers)
            || !IsVersionMember(member, receiverType, versionType, protocolMembers))
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.VF10012_InternalVersionMemberAccess,
            GetMemberLocation(operation),
            member.Name));
    }

    private static bool IsVersionMember(
        ISymbol member,
        ITypeSymbol? receiverType,
        INamedTypeSymbol versionType,
        ImmutableArray<ISymbol> protocolMembers)
        => member.ContainingType != null
            && GeneratorHelper.IsOrImplementsInterface(member.ContainingType, versionType)
            || MapsToProtocolMember(receiverType, member, protocolMembers);

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
