using NUnit.Framework;

namespace ReactiveBinding.SourceGenerator.Tests;

/// <summary>
/// Tests for diagnostic error messages.
/// </summary>
[TestFixture]
public class DiagnosticTests
{
    [Test]
    public void DiagnosticIds_AreUniqueAndContinuousPerPrefix()
    {
        var assembly = typeof(ReactiveBinding.Generator.ReactiveBindGenerator).Assembly;
        var descriptorsType = assembly.GetType(
            "ReactiveBinding.Generator.DiagnosticDescriptors",
            throwOnError: true)!;
        var ids = descriptorsType
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(field => field.FieldType == typeof(Microsoft.CodeAnalysis.DiagnosticDescriptor))
            .Select(field => ((Microsoft.CodeAnalysis.DiagnosticDescriptor)field.GetValue(null)!).Id)
            .ToArray();
        var expected = Enumerable.Range(10001, 26)
            .Select(id => $"RB{id}")
            .Concat(Enumerable.Range(10001, 12).Select(id => $"VF{id}"))
            .Concat(Enumerable.Range(10001, 4).Select(id => $"VS{id}"))
            .ToArray();

        Assert.That(ids.Distinct(StringComparer.Ordinal).Count(), Is.EqualTo(ids.Length));
        Assert.That(
            ids.OrderBy(id => id, StringComparer.Ordinal),
            Is.EqualTo(expected.OrderBy(id => id, StringComparer.Ordinal)));
    }

    #region General RB diagnostics

    [Test]
    public void RB10007_UnmatchedSource_ProducesError()
    {
        var source = @"
namespace ReactiveBinding.Test
{
    public partial class TestClass : IReactiveObserver
    {
        [ReactiveSource]
        private int Health;

        // No binding for Health - should produce RB10007 error
    }
}";

        var result = GeneratorTestHelper.RunGenerator(source);

        GeneratorTestHelper.AssertHasDiagnostic(result, "RB10007");
    }

    [Test]
    public void RB10008_UnmatchedBind_ProducesError()
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

        GeneratorTestHelper.AssertHasDiagnostic(result, "RB10008");
    }

    [Test]
    public void RB10025_SourceNotMarked_ProducesError()
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

        GeneratorTestHelper.AssertHasDiagnostic(result, "RB10025");
    }

    [Test]
    public void RB10025_SourceNotMarked_StillGeneratesInterfaceImplementation()
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

        GeneratorTestHelper.AssertHasDiagnostic(result, "RB10025");
        // Even with invalid bindings, ObserveChanges/ResetChanges must still be generated
        // to avoid CS0535 "does not implement interface member" errors
        GeneratorTestHelper.AssertGeneratedContains(result, "void ObserveChanges()");
        GeneratorTestHelper.AssertGeneratedContains(result, "void ResetChanges()");
    }

    #endregion

    #region Class-level diagnostics (RB10001-RB10006)

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

    #region ReactiveSource diagnostics (RB10010-RB10015)

    [Test]
    public void RB10010_MethodReturnsVoid_ProducesError()
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

        GeneratorTestHelper.AssertHasDiagnostic(result, "RB10010");
    }

    [Test]
    public void RB10011_PropertyNoGetter_ProducesError()
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

        GeneratorTestHelper.AssertHasDiagnostic(result, "RB10011");
    }

    [Test]
    public void RB10012_MethodHasParameters_ProducesError()
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

        GeneratorTestHelper.AssertHasDiagnostic(result, "RB10012");
    }

    #endregion

    #region ReactiveBind diagnostics (RB10016-RB10026)

    [Test]
    public void RB10023_AutoInferEmptyBody_ProducesError()
    {
        // With auto-inference, [ReactiveBind()] triggers source detection in method body.
        // Empty method body means no sources found, resulting in RB10023.
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

        GeneratorTestHelper.AssertHasDiagnostic(result, "RB10023");
    }

    [Test]
    public void RB10017_MethodIsStatic_ProducesError()
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

        GeneratorTestHelper.AssertHasDiagnostic(result, "RB10017");
    }

    [Test]
    public void RB10018_MethodNotVoid_ProducesError()
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

        GeneratorTestHelper.AssertHasDiagnostic(result, "RB10018");
    }

    [Test]
    public void RB10019_InvalidParameterCount_ProducesError()
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

        GeneratorTestHelper.AssertHasDiagnostic(result, "RB10019");
    }

    [Test]
    public void RB10020_ParameterTypeMismatch_ProducesError()
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

        GeneratorTestHelper.AssertHasDiagnostic(result, "RB10020");
    }

    [Test]
    public void RB10021_DuplicateIds_ProducesError()
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

        GeneratorTestHelper.AssertHasDiagnostic(result, "RB10021");
    }

    [Test]
    public void RB10022_NotUsingNameof_ProducesError()
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

        GeneratorTestHelper.AssertHasDiagnostic(result, "RB10022");
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

    #region ObserveChanges Call Analyzer (RB10009)

    [Test]
    public async Task RB10009_NoObserveChangesCall_ProducesWarning()
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

        Assert.That(diagnostics.Any(d => d.Id == "RB10009"), Is.True);
    }

    [TestCase("ObserveChanges(1);")]
    [TestCase("this.ObserveChanges(1);")]
    public async Task RB10009_OnlyParameterizedObserveChangesCall_ProducesWarning(string invocation)
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

        void ObserveChanges(int value) { }

        void Update()
        {
            $INVOCATION$
        }
    }
}".Replace("$INVOCATION$", invocation);

        var diagnostics = await GeneratorTestHelper.RunObserveChangesCallAnalyzer(source);

        Assert.That(diagnostics.Any(d => d.Id == "RB10009"), Is.True);
    }

    [Test]
    public async Task RB10009_HasObserveChangesCall_NoDiagnostic()
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

        Assert.That(diagnostics.Any(d => d.Id == "RB10009"), Is.False);
    }

    [Test]
    public async Task RB10009_HasThisObserveChangesCall_NoDiagnostic()
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

        Assert.That(diagnostics.Any(d => d.Id == "RB10009"), Is.False);
    }

    [Test]
    public async Task RB10009_WithReactiveObserveIgnore_NoDiagnostic()
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

        Assert.That(diagnostics.Any(d => d.Id == "RB10009"), Is.False);
    }

    [Test]
    public async Task RB10009_DerivedClass_NoDiagnostic()
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

        Assert.That(diagnostics.Any(d => d.Id == "RB10009"), Is.False);
    }

    [Test]
    public async Task RB10009_NoReactiveMembers_NoDiagnostic()
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

        Assert.That(diagnostics.Any(d => d.Id == "RB10009"), Is.False);
    }

    #endregion
}
