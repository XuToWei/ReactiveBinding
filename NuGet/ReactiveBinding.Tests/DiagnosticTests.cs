using NUnit.Framework;

namespace ReactiveBinding.SourceGenerator.Tests;

/// <summary>
/// Tests for diagnostic error messages.
/// </summary>
[TestFixture]
public class DiagnosticTests
{
    #region RB0xxx

    [Test]
    public void RB0001_UnmatchedSource_ProducesError()
    {
        var source = @"
namespace ReactiveBinding.Test
{
    public partial class TestClass : IReactiveObserver
    {
        [ReactiveSource]
        private int Health;

        // No binding for Health - should produce RB0001 error
    }
}";

        var result = GeneratorTestHelper.RunGenerator(source);

        GeneratorTestHelper.AssertHasDiagnostic(result, "RB0001");
    }

    [Test]
    public void RB0002_UnmatchedBind_ProducesError()
    {
        var source = @"
namespace ReactiveBinding.Test
{
    public partial class TestClass : IReactiveObserver
    {
        [ReactiveSource]
        private int Health;

        // ""NonExistent"" does not exist as any member
        [ReactiveBind(""NonExistent"")]
        private void OnChanged() { }
    }
}";

        var result = GeneratorTestHelper.RunGenerator(source);

        GeneratorTestHelper.AssertHasDiagnostic(result, "RB0002");
    }

    [Test]
    public void RB30010_SourceNotMarked_ProducesError()
    {
        var source = @"
namespace ReactiveBinding.Test
{
    public partial class TestClass : IReactiveObserver
    {
        [ReactiveBind(nameof(Health))]
        private void OnHealthChanged() { }

        private int Health; // Exists but not marked with [ReactiveSource]
    }
}";

        var result = GeneratorTestHelper.RunGenerator(source);

        GeneratorTestHelper.AssertHasDiagnostic(result, "RB30010");
    }

    [Test]
    public void RB30010_SourceNotMarked_StillGeneratesInterfaceImplementation()
    {
        var source = @"
namespace ReactiveBinding.Test
{
    public partial class TestClass : IReactiveObserver
    {
        [ReactiveBind(nameof(Health))]
        private void OnHealthChanged() { }

        private int Health; // Exists but not marked with [ReactiveSource]
    }
}";

        var result = GeneratorTestHelper.RunGenerator(source);

        GeneratorTestHelper.AssertHasDiagnostic(result, "RB30010");
        // Even with invalid bindings, ObserveChanges/ResetChanges must still be generated
        // to avoid CS0535 "does not implement interface member" errors
        GeneratorTestHelper.AssertGeneratedContains(result, "void ObserveChanges()");
        GeneratorTestHelper.AssertGeneratedContains(result, "void ResetChanges()");
    }

    #endregion

    #region Class-level errors (RB1xxx)

    [Test]
    public void RB10001_ClassNotPartial_ProducesError()
    {
        var source = @"
namespace ReactiveBinding.Test
{
    public class TestClass : IReactiveObserver // Not partial
    {
        [ReactiveSource]
        private int Health;

        [ReactiveBind(nameof(Health))]
        private void OnHealthChanged() { }

        public void ObserveChanges() { }
    }
}";

        var result = GeneratorTestHelper.RunGenerator(source);

        GeneratorTestHelper.AssertHasDiagnostic(result, "RB10001");
    }

    [Test]
    public void RB10002_ClassNotImplementInterface_ProducesError()
    {
        var source = @"
namespace ReactiveBinding.Test
{
    public partial class TestClass // Does not implement IReactiveObserver
    {
        [ReactiveSource]
        private int Health;

        [ReactiveBind(nameof(Health))]
        private void OnHealthChanged() { }
    }
}";

        var result = GeneratorTestHelper.RunGenerator(source);

        GeneratorTestHelper.AssertHasDiagnostic(result, "RB10002");
    }

    [Test]
    public void RB10003_ThrottleInvalidValue_ProducesError()
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

