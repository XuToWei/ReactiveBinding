using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using ReactiveBinding.Generator;

namespace ReactiveBinding.SourceGenerator.Tests;

/// <summary>
/// Helper class for testing the ReactiveBindGenerator.
/// </summary>
public static class GeneratorTestHelper
{
    private static readonly string[] DefaultUsings =
    {
        "using System;",
        "using ReactiveBinding;"
    };

    /// <summary>
    /// Runs the source generator on the provided source code and returns the result.
    /// </summary>
    public static GeneratorRunResult RunGenerator(string source, bool includeUsings = true)
    {
        var fullSource = includeUsings
            ? string.Join("\n", DefaultUsings) + "\n\n" + source
            : source;

        var syntaxTree = CSharpSyntaxTree.ParseText(fullSource);

        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Attribute).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(ReactiveSourceAttribute).Assembly.Location),
        };

        // Add runtime reference
        var runtimePath = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var runtimeRef = MetadataReference.CreateFromFile(Path.Combine(runtimePath, "System.Runtime.dll"));

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { syntaxTree },
            references.Append(runtimeRef),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new ReactiveBindGenerator();

        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        var runResult = driver.GetRunResult();

        return new GeneratorRunResult
        {
            GeneratedSources = runResult.GeneratedTrees.Select(t => t.GetText().ToString()).ToArray(),
            Diagnostics = runResult.Results.SelectMany(r => r.Diagnostics).ToArray(),
            CompilationDiagnostics = outputCompilation.GetDiagnostics().ToArray()
        };
    }

    /// <summary>
    /// Asserts that the generator produces no errors.
    /// </summary>
    public static void AssertNoErrors(GeneratorRunResult result)
    {
        var errors = result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        if (errors.Length > 0)
        {
            var errorMessages = string.Join("\n", errors.Select(e => $"{e.Id}: {e.GetMessage()}"));
            throw new AssertionException($"Generator produced errors:\n{errorMessages}");
        }
    }

    /// <summary>
    /// Asserts that the generator produces a specific diagnostic.
    /// </summary>
    public static void AssertHasDiagnostic(GeneratorRunResult result, string diagnosticId)
    {
        var hasDiagnostic = result.Diagnostics.Any(d => d.Id == diagnosticId);
        if (!hasDiagnostic)
        {
            var allDiagnostics = string.Join(", ", result.Diagnostics.Select(d => d.Id));
            throw new AssertionException($"Expected diagnostic {diagnosticId} not found. Found: {allDiagnostics}");
        }
    }

    /// <summary>
    /// Asserts that the generated code contains the specified text.
    /// </summary>
    public static void AssertGeneratedContains(GeneratorRunResult result, string expectedText)
    {
        var found = result.GeneratedSources.Any(s => s.Contains(expectedText));
        if (!found)
        {
            var allGenerated = string.Join("\n---\n", result.GeneratedSources);
            throw new AssertionException($"Expected text not found in generated code: {expectedText}\n\nGenerated:\n{allGenerated}");
        }
    }

    /// <summary>
    /// Gets the first generated source that contains a specific class name.
    /// </summary>
    public static string? GetGeneratedForClass(GeneratorRunResult result, string className)
    {
        return result.GeneratedSources.FirstOrDefault(s => s.Contains($"partial class {className}"));
    }

    /// <summary>
    /// Runs the VersionFieldGenerator on the provided source code and returns the result.
    /// </summary>
    public static GeneratorRunResult RunVersionFieldGenerator(string source, bool includeUsings = true)
    {
        var fullSource = includeUsings
            ? string.Join("\n", DefaultUsings) + "\n\n" + source
            : source;

        var syntaxTree = CSharpSyntaxTree.ParseText(fullSource);

        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Attribute).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(ReactiveSourceAttribute).Assembly.Location),
        };

        // Add runtime reference
        var runtimePath = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var runtimeRef = MetadataReference.CreateFromFile(Path.Combine(runtimePath, "System.Runtime.dll"));

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { syntaxTree },
            references.Append(runtimeRef),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new VersionFieldGenerator();

        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        var runResult = driver.GetRunResult();

        return new GeneratorRunResult
        {
            GeneratedSources = runResult.GeneratedTrees.Select(t => t.GetText().ToString()).ToArray(),
            Diagnostics = runResult.Results.SelectMany(r => r.Diagnostics).ToArray(),
            CompilationDiagnostics = outputCompilation.GetDiagnostics().ToArray()
        };
    }

    /// <summary>
    /// Runs the ParentAccessAnalyzer on the provided source code and returns diagnostics.
    /// </summary>
    public static async Task<Diagnostic[]> RunParentAccessAnalyzer(string source, bool includeUsings = true)
    {
        var fullSource = includeUsings
            ? string.Join("\n", DefaultUsings) + "\n\n" + source
            : source;

        var syntaxTree = CSharpSyntaxTree.ParseText(fullSource);

        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Attribute).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(ReactiveSourceAttribute).Assembly.Location),
        };

        var runtimePath = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var runtimeRef = MetadataReference.CreateFromFile(Path.Combine(runtimePath, "System.Runtime.dll"));

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { syntaxTree },
            references.Append(runtimeRef),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var analyzer = new ParentAccessAnalyzer();
        var compilationWithAnalyzers = compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(analyzer));

        var diagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
        return diagnostics.ToArray();
    }
}

/// <summary>
/// Result of running the source generator.
/// </summary>
public class GeneratorRunResult
{
    public string[] GeneratedSources { get; init; } = Array.Empty<string>();
    public Diagnostic[] Diagnostics { get; init; } = Array.Empty<Diagnostic>();
    public Diagnostic[] CompilationDiagnostics { get; init; } = Array.Empty<Diagnostic>();
}

/// <summary>
/// Exception thrown when an assertion fails.
/// </summary>
public class AssertionException : Exception
{
    public AssertionException(string message) : base(message) { }
}
