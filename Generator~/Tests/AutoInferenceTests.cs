using NUnit.Framework;

namespace ReactiveBinding.SourceGenerator.Tests;

/// <summary>
/// Tests for auto-inference of ReactiveSource bindings.
/// </summary>
[TestFixture]
public class AutoInferenceTests
{
    [Test]
    public void AutoInfer_SingleField_GeneratesBinding()
    {
        var source = @"
namespace ReactiveBinding.Test
{
    public partial class TestClass : IReactiveObserver
    {
        [ReactiveSource]
        private int Health;

        [ReactiveBind]
        private void OnHealthChanged()
        {
            var value = Health;
        }
    }
}";

        var result = GeneratorTestHelper.RunGenerator(source);

        GeneratorTestHelper.AssertNoErrors(result);
        GeneratorTestHelper.AssertGeneratedContains(result, "OnHealthChanged()");
        GeneratorTestHelper.AssertGeneratedContains(result, "__reactive_Health");
    }

    [Test]
    public void AutoInfer_MultipleFields_GeneratesAllBindings()
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

        [ReactiveBind]
        private void OnStatsChanged()
        {
            var total = Health + Mana;
        }
    }
}";

        var result = GeneratorTestHelper.RunGenerator(source);

        GeneratorTestHelper.AssertNoErrors(result);
        GeneratorTestHelper.AssertGeneratedContains(result, "OnStatsChanged()");
        GeneratorTestHelper.AssertGeneratedContains(result, "__reactive_Health");
        GeneratorTestHelper.AssertGeneratedContains(result, "__reactive_Mana");
        GeneratorTestHelper.AssertGeneratedContains(result, "__changed_Health");
        GeneratorTestHelper.AssertGeneratedContains(result, "__changed_Mana");
    }

    [Test]
    public void AutoInfer_ThisAccess_GeneratesBinding()
    {
        var source = @"
namespace ReactiveBinding.Test
{
    public partial class TestClass : IReactiveObserver
    {
        [ReactiveSource]
        private int Health;

        [ReactiveBind]
        private void OnHealthChanged()
        {
            var value = this.Health;
        }
    }
}";

        var result = GeneratorTestHelper.RunGenerator(source);

        GeneratorTestHelper.AssertNoErrors(result);
        GeneratorTestHelper.AssertGeneratedContains(result, "OnHealthChanged()");
        GeneratorTestHelper.AssertGeneratedContains(result, "__reactive_Health");
    }

    [Test]
    public void AutoInfer_PropertySource_GeneratesBinding()
    {
        var source = @"
namespace ReactiveBinding.Test
{
    public partial class TestClass : IReactiveObserver
    {
        private int _healthValue;

        [ReactiveSource]
        private int Health => _healthValue;

        [ReactiveBind]
        private void OnHealthChanged()
        {
            var value = Health;
        }
    }
}";

        var result = GeneratorTestHelper.RunGenerator(source);

        GeneratorTestHelper.AssertNoErrors(result);
        GeneratorTestHelper.AssertGeneratedContains(result, "OnHealthChanged()");
        GeneratorTestHelper.AssertGeneratedContains(result, "__reactive_Health");
    }

    [Test]
    public void AutoInfer_MethodSource_GeneratesBinding()
    {
        var source = @"
namespace ReactiveBinding.Test
{
    public partial class TestClass : IReactiveObserver
    {
        private int _mana;

        [ReactiveSource]
        private int GetMana() => _mana;

        [ReactiveBind]
        private void OnManaChanged()
        {
            var value = GetMana();
        }
    }
}";

        var result = GeneratorTestHelper.RunGenerator(source);

        GeneratorTestHelper.AssertNoErrors(result);
        GeneratorTestHelper.AssertGeneratedContains(result, "OnManaChanged()");
        GeneratorTestHelper.AssertGeneratedContains(result, "__reactive_GetMana");
        GeneratorTestHelper.AssertGeneratedContains(result, "GetMana()");
    }

    [Test]
    public void AutoInfer_ExpressionBodyMethod_GeneratesBinding()
    {
        var source = @"
namespace ReactiveBinding.Test
{
    public partial class TestClass : IReactiveObserver
    {
        [ReactiveSource]
        private int Health;

        [ReactiveBind]
        private void OnHealthChanged() => Console.WriteLine(Health);
    }
}";

        var result = GeneratorTestHelper.RunGenerator(source);

        GeneratorTestHelper.AssertNoErrors(result);
        GeneratorTestHelper.AssertGeneratedContains(result, "OnHealthChanged()");
        GeneratorTestHelper.AssertGeneratedContains(result, "__reactive_Health");
    }

    [Test]
    public void AutoInfer_LambdaReference_GeneratesBinding()
    {
        var source = @"
namespace ReactiveBinding.Test
{
    public partial class TestClass : IReactiveObserver
    {
        [ReactiveSource]
        private int Health;

        [ReactiveBind]
        private void OnHealthChanged()
        {
            System.Func<int> getter = () => Health;
        }
    }
}";

        var result = GeneratorTestHelper.RunGenerator(source);

        GeneratorTestHelper.AssertNoErrors(result);
        GeneratorTestHelper.AssertGeneratedContains(result, "OnHealthChanged()");
        GeneratorTestHelper.AssertGeneratedContains(result, "__reactive_Health");
    }

    [Test]
    public void AutoInfer_EmptyMethodBody_ReportsRB3008()
    {
        var source = @"
namespace ReactiveBinding.Test
{
    public partial class TestClass : IReactiveObserver
    {
        [ReactiveSource]
        private int Health;

        [ReactiveBind]
        private void OnHealthChanged()
        {
            // Empty - no source references
        }
    }
}";

        var result = GeneratorTestHelper.RunGenerator(source);

        GeneratorTestHelper.AssertHasDiagnostic(result, "RB3008");
    }

    [Test]
    public void AutoInfer_NoSourceReferences_ReportsRB3008()
    {
        var source = @"
namespace ReactiveBinding.Test
{
    public partial class TestClass : IReactiveObserver
    {
        [ReactiveSource]
        private int Health;

        private int Armor; // Not a source

        [ReactiveBind]
        private void OnArmorChanged()
        {
            var value = Armor; // References non-source field
        }
    }
}";

        var result = GeneratorTestHelper.RunGenerator(source);

        GeneratorTestHelper.AssertHasDiagnostic(result, "RB3008");
    }

    [Test]
    public void AutoInfer_LocalVariableShadowing_IgnoresShadowedName()
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

        [ReactiveBind]
        private void OnStatsChanged()
        {
            int Health = 10; // Shadows the field
            var value = Health + Mana; // Health refers to local, Mana refers to field
        }
    }
}";

        var result = GeneratorTestHelper.RunGenerator(source);

        GeneratorTestHelper.AssertNoErrors(result);
        // Should only bind to Mana since Health is shadowed
        GeneratorTestHelper.AssertGeneratedContains(result, "__reactive_Mana");
        // Health should be flagged as unused since it's shadowed
        GeneratorTestHelper.AssertHasDiagnostic(result, "RB0001");
    }

    [Test]
    public void AutoInfer_IgnoresNonSourceMembers()
    {
        var source = @"
namespace ReactiveBinding.Test
{
    public partial class TestClass : IReactiveObserver
    {
        [ReactiveSource]
        private int Health;

        private int Armor;  // Not a source
        private string Name; // Not a source

        [ReactiveBind]
        private void OnHealthChanged()
        {
            var total = Health + Armor;
            Console.WriteLine(Name);
        }
    }
}";

        var result = GeneratorTestHelper.RunGenerator(source);

        GeneratorTestHelper.AssertNoErrors(result);
        GeneratorTestHelper.AssertGeneratedContains(result, "__reactive_Health");
    }

    [Test]
    public void AutoInfer_MixedWithExplicitBind_WorksCorrectly()
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
        private void OnHealthChanged()
        {
            Console.WriteLine(Health);
        }

        [ReactiveBind]
        private void OnManaChanged()
        {
            var value = Mana;
        }
    }
}";

        var result = GeneratorTestHelper.RunGenerator(source);

        GeneratorTestHelper.AssertNoErrors(result);
        GeneratorTestHelper.AssertGeneratedContains(result, "OnHealthChanged()");
        GeneratorTestHelper.AssertGeneratedContains(result, "OnManaChanged()");
    }

    [Test]
    public void AutoInfer_ThisMethodCall_GeneratesBinding()
    {
        var source = @"
namespace ReactiveBinding.Test
{
    public partial class TestClass : IReactiveObserver
    {
        private int _mana;

        [ReactiveSource]
        private int GetMana() => _mana;

        [ReactiveBind]
        private void OnManaChanged()
        {
            var value = this.GetMana();
        }
    }
}";

        var result = GeneratorTestHelper.RunGenerator(source);

        GeneratorTestHelper.AssertNoErrors(result);
        GeneratorTestHelper.AssertGeneratedContains(result, "OnManaChanged()");
        GeneratorTestHelper.AssertGeneratedContains(result, "__reactive_GetMana");
    }

    [Test]
    public void AutoInfer_PreservesOrderOfFirstAppearance()
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
        private int Armor;

        [ReactiveBind]
        private void OnStatsChanged()
        {
            // Order: Mana, Health, Armor
            var a = Mana;
            var b = Health;
            var c = Armor;
        }
    }
}";

        var result = GeneratorTestHelper.RunGenerator(source);

        GeneratorTestHelper.AssertNoErrors(result);
        // All three should be bound
        GeneratorTestHelper.AssertGeneratedContains(result, "__reactive_Health");
        GeneratorTestHelper.AssertGeneratedContains(result, "__reactive_Mana");
        GeneratorTestHelper.AssertGeneratedContains(result, "__reactive_Armor");
    }

    [Test]
    public void AutoInfer_WithParameters_ReportsRB3009()
    {
        var source = @"
namespace ReactiveBinding.Test
{
    public partial class TestClass : IReactiveObserver
    {
        [ReactiveSource]
        private int Health;

        [ReactiveBind] // Auto-inference with parameters is not allowed
        private void OnHealthChanged(int newValue)
        {
            var value = Health;
        }
    }
}";

        var result = GeneratorTestHelper.RunGenerator(source);

        GeneratorTestHelper.AssertHasDiagnostic(result, "RB3009");
    }

    [Test]
    public void AutoInfer_WithOldNewParameters_ReportsRB3009()
    {
        var source = @"
namespace ReactiveBinding.Test
{
    public partial class TestClass : IReactiveObserver
    {
        [ReactiveSource]
        private int Health;

        [ReactiveBind] // Auto-inference with parameters is not allowed
        private void OnHealthChanged(int oldValue, int newValue)
        {
            var value = Health;
        }
    }
}";

        var result = GeneratorTestHelper.RunGenerator(source);

        GeneratorTestHelper.AssertHasDiagnostic(result, "RB3009");
    }
}