        GeneratorTestHelper.AssertHasDiagnostic(result, "RB10003");
    }

    [Test]
    public void RB10004_ThrottleWithoutInterface_ProducesError()
    {
        var source = @"
namespace ReactiveBinding.Test
{
    [ReactiveThrottle(10)]
    public partial class TestClass // No IReactiveObserver
    {
        [ReactiveSource]
        private int Health;

        [ReactiveBind(nameof(Health))]
        private void OnHealthChanged() { }
    }
}";

        var result = GeneratorTestHelper.RunGenerator(source);

        GeneratorTestHelper.AssertHasDiagnostic(result, "RB10004");
    }

    #endregion

    #region ReactiveSource errors (RB2xxx)

    [Test]
    public void RB20001_MethodReturnsVoid_ProducesError()
    {
        var source = @"
namespace ReactiveBinding.Test
{
    public partial class TestClass : IReactiveObserver
    {
        [ReactiveSource]
        private void DoSomething() { } // Returns void

        [ReactiveBind(nameof(DoSomething))]
        private void OnChanged() { }
    }
}";

        var result = GeneratorTestHelper.RunGenerator(source);

        GeneratorTestHelper.AssertHasDiagnostic(result, "RB20001");
    }

    [Test]
    public void RB20002_PropertyNoGetter_ProducesError()
    {
        var source = @"
namespace ReactiveBinding.Test
{
    public partial class TestClass : IReactiveObserver
    {
        private int _health;

        [ReactiveSource]
        private int Health { set { _health = value; } } // No getter

        [ReactiveBind(nameof(Health))]
        private void OnHealthChanged() { }
    }
}";

        var result = GeneratorTestHelper.RunGenerator(source);

        GeneratorTestHelper.AssertHasDiagnostic(result, "RB20002");
    }

    [Test]
    public void RB20003_MethodHasParameters_ProducesError()
    {
        var source = @"
namespace ReactiveBinding.Test
{
    public partial class TestClass : IReactiveObserver
    {
        [ReactiveSource]
        private int GetValue(int multiplier) => 10 * multiplier; // Has parameters

        [ReactiveBind(nameof(GetValue))]
        private void OnValueChanged() { }
    }
}";

        var result = GeneratorTestHelper.RunGenerator(source);

        GeneratorTestHelper.AssertHasDiagnostic(result, "RB20003");
    }

    #endregion

    #region ReactiveBind errors (RB3xxx)

    [Test]
    public void RB30008_AutoInferEmptyBody_ProducesError()
    {
        // With auto-inference, [ReactiveBind()] triggers source detection in method body.
        // Empty method body means no sources found, resulting in RB30008.
        var source = @"
namespace ReactiveBinding.Test
{
    public partial class TestClass : IReactiveObserver
    {
        [ReactiveSource]
        private int Health;

        [ReactiveBind()] // Auto-inference with empty body
        private void OnChanged() { }
    }
}";

        var result = GeneratorTestHelper.RunGenerator(source);

        GeneratorTestHelper.AssertHasDiagnostic(result, "RB30008");
    }

    [Test]
    public void RB30002_MethodIsStatic_ProducesError()
    {
        var source = @"
namespace ReactiveBinding.Test
{
    public partial class TestClass : IReactiveObserver
    {
        [ReactiveSource]
        private int Health;

        [ReactiveBind(nameof(Health))]
        private static void OnHealthChanged() { } // Static method
    }
}";

        var result = GeneratorTestHelper.RunGenerator(source);

        GeneratorTestHelper.AssertHasDiagnostic(result, "RB30002");
    }

    [Test]
    public void RB30003_MethodNotVoid_ProducesError()
    {
        var source = @"
namespace ReactiveBinding.Test
{
    public partial class TestClass : IReactiveObserver
    {
        [ReactiveSource]
        private int Health;

        [ReactiveBind(nameof(Health))]
        private int OnHealthChanged() { return 0; } // Returns int
    }
}";

        var result = GeneratorTestHelper.RunGenerator(source);

        GeneratorTestHelper.AssertHasDiagnostic(result, "RB30003");
    }

    [Test]
    public void RB30004_InvalidParameterCount_ProducesError()
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

        // 2 sources but 3 parameters (should be 0, 2, or 4)
        [ReactiveBind(nameof(Health), nameof(Mana))]
        private void OnStatsChanged(int a, int b, int c) { }
    }
}";

        var result = GeneratorTestHelper.RunGenerator(source);

        GeneratorTestHelper.AssertHasDiagnostic(result, "RB30004");
    }

    [Test]
    public void RB30005_ParameterTypeMismatch_ProducesError()
    {
        var source = @"
namespace ReactiveBinding.Test
{
    public partial class TestClass : IReactiveObserver
    {
        [ReactiveSource]
        private int Health;

        // Parameter type is string but source type is int
        [ReactiveBind(nameof(Health))]
        private void OnHealthChanged(string oldValue, string newValue) { }
    }
}";

        var result = GeneratorTestHelper.RunGenerator(source);

        GeneratorTestHelper.AssertHasDiagnostic(result, "RB30005");
    }

    [Test]
    public void RB30006_DuplicateIds_ProducesError()
    {
        var source = @"
namespace ReactiveBinding.Test
{
    public partial class TestClass : IReactiveObserver
    {
        [ReactiveSource]
        private int Health;

        // Duplicate Health id
        [ReactiveBind(nameof(Health), nameof(Health))]
        private void OnHealthChanged() { }
    }
}";

        var result = GeneratorTestHelper.RunGenerator(source);

        GeneratorTestHelper.AssertHasDiagnostic(result, "RB30006");
    }

    [Test]
    public void RB30007_NotUsingNameof_ProducesError()
    {
        var source = @"
namespace ReactiveBinding.Test
{
    public partial class TestClass : IReactiveObserver
    {
        [ReactiveSource]
        private int Health;

        // Using string literal instead of nameof()
        [ReactiveBind(""Health"")]
        private void OnHealthChanged() { }
    }
}";

        var result = GeneratorTestHelper.RunGenerator(source);

        GeneratorTestHelper.AssertHasDiagnostic(result, "RB30007");
    }

    #endregion

    #region Valid cases that should not produce errors

    [Test]
    public void ValidClass_NoErrors()
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
    }

    [Test]
    public void IReactiveObserver_WithoutMarkers_GeneratesEmptyObserveChanges()
    {
        var source = @"
namespace ReactiveBinding.Test
{
    public partial class TestClass : IReactiveObserver
    {
        // No ReactiveSource or ReactiveBind markers
        private int Health;

        public void DoSomething() { }
    }
}";

        var result = GeneratorTestHelper.RunGenerator(source);

        GeneratorTestHelper.AssertNoErrors(result);
        GeneratorTestHelper.AssertGeneratedContains(result, "public virtual void ObserveChanges()");
    }

    [Test]
    public void IReactiveObserver_WithoutMarkers_NotPartial_ProducesError()
    {
        var source = @"
namespace ReactiveBinding.Test
{
    public class TestClass : IReactiveObserver // Not partial
    {
        // No ReactiveSource or ReactiveBind markers
        public void ObserveChanges() { }
    }
}";

        var result = GeneratorTestHelper.RunGenerator(source);

        GeneratorTestHelper.AssertHasDiagnostic(result, "RB10001");
    }

    [Test]
    public void ValidMultiSource_NoErrors()
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
    }

    #endregion

    #region Reserved Method Analyzer (RB10005, RB10006)

    [Test]
    public async Task RB10005_ManualObserveChanges_ProducesError()
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

        public void ObserveChanges() { }
        public void ResetChanges() { }
    }
}";

        var diagnostics = await GeneratorTestHelper.RunReservedMethodAnalyzer(source);

        Assert.That(diagnostics, Has.Length.EqualTo(2));
        Assert.That(diagnostics.Any(d => d.Id == "RB10005"), Is.True);
        Assert.That(diagnostics.Any(d => d.Id == "RB10006"), Is.True);
    }

    [Test]
    public async Task RB10005_ManualObserveChangesOnly_ProducesError()
    {
        var source = @"
namespace ReactiveBinding.Test
{
    public partial class TestClass : IReactiveObserver
    {
        public void ObserveChanges() { }
        public void ResetChanges() { }
    }
}";

        var diagnostics = await GeneratorTestHelper.RunReservedMethodAnalyzer(source);

        Assert.That(diagnostics, Has.Length.EqualTo(2));
        Assert.That(diagnostics.Any(d => d.Id == "RB10005"), Is.True);
        Assert.That(diagnostics.Any(d => d.Id == "RB10006"), Is.True);
    }

    [Test]
    public async Task RB10005_NotIReactiveObserver_NoDiagnostic()
    {
        var source = @"
namespace ReactiveBinding.Test
{
    public class RegularClass
    {
        public void ObserveChanges() { }
        public void ResetChanges() { }
    }
}";

        var diagnostics = await GeneratorTestHelper.RunReservedMethodAnalyzer(source);

        Assert.That(diagnostics, Has.Length.EqualTo(0));
    }

    [Test]
    public async Task RB10005_MethodWithParameters_NoDiagnostic()
    {
        var source = @"
namespace ReactiveBinding.Test
{
    public partial class TestClass : IReactiveObserver
    {
        public void ObserveChanges(int x) { }
        public void ResetChanges(int x) { }
    }
}";

        var diagnostics = await GeneratorTestHelper.RunReservedMethodAnalyzer(source);

        Assert.That(diagnostics, Has.Length.EqualTo(0));
    }

    #endregion

    #region ObserveChanges Call Analyzer (RB0003)

    [Test]
    public async Task RB0003_NoObserveChangesCall_ProducesWarning()
    {
        var source = @"
namespace ReactiveBinding.Test
{
    public partial class TestClass : IReactiveObserver
    {
        [ReactiveSource]
        private int Health;

        [ReactiveBind(nameof(Health))]
        void OnHealthChanged() { }

        void Update() { }
    }
}";

        var diagnostics = await GeneratorTestHelper.RunObserveChangesCallAnalyzer(source);

        Assert.That(diagnostics.Any(d => d.Id == "RB0003"), Is.True);
    }

    [Test]
    public async Task RB0003_HasObserveChangesCall_NoDiagnostic()
    {
        var source = @"
namespace ReactiveBinding.Test
{
    public partial class TestClass : IReactiveObserver
    {
        [ReactiveSource]
        private int Health;

        [ReactiveBind(nameof(Health))]
        void OnHealthChanged() { }

        void Update()
        {
            ObserveChanges();
        }
    }
}";

        var diagnostics = await GeneratorTestHelper.RunObserveChangesCallAnalyzer(source);

        Assert.That(diagnostics.Any(d => d.Id == "RB0003"), Is.False);
    }

    [Test]
    public async Task RB0003_HasThisObserveChangesCall_NoDiagnostic()
    {
        var source = @"
namespace ReactiveBinding.Test
{
    public partial class TestClass : IReactiveObserver
    {
        [ReactiveSource]
        private int Health;

        [ReactiveBind(nameof(Health))]
        void OnHealthChanged() { }

        void Update()
        {
            this.ObserveChanges();
        }
    }
}";

        var diagnostics = await GeneratorTestHelper.RunObserveChangesCallAnalyzer(source);

        Assert.That(diagnostics.Any(d => d.Id == "RB0003"), Is.False);
    }

    [Test]
    public async Task RB0003_WithReactiveObserveIgnore_NoDiagnostic()
    {
        var source = @"
namespace ReactiveBinding.Test
{
    [ReactiveObserveIgnore]
    public partial class TestClass : IReactiveObserver
    {
        [ReactiveSource]
        private int Health;

        [ReactiveBind(nameof(Health))]
        void OnHealthChanged() { }
    }
}";

        var diagnostics = await GeneratorTestHelper.RunObserveChangesCallAnalyzer(source);

        Assert.That(diagnostics.Any(d => d.Id == "RB0003"), Is.False);
    }

    [Test]
    public async Task RB0003_DerivedClass_NoDiagnostic()
    {
        var source = @"
namespace ReactiveBinding.Test
{
    public partial class BaseClass : IReactiveObserver
    {
        [ReactiveSource]
        private int Health;

        [ReactiveBind(nameof(Health))]
        void OnHealthChanged() { }

        void Update()
        {
            ObserveChanges();
        }
    }

    public partial class DerivedClass : BaseClass
    {
        [ReactiveSource]
        private int Mana;

        [ReactiveBind(nameof(Mana))]
        void OnManaChanged() { }
    }
}";

        var diagnostics = await GeneratorTestHelper.RunObserveChangesCallAnalyzer(source);

        Assert.That(diagnostics.Any(d => d.Id == "RB0003"), Is.False);
    }

    [Test]
    public async Task RB0003_NoReactiveMembers_NoDiagnostic()
    {
        var source = @"
namespace ReactiveBinding.Test
{
    public partial class TestClass : IReactiveObserver
    {
        void Update() { }
    }
}";

        var diagnostics = await GeneratorTestHelper.RunObserveChangesCallAnalyzer(source);

        Assert.That(diagnostics.Any(d => d.Id == "RB0003"), Is.False);
    }

    #endregion
}
