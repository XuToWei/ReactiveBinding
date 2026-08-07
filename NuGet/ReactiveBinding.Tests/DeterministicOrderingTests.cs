using NUnit.Framework;

namespace ReactiveBinding.SourceGenerator.Tests;

[TestFixture]
public class DeterministicOrderingTests
{
    private const string Sources = @"
namespace Test
{
    public partial class Observer : IReactiveObserver
    {
        [ReactiveSource] private int First;
        [ReactiveBind(nameof(First))] private void OnFirst() { }
    }
}";

    private const string AdditionalBindings = @"
namespace Test
{
    public partial class Observer
    {
        [ReactiveSource] private int Second;
        [ReactiveBind(nameof(Second))] private void OnSecond() { }
    }
}";

    [Test]
    public void PartialClass_ReversedSyntaxTreeInput_GeneratesIdenticalCode()
    {
        var forward = GeneratorTestHelper.RunGenerator(
            ("Observer.A.cs", Sources),
            ("Observer.B.cs", AdditionalBindings));
        var reversed = GeneratorTestHelper.RunGenerator(
            ("Observer.B.cs", AdditionalBindings),
            ("Observer.A.cs", Sources));

        GeneratorTestHelper.AssertNoErrors(forward);
        GeneratorTestHelper.AssertNoErrors(reversed);
        Assert.That(
            GeneratorTestHelper.GetGeneratedForClass(reversed, "Observer"),
            Is.EqualTo(GeneratorTestHelper.GetGeneratedForClass(forward, "Observer")));
    }

    [Test]
    public void PartialClass_EmptyPathsAndReversedInput_GenerateIdenticalCode()
    {
        var forward = GeneratorTestHelper.RunGenerator(
            (string.Empty, Sources),
            (string.Empty, AdditionalBindings));
        var reversed = GeneratorTestHelper.RunGenerator(
            (string.Empty, AdditionalBindings),
            (string.Empty, Sources));

        GeneratorTestHelper.AssertNoErrors(forward);
        GeneratorTestHelper.AssertNoErrors(reversed);
        Assert.That(
            GeneratorTestHelper.GetGeneratedForClass(reversed, "Observer"),
            Is.EqualTo(GeneratorTestHelper.GetGeneratedForClass(forward, "Observer")));
    }

    [Test]
    public void PartialClass_PathSeparators_GenerateIdenticalCode()
    {
        var windowsPaths = GeneratorTestHelper.RunGenerator(
            ("Views\\Observer.A.cs", Sources),
            ("Views\\Observer.B.cs", AdditionalBindings));
        var unixPaths = GeneratorTestHelper.RunGenerator(
            ("Views/Observer.A.cs", Sources),
            ("Views/Observer.B.cs", AdditionalBindings));

        GeneratorTestHelper.AssertNoErrors(windowsPaths);
        GeneratorTestHelper.AssertNoErrors(unixPaths);
        Assert.That(
            GeneratorTestHelper.GetGeneratedForClass(windowsPaths, "Observer"),
            Is.EqualTo(GeneratorTestHelper.GetGeneratedForClass(unixPaths, "Observer")));
    }
}
