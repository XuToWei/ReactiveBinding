using NUnit.Framework;

namespace ReactiveBinding.SourceGenerator.Tests;

[TestFixture]
public class VersionFieldDeterministicOrderingTests
{
    private const string FirstPart = @"
namespace Test
{
    public partial class State : IVersionSync
    {
        [VersionField] private int __First;
    }
}";

    private const string SecondPart = @"
namespace Test
{
    public partial class State
    {
        [VersionField] private int __Second;
    }
}";

    [Test]
    public void PartialClass_ReversedSyntaxTreeInput_GeneratesIdenticalCode()
    {
        var forward = GeneratorTestHelper.RunVersionFieldGenerator(
            ("State.A.cs", FirstPart),
            ("State.B.cs", SecondPart));
        var reversed = GeneratorTestHelper.RunVersionFieldGenerator(
            ("State.B.cs", SecondPart),
            ("State.A.cs", FirstPart));

        GeneratorTestHelper.AssertNoErrors(forward);
        GeneratorTestHelper.AssertNoErrors(reversed);
        Assert.That(
            GeneratorTestHelper.GetGeneratedForClass(reversed, "State"),
            Is.EqualTo(GeneratorTestHelper.GetGeneratedForClass(forward, "State")));
    }

    [Test]
    public void PartialClass_EmptyPathsAndReversedInput_GeneratesIdenticalCode()
    {
        var forward = GeneratorTestHelper.RunVersionFieldGenerator(
            (string.Empty, FirstPart),
            (string.Empty, SecondPart));
        var reversed = GeneratorTestHelper.RunVersionFieldGenerator(
            (string.Empty, SecondPart),
            (string.Empty, FirstPart));

        GeneratorTestHelper.AssertNoErrors(forward);
        GeneratorTestHelper.AssertNoErrors(reversed);
        Assert.That(
            GeneratorTestHelper.GetGeneratedForClass(reversed, "State"),
            Is.EqualTo(GeneratorTestHelper.GetGeneratedForClass(forward, "State")));
    }
}
