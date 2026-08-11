using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace ReactiveBinding.Generator;

internal sealed class GeneratorReportingContext
{
    public Compilation Compilation { get; }
    public List<Diagnostic> Diagnostics { get; } = new();
    private string? HintName { get; set; }
    private string? Source { get; set; }

    public GeneratorReportingContext(Compilation compilation)
    {
        Compilation = compilation;
    }

    public void ReportDiagnostic(Diagnostic diagnostic) => Diagnostics.Add(diagnostic);

    public void AddSource(string hintName, string source)
    {
        HintName = hintName;
        Source = source;
    }

    public GeneratedClassOutput ToOutput() => new(HintName, Source, Diagnostics);
}

internal sealed class GeneratedClassOutput
{
    public string? HintName { get; }
    public string? Source { get; }
    public IReadOnlyList<Diagnostic> Diagnostics { get; }

    public GeneratedClassOutput(string? hintName, string? source, IReadOnlyList<Diagnostic> diagnostics)
    {
        HintName = hintName;
        Source = source;
        Diagnostics = diagnostics;
    }

    public static void Emit(SourceProductionContext context, GeneratedClassOutput output)
    {
        foreach (var diagnostic in output.Diagnostics)
            context.ReportDiagnostic(diagnostic);

        if (output.HintName != null && output.Source != null)
            context.AddSource(output.HintName, SourceText.From(output.Source, System.Text.Encoding.UTF8));
    }
}

internal sealed class GeneratedClassOutputComparer : IEqualityComparer<GeneratedClassOutput>
{
    public static readonly GeneratedClassOutputComparer Instance = new();

    public bool Equals(GeneratedClassOutput? x, GeneratedClassOutput? y)
    {
        if (ReferenceEquals(x, y)) return true;
        if (x == null || y == null
            || x.HintName != y.HintName
            || x.Source != y.Source
            || x.Diagnostics.Count != y.Diagnostics.Count)
            return false;

        for (int i = 0; i < x.Diagnostics.Count; i++)
        {
            var left = x.Diagnostics[i];
            var right = y.Diagnostics[i];
            if (left.Id != right.Id
                || left.Severity != right.Severity
                || left.GetMessage() != right.GetMessage()
                || left.Location.SourceSpan != right.Location.SourceSpan
                || left.Location.SourceTree?.FilePath != right.Location.SourceTree?.FilePath)
                return false;
        }

        return true;
    }

    public int GetHashCode(GeneratedClassOutput obj)
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 31 + (obj.HintName?.GetHashCode() ?? 0);
            hash = hash * 31 + (obj.Source?.GetHashCode() ?? 0);
            foreach (var diagnostic in obj.Diagnostics)
            {
                hash = hash * 31 + diagnostic.Id.GetHashCode();
                hash = hash * 31 + diagnostic.GetMessage().GetHashCode();
                hash = hash * 31 + diagnostic.Location.SourceSpan.GetHashCode();
            }
            return hash;
        }
    }
}

internal static class IncrementalGeneratorHelper
{
    public static bool IsReactiveCandidate(ClassDeclarationSyntax declaration)
        => declaration.BaseList != null
            || declaration.AttributeLists.Count > 0
            || declaration.Members.Any(static member => member switch
            {
                FieldDeclarationSyntax field => field.AttributeLists.Count > 0,
                PropertyDeclarationSyntax property => property.AttributeLists.Count > 0,
                MethodDeclarationSyntax method => method.AttributeLists.Count > 0,
                _ => false
            });

    public static bool IsVersionFieldCandidate(ClassDeclarationSyntax declaration)
        => declaration.Members.OfType<FieldDeclarationSyntax>()
            .Any(static field => field.AttributeLists.Count > 0);

    public static bool IsOwner(
        ClassDeclarationSyntax declaration,
        INamedTypeSymbol symbol,
        Func<ClassDeclarationSyntax, bool> predicate,
        CancellationToken cancellationToken)
    {
        var owner = symbol.DeclaringSyntaxReferences
            .Select(reference => reference.GetSyntax(cancellationToken))
            .OfType<ClassDeclarationSyntax>()
            .Where(predicate)
            .OrderBy(static candidate => GetSyntaxTreeKey(candidate.SyntaxTree), StringComparer.Ordinal)
            .ThenBy(static candidate => candidate.SpanStart)
            .ThenBy(static candidate => candidate.RawKind)
            .FirstOrDefault();

        return owner != null
            && ReferenceEquals(owner.SyntaxTree, declaration.SyntaxTree)
            && owner.Span == declaration.Span;
    }

    public static IEnumerable<ClassDeclarationSyntax> GetOrderedDeclarations(
        INamedTypeSymbol symbol,
        CancellationToken cancellationToken)
        => symbol.DeclaringSyntaxReferences
            .Select(reference => reference.GetSyntax(cancellationToken))
            .OfType<ClassDeclarationSyntax>()
            .OrderBy(static declaration => GetSyntaxTreeKey(declaration.SyntaxTree), StringComparer.Ordinal)
            .ThenBy(static declaration => declaration.SpanStart)
            .ThenBy(static declaration => declaration.RawKind);

    public static string GetSyntaxTreeKey(SyntaxTree syntaxTree)
    {
        if (!string.IsNullOrEmpty(syntaxTree.FilePath))
            return $"P:{syntaxTree.FilePath.Replace('\\', '/')}";

        var key = new System.Text.StringBuilder("C:");
        foreach (byte value in syntaxTree.GetText().GetChecksum())
            key.Append(value.ToString("x2"));
        return key.ToString();
    }
}
