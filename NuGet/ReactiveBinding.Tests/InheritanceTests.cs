using NUnit.Framework;

namespace ReactiveBinding.SourceGenerator.Tests;

[TestFixture]
public class InheritanceTests
{
    [Test]
    public void SingleClass_NoVirtual()
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
        Assert.That(generated, Does.Contain("public void ObserveChanges()"));
        Assert.That(generated, Does.Contain("public void ResetChanges()"));
        Assert.That(generated, Does.Not.Contain("virtual"));
        Assert.That(generated, Does.Not.Contain("base.ObserveChanges()"));
    }

    [Test]
    public void BaseAndDerived_BothWithReactiveMembers()
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
        private void OnManaChanged() { }
    }
}";

        var result = GeneratorTestHelper.RunGenerator(source);
        GeneratorTestHelper.AssertNoErrors(result);

        // Base should have virtual
        var baseGenerated = GeneratorTestHelper.GetGeneratedForClass(result, "BaseUI");
        Assert.That(baseGenerated, Is.Not.Null);
        Assert.That(baseGenerated, Does.Contain("public virtual void ObserveChanges()"));
        Assert.That(baseGenerated, Does.Contain("public virtual void ResetChanges()"));
        Assert.That(baseGenerated, Does.Not.Contain("base.ObserveChanges()"));

        // Derived should have override + base call
        var derivedGenerated = GeneratorTestHelper.GetGeneratedForClass(result, "DerivedUI");
        Assert.That(derivedGenerated, Is.Not.Null);
        Assert.That(derivedGenerated, Does.Contain("public override void ObserveChanges()"));
        Assert.That(derivedGenerated, Does.Contain("base.ObserveChanges()"));
        Assert.That(derivedGenerated, Does.Contain("public override void ResetChanges()"));
        Assert.That(derivedGenerated, Does.Contain("base.ResetChanges()"));
        Assert.That(derivedGenerated, Does.Contain("__reactive_Mana"));
    }

    [Test]
    public void BaseWithReactive_DerivedWithNone_SkipsDerived()
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
        // No reactive members - should inherit from base
    }
}";

        var result = GeneratorTestHelper.RunGenerator(source);
        GeneratorTestHelper.AssertNoErrors(result);

        var baseGenerated = GeneratorTestHelper.GetGeneratedForClass(result, "BaseUI");
        Assert.That(baseGenerated, Is.Not.Null);
        // No virtual needed since derived has no reactive members
        Assert.That(baseGenerated, Does.Contain("public void ObserveChanges()"));
        Assert.That(baseGenerated, Does.Not.Contain("virtual"));

        // Derived should have no generated code
        var derivedGenerated = GeneratorTestHelper.GetGeneratedForClass(result, "DerivedUI");
        Assert.That(derivedGenerated, Is.Null);
    }

    [Test]
    public void BaseEmpty_DerivedWithReactive()
    {
        var source = @"
namespace ReactiveBinding.Test
{
    public partial class BaseUI : IReactiveObserver
    {
        // No reactive members, just implements interface
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
        GeneratorTestHelper.AssertNoErrors(result);

        // Base should generate empty virtual methods
        var baseGenerated = GeneratorTestHelper.GetGeneratedForClass(result, "BaseUI");
        Assert.That(baseGenerated, Is.Not.Null);
        Assert.That(baseGenerated, Does.Contain("public virtual void ObserveChanges()"));
        Assert.That(baseGenerated, Does.Contain("public virtual void ResetChanges()"));

        // Derived should have override
        var derivedGenerated = GeneratorTestHelper.GetGeneratedForClass(result, "DerivedUI");
        Assert.That(derivedGenerated, Is.Not.Null);
        Assert.That(derivedGenerated, Does.Contain("public override void ObserveChanges()"));
        Assert.That(derivedGenerated, Does.Contain("base.ObserveChanges()"));
        Assert.That(derivedGenerated, Does.Contain("public override void ResetChanges()"));
        Assert.That(derivedGenerated, Does.Contain("base.ResetChanges()"));
    }

    [Test]
    public void ThreeLevelInheritance_AllWithReactive()
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
        GeneratorTestHelper.AssertNoErrors(result);

        // LevelA: virtual, no base call
        var aGenerated = GeneratorTestHelper.GetGeneratedForClass(result, "LevelA");
        Assert.That(aGenerated, Does.Contain("public virtual void ObserveChanges()"));
        Assert.That(aGenerated, Does.Not.Contain("base.ObserveChanges()"));

        // LevelB: override, base call
        var bGenerated = GeneratorTestHelper.GetGeneratedForClass(result, "LevelB");
        Assert.That(bGenerated, Does.Contain("public override void ObserveChanges()"));
        Assert.That(bGenerated, Does.Contain("base.ObserveChanges()"));

        // LevelC: override, base call
        var cGenerated = GeneratorTestHelper.GetGeneratedForClass(result, "LevelC");
        Assert.That(cGenerated, Does.Contain("public override void ObserveChanges()"));
        Assert.That(cGenerated, Does.Contain("base.ObserveChanges()"));
    }

    [Test]
    public void DerivedWithThrottle_GeneratesCorrectly()
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

    [ReactiveThrottle(5)]
    public partial class DerivedUI : BaseUI
    {
        [ReactiveSource]
        private int Mana;

        [ReactiveBind(nameof(Mana))]
        private void OnManaChanged() { }
    }
}";

        var result = GeneratorTestHelper.RunGenerator(source);
        GeneratorTestHelper.AssertNoErrors(result);

        var derivedGenerated = GeneratorTestHelper.GetGeneratedForClass(result, "DerivedUI");
        Assert.That(derivedGenerated, Does.Contain("public override void ObserveChanges()"));
        Assert.That(derivedGenerated, Does.Contain("base.ObserveChanges()"));
        Assert.That(derivedGenerated, Does.Contain("__reactive_callCount"));
    }

    [Test]
    public async Task ReservedMethodAnalyzer_DerivedWithNoReactive_AllowsManualImpl()
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
        // No reactive attributes - manual override should be allowed
        public void ObserveChanges()
        {
        }

        public void ResetChanges()
        {
        }
    }
}";

        var diagnostics = await GeneratorTestHelper.RunReservedMethodAnalyzer(source);

        // DerivedUI should be flagged (implements IReactiveObserver via inheritance)
        // BaseUI doesn't have manual ObserveChanges/ResetChanges, so it's not flagged
        Assert.That(diagnostics.Count(d => d.Id == "RB1005"), Is.EqualTo(1));
        Assert.That(diagnostics.Count(d => d.Id == "RB1006"), Is.EqualTo(1));
    }

    [Test]
    public async Task ReservedMethodAnalyzer_DerivedWithReactive_StillFlagged()
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
        private void OnManaChanged() { }

        // Manual impl with reactive attrs should be flagged
        public void ObserveChanges()
        {
        }
    }
}";

        var diagnostics = await GeneratorTestHelper.RunReservedMethodAnalyzer(source);

        // Only DerivedUI should be flagged (has reactive attributes + manual ObserveChanges)
        // BaseUI doesn't have manual ObserveChanges, so it's not flagged
        Assert.That(diagnostics.Count(d => d.Id == "RB1005"), Is.EqualTo(1));
    }
}
