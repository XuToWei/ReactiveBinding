using System.Collections.Immutable;
using System.Reflection;
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

    private static MetadataReference[] CreateFrameworkReferences()
    {
        var tpa = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        return tpa.Where(p => !string.IsNullOrEmpty(p))
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .Append(MetadataReference.CreateFromFile(typeof(ReactiveSourceAttribute).Assembly.Location))
            .ToArray();
    }

    /// <summary>
    /// Runs the source generator on the provided source code and returns the result.
    /// </summary>
    public static GeneratorRunResult RunGenerator(string source, bool includeUsings = true)
    {
        var fullSource = includeUsings
            ? string.Join("\n", DefaultUsings) + "\n\n" + source
            : source;

        var syntaxTree = CSharpSyntaxTree.ParseText(fullSource);

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { syntaxTree },
            CreateFrameworkReferences(),
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
        var errors = result.Diagnostics.Concat(result.CompilationDiagnostics)
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToArray();
        if (errors.Length > 0)
        {
            var errorMessages = string.Join("\n", errors.Select(e => $"{e.Id}: {e.GetMessage()}"));
            throw new AssertionException($"Generation or generated-code compilation produced errors:\n{errorMessages}");
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

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { syntaxTree },
            CreateFrameworkReferences(),
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
    /// Runs the VersionFieldGenerator on the source, compiles source + generated code into an
    /// in-memory assembly, and returns it for execution-based round-trip testing.
    /// Throws if generation or compilation produces errors.
    /// </summary>
    public static CompiledResult CompileAndRun(string source, bool includeUsings = true)
        => CompileAndRunCore(source, includeUsings, new VersionFieldGenerator());

    /// <summary>Compiles and executes source using only the ReactiveBind generator.</summary>
    public static CompiledResult CompileAndRunReactive(string source, bool includeUsings = true)
        => CompileAndRunCore(source, includeUsings, new ReactiveBindGenerator());

    /// <summary>Compiles and executes source using both production generators.</summary>
    public static CompiledResult CompileAndRunAll(string source, bool includeUsings = true)
        => CompileAndRunCore(source, includeUsings, new ReactiveBindGenerator(), new VersionFieldGenerator());

    private static CompiledResult CompileAndRunCore(string source, bool includeUsings, params ISourceGenerator[] generators)
    {
        var fullSource = includeUsings
            ? string.Join("\n", DefaultUsings) + "\n\n" + source
            : source;

        var syntaxTree = CSharpSyntaxTree.ParseText(fullSource);

        // Reference the full framework (TPA) plus this test assembly (where ReactiveBinding runtime types live).
        var tpa = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        var references = tpa.Where(p => !string.IsNullOrEmpty(p))
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .Append(MetadataReference.CreateFromFile(typeof(ReactiveBinding.SyncContext).Assembly.Location))
            .ToArray();

        var compilation = CSharpCompilation.Create(
            "SyncTestAssembly_" + System.Guid.NewGuid().ToString("N"),
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(generators);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out _);

        var genDiagnostics = driver.GetRunResult().Results.SelectMany(r => r.Diagnostics)
            .Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        if (genDiagnostics.Length > 0)
            throw new AssertionException("Generator errors:\n" + string.Join("\n", genDiagnostics.Select(d => $"{d.Id}: {d.GetMessage()}")));

        using var ms = new MemoryStream();
        var emit = outputCompilation.Emit(ms);
        if (!emit.Success)
        {
            var errors = emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error);
            throw new AssertionException("Compilation errors:\n" + string.Join("\n", errors.Select(d => $"{d.Id}: {d.GetMessage()}")));
        }

        var generatedSources = driver.GetRunResult().GeneratedTrees.Select(t => t.GetText().ToString()).ToArray();
        return new CompiledResult(Assembly.Load(ms.ToArray()), generatedSources);
    }

    /// <summary>
    /// Runs the ReservedMethodAnalyzer on the provided source code and returns diagnostics.
    /// </summary>
    public static async Task<Diagnostic[]> RunReservedMethodAnalyzer(string source, bool includeUsings = true)
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

        var analyzer = new ReservedMethodAnalyzer();
        var compilationWithAnalyzers = compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(analyzer));

        var diagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
        return diagnostics.ToArray();
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

    /// <summary>
    /// Runs the VersionInheritanceAnalyzer on the provided source code and returns diagnostics.
    /// </summary>
    public static async Task<Diagnostic[]> RunVersionInheritanceAnalyzer(string source, bool includeUsings = true)
    {
        var fullSource = includeUsings
            ? string.Join("\n", DefaultUsings) + "\n\n" + source
            : source;

        var syntaxTree = CSharpSyntaxTree.ParseText(fullSource);
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { syntaxTree },
            CreateFrameworkReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var analyzer = new VersionInheritanceAnalyzer();
        var compilationWithAnalyzers = compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(analyzer));
        return (await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync()).ToArray();
    }

    /// <summary>
    /// Runs the ObserveChangesCallAnalyzer on the provided source code and returns diagnostics.
    /// </summary>
    public static async Task<Diagnostic[]> RunObserveChangesCallAnalyzer(string source, bool includeUsings = true)
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

        var analyzer = new ObserveChangesCallAnalyzer();
        var compilationWithAnalyzers = compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(analyzer));

        var diagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
        return diagnostics.ToArray();
    }

    /// <summary>
    /// Runs the VersionFieldAccessAnalyzer on the provided source code and returns diagnostics.
    /// </summary>
    public static async Task<Diagnostic[]> RunVersionFieldAccessAnalyzer(string source, bool includeUsings = true)
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

        var analyzer = new VersionFieldAccessAnalyzer();
        var compilationWithAnalyzers = compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(analyzer));

        var diagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
        return diagnostics.ToArray();
    }


    /// <summary>
    /// Runs the VersionFieldInitializerAnalyzer on the provided source code and returns diagnostics.
    /// </summary>
    public static async Task<Diagnostic[]> RunVersionFieldInitializerAnalyzer(string source, bool includeUsings = true)
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

        var analyzer = new VersionFieldInitializerAnalyzer();
        var compilationWithAnalyzers = compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(analyzer));

        var diagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
        return diagnostics.ToArray();
    }
}

/// <summary>
/// Result of compiling generated code into a runnable in-memory assembly.
/// </summary>
public class CompiledResult
{
    public Assembly Assembly { get; }
    public string[] GeneratedSources { get; }

    public CompiledResult(Assembly assembly, string[] generatedSources)
    {
        Assembly = assembly;
        GeneratedSources = generatedSources;
    }

    /// <summary>Creates an instance of the named type (as dynamic for ergonomic member access).</summary>
    public dynamic Create(string fullTypeName)
    {
        var type = Assembly.GetType(fullTypeName)
            ?? throw new AssertionException($"Type not found in compiled assembly: {fullTypeName}");
        return Activator.CreateInstance(type)!;
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
