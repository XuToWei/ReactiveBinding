using NUnit.Framework;

namespace ReactiveBinding.SourceGenerator.Tests;

/// <summary>
/// Tests for basic field/property/method change detection.
/// </summary>
[TestFixture]
public class BasicChangeDetectionTests
{
    [Test]
    public void Field_WithSingleBind_GeneratesChangeDetection()
    {
        var source = @"
namespace TestNamespace
{
    public partial class TestClass : IReactiveObserver
    {
        [ReactiveSource]
        private int _health;

        [ReactiveBind(nameof(_health))]
        private void OnHealthChanged(int oldValue, int newValue) { }
    }
}";

        var result = GeneratorTestHelper.RunGenerator(source);

        GeneratorTestHelper.AssertNoErrors(result);
        GeneratorTestHelper.AssertGeneratedContains(result, "partial class TestClass");
        GeneratorTestHelper.AssertGeneratedContains(result, "public void ObserveChanges()");
        GeneratorTestHelper.AssertGeneratedContains(result, "__reactive__health");
        GeneratorTestHelper.AssertGeneratedContains(result, "OnHealthChanged");
    }

    [Test]
    public void Property_WithSingleBind_GeneratesChangeDetection()
    {
        var source = @"
namespace TestNamespace
{
    public partial class TestClass : IReactiveObserver
    {
        private int _healthValue;

        [ReactiveSource]
        private int Health => _healthValue;

        [ReactiveBind(nameof(Health))]
        private void OnHealthChanged(int oldValue, int newValue) { }
    }
}";

        var result = GeneratorTestHelper.RunGenerator(source);

        GeneratorTestHelper.AssertNoErrors(result);
        GeneratorTestHelper.AssertGeneratedContains(result, "__reactive_Health");
        GeneratorTestHelper.AssertGeneratedContains(result, "Health");
    }

    [Test]
    public void Method_WithSingleBind_GeneratesChangeDetection()
    {
        var source = @"
namespace TestNamespace
{
    public partial class TestClass : IReactiveObserver
    {
        private int _mana;

        [ReactiveSource]
        private int GetMana() => _mana;

        [ReactiveBind(nameof(GetMana))]
        private void OnManaChanged(int oldValue, int newValue) { }
    }
}";

        var result = GeneratorTestHelper.RunGenerator(source);

        GeneratorTestHelper.AssertNoErrors(result);
        GeneratorTestHelper.AssertGeneratedContains(result, "__reactive_GetMana");
        GeneratorTestHelper.AssertGeneratedContains(result, "GetMana()");
        GeneratorTestHelper.AssertGeneratedContains(result, "__current_GetMana");
    }

    [Test]
    public void Bind_WithNoParameters_GeneratesCorrectCall()
    {
        var source = @"
namespace TestNamespace
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
        GeneratorTestHelper.AssertGeneratedContains(result, "OnHealthChanged()");
    }

    [Test]
    public void Bind_WithNewValueOnly_GeneratesCorrectCall()
    {
        var source = @"
namespace TestNamespace
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
        GeneratorTestHelper.AssertGeneratedContains(result, "__reactive_Health");
    }

    [Test]
    public void Bind_WithOldAndNewValue_GeneratesCorrectCall()
    {
        var source = @"
namespace TestNamespace
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
        GeneratorTestHelper.AssertGeneratedContains(result, "__old_Health");
        GeneratorTestHelper.AssertGeneratedContains(result, "__reactive_Health");
    }

    [Test]
    public void SingleSource_MultipleBinds_GeneratesAllCallbacks()
    {
        var source = @"
namespace TestNamespace
{
    public partial class TestClass : IReactiveObserver
    {
        [ReactiveSource]
        private int Health;

        [ReactiveBind(nameof(Health))]
        private void OnHealthChanged1() { }

        [ReactiveBind(nameof(Health))]
        private void OnHealthChanged2(int newValue) { }

        [ReactiveBind(nameof(Health))]
        private void OnHealthChanged3(int oldValue, int newValue) { }
    }
}";

        var result = GeneratorTestHelper.RunGenerator(source);

        GeneratorTestHelper.AssertNoErrors(result);
        GeneratorTestHelper.AssertGeneratedContains(result, "OnHealthChanged1()");
        GeneratorTestHelper.AssertGeneratedContains(result, "OnHealthChanged2");
        GeneratorTestHelper.AssertGeneratedContains(result, "OnHealthChanged3");
    }
}
