using NUnit.Framework;

namespace ReactiveBinding.SourceGenerator.Tests;

/// <summary>
/// Tests for diagnostic error messages.
/// </summary>
[TestFixture]
public class DiagnosticTests
{
    #region Warnings (RB0xxx)

    [Test]
    public void RB0001_UnmatchedSource_ProducesWarning()
    {
        var source = @"
namespace TestNamespace
{
    public partial class TestClass : IReactiveObserver
    {
        [ReactiveSource]
        private int Health;

        // No binding for Health - should produce RB0001 warning
    }
}";

        var result = GeneratorTestHelper.RunGenerator(source);

        GeneratorTestHelper.AssertHasDiagnostic(result, "RB0001");
    }

    [Test]
    public void RB0002_UnmatchedBind_ProducesError()
    {
        var source = @"
namespace TestNamespace
{
    public partial class TestClass : IReactiveObserver
    {
        // No ReactiveSource for ""Health""
        [ReactiveBind(nameof(Health))]
        private void OnHealthChanged() { }

        private int Health; // Not marked with ReactiveSource
    }
}";

        var result = GeneratorTestHelper.RunGenerator(source);

        GeneratorTestHelper.AssertHasDiagnostic(result, "RB0002");
    }

    #endregion

    #region Class-level errors (RB1xxx)

    [Test]
    public void RB1001_ClassNotPartial_ProducesError()
    {
        var source = @"
namespace TestNamespace
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

        GeneratorTestHelper.AssertHasDiagnostic(result, "RB1001");
    }

    [Test]
    public void RB1002_ClassNotImplementInterface_ProducesError()
    {
        var source = @"
namespace TestNamespace
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

        GeneratorTestHelper.AssertHasDiagnostic(result, "RB1002");
    }

    [Test]
    public void RB1003_ThrottleInvalidValue_ProducesError()
    {
        var source = @"
namespace TestNamespace
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
    public void RB1004_ThrottleWithoutInterface_ProducesError()
    {
        var source = @"
namespace TestNamespace
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

        GeneratorTestHelper.AssertHasDiagnostic(result, "RB1004");
    }

    #endregion

    #region ReactiveSource errors (RB2xxx)

    [Test]
    public void RB2001_MethodReturnsVoid_ProducesError()
    {
        var source = @"
namespace TestNamespace
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

        GeneratorTestHelper.AssertHasDiagnostic(result, "RB2001");
    }

    [Test]
    public void RB2002_PropertyNoGetter_ProducesError()
    {
        var source = @"
namespace TestNamespace
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

        GeneratorTestHelper.AssertHasDiagnostic(result, "RB2002");
    }

    [Test]
    public void RB2003_MethodHasParameters_ProducesError()
    {
        var source = @"
namespace TestNamespace
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

        GeneratorTestHelper.AssertHasDiagnostic(result, "RB2003");
    }

    #endregion

    #region ReactiveBind errors (RB3xxx)

    [Test]
    public void RB3001_BindEmptyIds_ProducesError()
    {
        var source = @"
namespace TestNamespace
{
    public partial class TestClass : IReactiveObserver
    {
        [ReactiveSource]
        private int Health;

        [ReactiveBind()] // Empty ids
        private void OnChanged() { }
    }
}";

        var result = GeneratorTestHelper.RunGenerator(source);

        GeneratorTestHelper.AssertHasDiagnostic(result, "RB3001");
    }

    [Test]
    public void RB3002_MethodIsStatic_ProducesError()
    {
        var source = @"
namespace TestNamespace
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

        GeneratorTestHelper.AssertHasDiagnostic(result, "RB3002");
    }

    [Test]
    public void RB3003_MethodNotVoid_ProducesError()
    {
        var source = @"
namespace TestNamespace
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

        GeneratorTestHelper.AssertHasDiagnostic(result, "RB3003");
    }

    [Test]
    public void RB3004_InvalidParameterCount_ProducesError()
    {
        var source = @"
namespace TestNamespace
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

        GeneratorTestHelper.AssertHasDiagnostic(result, "RB3004");
    }

    [Test]
    public void RB3005_ParameterTypeMismatch_ProducesError()
    {
        var source = @"
namespace TestNamespace
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

        GeneratorTestHelper.AssertHasDiagnostic(result, "RB3005");
    }

    [Test]
    public void RB3006_DuplicateIds_ProducesError()
    {
        var source = @"
namespace TestNamespace
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

        GeneratorTestHelper.AssertHasDiagnostic(result, "RB3006");
    }

    [Test]
    public void RB3007_NotUsingNameof_ProducesError()
    {
        var source = @"
namespace TestNamespace
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

        GeneratorTestHelper.AssertHasDiagnostic(result, "RB3007");
    }

    #endregion

    #region Valid cases that should not produce errors

    [Test]
    public void ValidClass_NoErrors()
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
    }

    [Test]
    public void ValidMultiSource_NoErrors()
    {
        var source = @"
namespace TestNamespace
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
}
