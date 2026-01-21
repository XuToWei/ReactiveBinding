using NUnit.Framework;

namespace ReactiveBinding.SourceGenerator.Tests;

/// <summary>
/// Tests for first call auto-trigger behavior.
/// </summary>
[TestFixture]
public class FirstCallTests
{
    [Test]
    public void FirstCall_GeneratesInitializedFlag()
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
        GeneratorTestHelper.AssertGeneratedContains(result, "private bool __reactive_initialized");
        GeneratorTestHelper.AssertGeneratedContains(result, "if (!__reactive_initialized)");
        GeneratorTestHelper.AssertGeneratedContains(result, "__reactive_initialized = true");
    }

    [Test]
    public void FirstCall_InitializesCacheVariables()
    {
        var source = @"
namespace ReactiveBinding.Test
{
    public partial class TestClass : IReactiveObserver
    {
        [ReactiveSource]
        private int Health;

        [ReactiveSource]
        private int Mana;

        [ReactiveBind(nameof(Health))]
        private void OnHealthChanged() { }

        [ReactiveBind(nameof(Mana))]
        private void OnManaChanged() { }
    }
}";

        var result = GeneratorTestHelper.RunGenerator(source);

        GeneratorTestHelper.AssertNoErrors(result);
        // Both cache variables should be initialized in the first-call block
        GeneratorTestHelper.AssertGeneratedContains(result, "__reactive_Health = Health");
        GeneratorTestHelper.AssertGeneratedContains(result, "__reactive_Mana = Mana");
    }

    [Test]
    public void FirstCall_NoParameters_CallsWithNoArgs()
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
        Assert.That(generated, Does.Contain("OnHealthChanged();"));
    }

    [Test]
    public void FirstCall_NewValueOnly_CallsWithCurrentValue()
    {
        var source = @"
namespace ReactiveBinding.Test
{
    public partial class TestClass : IReactiveObserver
    {
        [ReactiveSource]
        private int Health;

        [ReactiveBind(nameof(Health))]
        private void OnHealthChanged(int newValue) { }
    }
}";

        var result = GeneratorTestHelper.RunGenerator(source);

        GeneratorTestHelper.AssertNoErrors(result);
        // First call should pass the current value
        GeneratorTestHelper.AssertGeneratedContains(result, "OnHealthChanged(Health)");
    }

    [Test]
    public void FirstCall_OldAndNewValue_CallsWithDefaultAndCurrent()
    {
        var source = @"
namespace ReactiveBinding.Test
{
    public partial class TestClass : IReactiveObserver
    {
        [ReactiveSource]
        private int Health;

        [ReactiveBind(nameof(Health))]
        private void OnHealthChanged(int oldValue, int newValue) { }
    }
}";

        var result = GeneratorTestHelper.RunGenerator(source);

        GeneratorTestHelper.AssertNoErrors(result);
        // First call should pass default for old value and current for new value
        GeneratorTestHelper.AssertGeneratedContains(result, "default(int)!");
    }

    [Test]
    public void FirstCall_MultiSource_PassesAllValues()
    {
        var source = @"
namespace ReactiveBinding.Test
{
    public partial class TestClass : IReactiveObserver
    {
        [ReactiveSource]
        private int Health;

        [ReactiveSource]
        private int Mana;

        [ReactiveBind(nameof(Health), nameof(Mana))]
        private void OnStatsChanged(int newHealth, int newMana) { }
    }
}";

        var result = GeneratorTestHelper.RunGenerator(source);

        GeneratorTestHelper.AssertNoErrors(result);
        // First call should pass current values for both
        GeneratorTestHelper.AssertGeneratedContains(result, "OnStatsChanged(Health, Mana)");
    }

    [Test]
    public void FirstCall_ReturnsAfterTrigger()
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
        // Should return after first call block
        var generated = GeneratorTestHelper.GetGeneratedForClass(result, "TestClass");
        Assert.That(generated, Does.Contain("return;"));
    }
}
