using NUnit.Framework;

namespace ReactiveBinding.SourceGenerator.Tests;

/// <summary>
/// Tests for first call auto-trigger behavior.
/// </summary>
[TestFixture]
public class FirstCallTests
{
    [Test]
    public void FirstCall_GeneratesInitializationGuardAndReturns()
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
        GeneratorTestHelper.AssertGeneratedContains(result, "return;");
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
    public void FirstCall_OldAndNewValue_CallsWithCurrentForBoth()
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
        // First call should pass cached value for both old and new (same value on init)
        GeneratorTestHelper.AssertGeneratedContains(result, "OnHealthChanged(__reactive_Health, __reactive_Health)");
    }

}
