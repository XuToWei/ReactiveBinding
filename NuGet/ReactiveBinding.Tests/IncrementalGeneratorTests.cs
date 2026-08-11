using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using ReactiveBinding.Generator;

namespace ReactiveBinding.SourceGenerator.Tests;

[TestFixture]
public class IncrementalGeneratorTests
{
    [TestCase(true, "Reactive.ClassOutputs")]
    [TestCase(false, "VersionField.ClassOutputs")]
    public void UnchangedCompilation_CachesClassOutput(bool reactive, string trackingName)
    {
        const string reactiveSource = @"
using ReactiveBinding;
public partial class Observer : IReactiveObserver
{
    [ReactiveSource] private int Value;
    [ReactiveBind(nameof(Value))] private void Changed() { }
}";
        const string versionSource = @"
using ReactiveBinding;
public partial class State : IVersion
{
    [VersionField] private int __Value;
}";

        var compilation = CreateCompilation(reactive ? reactiveSource : versionSource);
        ISourceGenerator generator = reactive
            ? new ReactiveBindGenerator().AsSourceGenerator()
            : new VersionFieldGenerator().AsSourceGenerator();
        var options = new GeneratorDriverOptions(
            disabledOutputs: IncrementalGeneratorOutputKind.None,
            trackIncrementalGeneratorSteps: true);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            ImmutableArray.Create(generator),
            driverOptions: options);

        driver = driver.RunGenerators(compilation);
        driver = driver.RunGenerators(compilation);

        var trackedSteps = driver.GetRunResult().Results.Single().TrackedSteps;
        Assert.That(trackedSteps.ContainsKey(trackingName), Is.True);
        Assert.That(
            trackedSteps[trackingName].SelectMany(step => step.Outputs)
                .All(output => output.Reason is IncrementalStepRunReason.Cached
                    or IncrementalStepRunReason.Unchanged),
            Is.True);
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(path => !string.IsNullOrEmpty(path))
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .Append(MetadataReference.CreateFromFile(typeof(ReactiveBinding.ReactiveSourceAttribute).Assembly.Location));

        return CSharpCompilation.Create(
            "IncrementalTest",
            new[] { CSharpSyntaxTree.ParseText(source, path: "Input.cs") },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
