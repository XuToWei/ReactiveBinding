using NUnit.Framework;

namespace ReactiveBinding.SourceGenerator.Tests;

/// <summary>
/// Tests for different data types support.
/// </summary>
[TestFixture]
public class DataTypeTests
{
    [Test]
    public void IntType_GeneratesCorrectCode()
    {
        var source = @"
namespace TestNamespace
{
    public partial class TestClass : IReactiveObserver
    {
        [ReactiveSource]
        private int Value;

        [ReactiveBind(nameof(Value))]
        private void OnValueChanged(int oldValue, int newValue) { }
    }
}";

        var result = GeneratorTestHelper.RunGenerator(source);

        GeneratorTestHelper.AssertNoErrors(result);
        GeneratorTestHelper.AssertGeneratedContains(result, "private int __reactive_Value");
    }

    [Test]
    public void FloatType_GeneratesCorrectCode()
    {
        var source = @"
namespace TestNamespace
{
    public partial class TestClass : IReactiveObserver
    {
        [ReactiveSource]
        private float Value;

        [ReactiveBind(nameof(Value))]
        private void OnValueChanged(float oldValue, float newValue) { }
    }
}";

        var result = GeneratorTestHelper.RunGenerator(source);

        GeneratorTestHelper.AssertNoErrors(result);
        GeneratorTestHelper.AssertGeneratedContains(result, "private float __reactive_Value");
    }

    [Test]
    public void DoubleType_GeneratesCorrectCode()
    {
        var source = @"
namespace TestNamespace
{
    public partial class TestClass : IReactiveObserver
    {
        [ReactiveSource]
        private double Value;

        [ReactiveBind(nameof(Value))]
        private void OnValueChanged(double oldValue, double newValue) { }
    }
}";

        var result = GeneratorTestHelper.RunGenerator(source);

        GeneratorTestHelper.AssertNoErrors(result);
        GeneratorTestHelper.AssertGeneratedContains(result, "private double __reactive_Value");
    }

    [Test]
    public void StringType_GeneratesCorrectCode()
    {
        var source = @"
namespace TestNamespace
{
    public partial class TestClass : IReactiveObserver
    {
        [ReactiveSource]
        private string Name;

        [ReactiveBind(nameof(Name))]
        private void OnNameChanged(string oldValue, string newValue) { }
    }
}";

        var result = GeneratorTestHelper.RunGenerator(source);

        GeneratorTestHelper.AssertNoErrors(result);
        GeneratorTestHelper.AssertGeneratedContains(result, "private string __reactive_Name");
    }

    [Test]
    public void BoolType_GeneratesCorrectCode()
    {
        var source = @"
namespace TestNamespace
{
    public partial class TestClass : IReactiveObserver
    {
        [ReactiveSource]
        private bool IsActive;

        [ReactiveBind(nameof(IsActive))]
        private void OnActiveChanged(bool oldValue, bool newValue) { }
    }
}";

        var result = GeneratorTestHelper.RunGenerator(source);

        GeneratorTestHelper.AssertNoErrors(result);
        GeneratorTestHelper.AssertGeneratedContains(result, "private bool __reactive_IsActive");
    }

    [Test]
    public void ReferenceType_GeneratesCorrectCode()
    {
        var source = @"
namespace TestNamespace
{
    public class PlayerData { public int Health; }

    public partial class TestClass : IReactiveObserver
    {
        [ReactiveSource]
        private PlayerData Data;

        [ReactiveBind(nameof(Data))]
        private void OnDataChanged(PlayerData oldValue, PlayerData newValue) { }
    }
}";

        var result = GeneratorTestHelper.RunGenerator(source);

        GeneratorTestHelper.AssertNoErrors(result);
        GeneratorTestHelper.AssertGeneratedContains(result, "private TestNamespace.PlayerData __reactive_Data");
    }

    [Test]
    public void NullableValueType_GeneratesCorrectCode()
    {
        var source = @"
namespace TestNamespace
{
    public partial class TestClass : IReactiveObserver
    {
        [ReactiveSource]
        private int? Value;

        [ReactiveBind(nameof(Value))]
        private void OnValueChanged(int? oldValue, int? newValue) { }
    }
}";

        var result = GeneratorTestHelper.RunGenerator(source);

        GeneratorTestHelper.AssertNoErrors(result);
        GeneratorTestHelper.AssertGeneratedContains(result, "private int? __reactive_Value");
    }

    [Test]
    public void EnumType_GeneratesCorrectCode()
    {
        var source = @"
namespace TestNamespace
{
    public enum GameState { Menu, Playing, Paused }

    public partial class TestClass : IReactiveObserver
    {
        [ReactiveSource]
        private GameState State;

        [ReactiveBind(nameof(State))]
        private void OnStateChanged(GameState oldValue, GameState newValue) { }
    }
}";

        var result = GeneratorTestHelper.RunGenerator(source);

        GeneratorTestHelper.AssertNoErrors(result);
        GeneratorTestHelper.AssertGeneratedContains(result, "TestNamespace.GameState __reactive_State");
    }

    [Test]
    public void GenericType_GeneratesCorrectCode()
    {
        var source = @"
using System.Collections.Generic;

namespace TestNamespace
{
    public partial class TestClass : IReactiveObserver
    {
        [ReactiveSource]
        private List<int> Items;

        [ReactiveBind(nameof(Items))]
        private void OnItemsChanged(List<int> oldValue, List<int> newValue) { }
    }
}";

        var result = GeneratorTestHelper.RunGenerator(source);

        GeneratorTestHelper.AssertNoErrors(result);
        GeneratorTestHelper.AssertGeneratedContains(result, "System.Collections.Generic.List<int> __reactive_Items");
    }

    [Test]
    public void MixedTypes_GeneratesCorrectCode()
    {
        var source = @"
namespace TestNamespace
{
    public partial class TestClass : IReactiveObserver
    {
        [ReactiveSource]
        private int Health;

        [ReactiveSource]
        private string Name;

        [ReactiveSource]
        private float Speed;

        [ReactiveBind(nameof(Health), nameof(Name), nameof(Speed))]
        private void OnStatsChanged(int h, string n, float s) { }
    }
}";

        var result = GeneratorTestHelper.RunGenerator(source);

        GeneratorTestHelper.AssertNoErrors(result);
        GeneratorTestHelper.AssertGeneratedContains(result, "private int __reactive_Health");
        GeneratorTestHelper.AssertGeneratedContains(result, "private string __reactive_Name");
        GeneratorTestHelper.AssertGeneratedContains(result, "private float __reactive_Speed");
    }
}
