using NUnit.Framework;

namespace ReactiveBinding.SourceGenerator.Tests;

/// <summary>
/// Tests for multi-source binding to a single callback.
/// </summary>
[TestFixture]
public class MultiSourceBindingTests
{
    [Test]
    public void MultiSource_NoParameters_GeneratesOrCondition()
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
        private void OnStatsChanged() { }
    }
}";

        var result = GeneratorTestHelper.RunGenerator(source);

        GeneratorTestHelper.AssertNoErrors(result);
        GeneratorTestHelper.AssertGeneratedContains(result, "__changed_Health");
        GeneratorTestHelper.AssertGeneratedContains(result, "__changed_Mana");
        GeneratorTestHelper.AssertGeneratedContains(result, "__changed_Health || __changed_Mana");
        GeneratorTestHelper.AssertGeneratedContains(result, "OnStatsChanged()");
    }

    [Test]
    public void MultiSource_NewValuesOnly_GeneratesCorrectParameters()
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
        GeneratorTestHelper.AssertGeneratedContains(result, "OnStatsChanged(__reactive_Health, __reactive_Mana)");
    }

    [Test]
    public void MultiSource_OldAndNewValues_GeneratesCorrectParameters()
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
        private void OnStatsChanged(int oldHealth, int newHealth, int oldMana, int newMana) { }
    }
}";

        var result = GeneratorTestHelper.RunGenerator(source);

        GeneratorTestHelper.AssertNoErrors(result);
        GeneratorTestHelper.AssertGeneratedContains(result, "OnStatsChanged(__old_Health, __reactive_Health, __old_Mana, __reactive_Mana)");
    }

    [Test]
    public void MultiSource_ThreeSources_GeneratesCorrectCode()
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

        [ReactiveSource]
        private int Stamina;

        [ReactiveBind(nameof(Health), nameof(Mana), nameof(Stamina))]
        private void OnStatsChanged(int h, int m, int s) { }
    }
}";

        var result = GeneratorTestHelper.RunGenerator(source);

        GeneratorTestHelper.AssertNoErrors(result);
        GeneratorTestHelper.AssertGeneratedContains(result, "__changed_Health || __changed_Mana || __changed_Stamina");
    }

    [Test]
    public void MixedBindings_SingleAndMulti_GeneratesCorrectCode()
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

        // Single source binding
        [ReactiveBind(nameof(Health))]
        private void OnHealthChanged(int oldValue, int newValue) { }

        // Multi source binding
        [ReactiveBind(nameof(Health), nameof(Mana))]
        private void OnStatsChanged() { }
    }
}";

        var result = GeneratorTestHelper.RunGenerator(source);

        GeneratorTestHelper.AssertNoErrors(result);
        // Single source binding should be called inside the if block
        GeneratorTestHelper.AssertGeneratedContains(result, "OnHealthChanged(__old_Health, __reactive_Health)");
        // Multi source binding should check both changed flags
        GeneratorTestHelper.AssertGeneratedContains(result, "__changed_Health || __changed_Mana");
    }
}
