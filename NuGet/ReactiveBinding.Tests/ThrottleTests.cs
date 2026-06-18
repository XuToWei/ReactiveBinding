using NUnit.Framework;

namespace ReactiveBinding.SourceGenerator.Tests;

/// <summary>
/// Tests for ReactiveThrottle frequency control.
/// </summary>
[TestFixture]
public class ThrottleTests
{
    [Test]
    public void Throttle_Value1_NoThrottleLogic()
    {
        var source = @"
namespace ReactiveBinding.Test
{
    [ReactiveThrottle(1)]
    public partial class TestClass : IReactiveObserver
    {
        [ReactiveSource]
        private int Health;

        [ReactiveBind(nameof(Health))]
        private void OnHealthChanged() { }
    }
}";

        var result = GeneratorTestHelper.RunGenerator(source);

        GeneratorTestHelper.AssertNoErrors(result);
        // Throttle of 1 should not generate call count logic
        var generated = GeneratorTestHelper.GetGeneratedForClass(result, "TestClass");
        Assert.That(generated, Does.Not.Contain("__reactive_callCount"));
    }

    [Test]
    public void Throttle_ValueGreaterThan1_GeneratesThrottleLogic()
    {
        var source = @"
namespace ReactiveBinding.Test
{
    [ReactiveThrottle(10)]
    public partial class TestClass : IReactiveObserver
    {
        [ReactiveSource]
        private int Health;

        [ReactiveBind(nameof(Health))]
        private void OnHealthChanged() { }
    }
}";

        var result = GeneratorTestHelper.RunGenerator(source);

        GeneratorTestHelper.AssertNoErrors(result);
        GeneratorTestHelper.AssertGeneratedContains(result, "private int __reactive_callCount");
        GeneratorTestHelper.AssertGeneratedContains(result, "++__reactive_callCount < 10");
        GeneratorTestHelper.AssertGeneratedContains(result, "__reactive_callCount = 0");
    }

    [Test]
    public void Throttle_SkipsCallsCorrectly()
    {
        var source = @"
namespace ReactiveBinding.Test
{
    [ReactiveThrottle(5)]
    public partial class TestClass : IReactiveObserver
    {
        [ReactiveSource]
        private int Health;

        [ReactiveBind(nameof(Health))]
        private void OnHealthChanged() { }
    }
}";

        var result = GeneratorTestHelper.RunGenerator(source);

        GeneratorTestHelper.AssertNoErrors(result);
        // Should check initialized before incrementing
        GeneratorTestHelper.AssertGeneratedContains(result, "if (__reactive_initialized && ++__reactive_callCount < 5)");
        GeneratorTestHelper.AssertGeneratedContains(result, "return;");
    }

    [Test]
    public void Throttle_FirstCallAlwaysExecutes()
    {
        var source = @"
namespace ReactiveBinding.Test
{
    [ReactiveThrottle(100)]
    public partial class TestClass : IReactiveObserver
    {
        [ReactiveSource]
        private int Health;

        [ReactiveBind(nameof(Health))]
        private void OnHealthChanged() { }
    }
}";

        var result = GeneratorTestHelper.RunGenerator(source);

        GeneratorTestHelper.AssertNoErrors(result);
        // Throttle should only apply when initialized
        GeneratorTestHelper.AssertGeneratedContains(result, "__reactive_initialized && ++__reactive_callCount");
        // First call should still trigger
        GeneratorTestHelper.AssertGeneratedContains(result, "if (!__reactive_initialized)");
    }

    [Test]
    public void Throttle_NoAttribute_NoThrottleLogic()
    {
        var source = @"
namespace ReactiveBinding.Test
{
    public partial class TestClass : IReactiveObserver
    {
        [ReactiveSource]
        private int Health;

        [ReactiveBind(nameof(Health))]
        private void OnHealthChanged() { }
    }
}";

        var result = GeneratorTestHelper.RunGenerator(source);

        GeneratorTestHelper.AssertNoErrors(result);
        var generated = GeneratorTestHelper.GetGeneratedForClass(result, "TestClass");
        Assert.That(generated, Does.Not.Contain("__reactive_callCount"));
    }

    [Test]
    public void Throttle_InvalidValue_ProducesError()
    {
        var source = @"
namespace ReactiveBinding.Test
{
    [ReactiveThrottle(0)]
    public partial class TestClass : IReactiveObserver
    {
        [ReactiveSource]
        private int Health;

        [ReactiveBind(nameof(Health))]
        private void OnHealthChanged() { }
    }
}";

        var result = GeneratorTestHelper.RunGenerator(source);

        GeneratorTestHelper.AssertHasDiagnostic(result, "RB1003");
    }

    [Test]
    public void Throttle_NegativeValue_ProducesError()
    {
        var source = @"
namespace ReactiveBinding.Test
{
    [ReactiveThrottle(-5)]
    public partial class TestClass : IReactiveObserver
    {
        [ReactiveSource]
        private int Health;

        [ReactiveBind(nameof(Health))]
        private void OnHealthChanged() { }
    }
}";

        var result = GeneratorTestHelper.RunGenerator(source);

        GeneratorTestHelper.AssertHasDiagnostic(result, "RB1003");
    }
}
