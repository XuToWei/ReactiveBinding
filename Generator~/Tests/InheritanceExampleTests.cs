using NUnit.Framework;

namespace ReactiveBinding.SourceGenerator.Tests;

[TestFixture]
public class InheritanceExampleTests
{
    // Helper to print generated code for review
    private static void PrintGenerated(GeneratorRunResult result, params string[] classNames)
    {
        foreach (var name in classNames)
        {
            var gen = GeneratorTestHelper.GetGeneratedForClass(result, name);
            TestContext.WriteLine($"=== {name} ===");
            TestContext.WriteLine(gen ?? "(no code generated)");
            TestContext.WriteLine();
        }
        // Print diagnostics
        if (result.Diagnostics.Length > 0)
        {
            TestContext.WriteLine("=== Diagnostics ===");
            foreach (var d in result.Diagnostics)
                TestContext.WriteLine($"{d.Id}: {d.GetMessage()}");
        }
    }

    [Test]
    public void Example1_SingleClass_NoDerived()
    {
        var source = @"
namespace ReactiveBinding.Test
{
    public partial class PlayerUI : IReactiveObserver
    {
        [ReactiveSource]
        private int Health;

        [ReactiveBind(nameof(Health))]
        private void OnHealthChanged(int oldValue, int newValue) { }
    }
}";
        var result = GeneratorTestHelper.RunGenerator(source);
        PrintGenerated(result, "PlayerUI");
    }

    [Test]
    public void Example2_BaseWithBind_DerivedWithBind()
    {
        var source = @"
namespace ReactiveBinding.Test
{
    public partial class BaseUI : IReactiveObserver
    {
        [ReactiveSource]
        protected int Health;

        [ReactiveBind(nameof(Health))]
        private void OnHealthChanged() { }
    }

    public partial class DerivedUI : BaseUI
    {
        [ReactiveSource]
        private int Mana;

        [ReactiveBind(nameof(Mana))]
        private void OnManaChanged(int newValue) { }
    }
}";
        var result = GeneratorTestHelper.RunGenerator(source);
        PrintGenerated(result, "BaseUI", "DerivedUI");
    }

    [Test]
    public void Example3_BaseWithBind_DerivedNoBind()
    {
        var source = @"
namespace ReactiveBinding.Test
{
    public partial class BaseUI : IReactiveObserver
    {
        [ReactiveSource]
        protected int Health;

        [ReactiveBind(nameof(Health))]
        private void OnHealthChanged() { }
    }

    public partial class DerivedUI : BaseUI
    {
        // No reactive attributes at all
    }
}";
        var result = GeneratorTestHelper.RunGenerator(source);
        PrintGenerated(result, "BaseUI", "DerivedUI");
    }

    [Test]
    public void Example4_BaseWithBind_DerivedOnlySource()
    {
        var source = @"
namespace ReactiveBinding.Test
{
    public partial class BaseUI : IReactiveObserver
    {
        [ReactiveSource]
        protected int Health;

        [ReactiveBind(nameof(Health))]
        private void OnHealthChanged() { }
    }

    public partial class DerivedUI : BaseUI
    {
        [ReactiveSource]
        private int Mana;
        // Has source but no bind
    }
}";
        var result = GeneratorTestHelper.RunGenerator(source);
        PrintGenerated(result, "BaseUI", "DerivedUI");
    }

    [Test]
    public void Example5_BaseEmpty_DerivedWithBind()
    {
        var source = @"
namespace ReactiveBinding.Test
{
    public partial class BaseUI : IReactiveObserver
    {
        // No reactive members
    }

    public partial class DerivedUI : BaseUI
    {
        [ReactiveSource]
        private int Mana;

        [ReactiveBind(nameof(Mana))]
        private void OnManaChanged() { }
    }
}";
        var result = GeneratorTestHelper.RunGenerator(source);
        PrintGenerated(result, "BaseUI", "DerivedUI");
    }

    [Test]
    public void Example6_ThreeLevel()
    {
        var source = @"
namespace ReactiveBinding.Test
{
    public partial class LevelA : IReactiveObserver
    {
        [ReactiveSource]
        protected int Health;

        [ReactiveBind(nameof(Health))]
        private void OnHealthChanged() { }
    }

    public partial class LevelB : LevelA
    {
        [ReactiveSource]
        protected int Mana;

        [ReactiveBind(nameof(Mana))]
        private void OnManaChanged() { }
    }

    public partial class LevelC : LevelB
    {
        [ReactiveSource]
        private int Stamina;

        [ReactiveBind(nameof(Stamina))]
        private void OnStaminaChanged() { }
    }
}";
        var result = GeneratorTestHelper.RunGenerator(source);
        PrintGenerated(result, "LevelA", "LevelB", "LevelC");
    }
}
