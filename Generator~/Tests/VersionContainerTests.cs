using NUnit.Framework;

namespace ReactiveBinding.SourceGenerator.Tests;

/// <summary>
/// Tests for version container (VersionList, VersionDictionary, VersionHashSet) code generation.
/// </summary>
[TestFixture]
public class VersionContainerTests
{
    [Test]
    public void VersionList_WithNoParams_GeneratesVersionComparison()
    {
        var source = @"
namespace ReactiveBinding.Test
{
    public partial class TestClass : IReactiveObserver
    {
        [ReactiveSource]
        private VersionList<int> Items = new();

        [ReactiveBind(nameof(Items))]
        private void OnItemsChanged() { }
    }
}";

        var result = GeneratorTestHelper.RunGenerator(source);

        GeneratorTestHelper.AssertNoErrors(result);
        GeneratorTestHelper.AssertGeneratedContains(result, "partial class TestClass");
        GeneratorTestHelper.AssertGeneratedContains(result, "__reactive_Items_version");
        GeneratorTestHelper.AssertGeneratedContains(result, "Items?.Version ?? -1");
        GeneratorTestHelper.AssertGeneratedContains(result, "OnItemsChanged()");
    }

    [Test]
    public void VersionList_WithContainerParam_GeneratesCorrectCall()
    {
        var source = @"
namespace ReactiveBinding.Test
{
    public partial class TestClass : IReactiveObserver
    {
        [ReactiveSource]
        private VersionList<string> Items = new();

        [ReactiveBind(nameof(Items))]
        private void OnItemsChanged(VersionList<string> items) { }
    }
}";

        var result = GeneratorTestHelper.RunGenerator(source);

        GeneratorTestHelper.AssertNoErrors(result);
        GeneratorTestHelper.AssertGeneratedContains(result, "__reactive_Items_version");
        GeneratorTestHelper.AssertGeneratedContains(result, "OnItemsChanged(Items)");
    }

    [Test]
    public void VersionDictionary_WithNoParams_GeneratesVersionComparison()
    {
        var source = @"
namespace ReactiveBinding.Test
{
    public partial class TestClass : IReactiveObserver
    {
        [ReactiveSource]
        private VersionDictionary<string, int> Data = new();

        [ReactiveBind(nameof(Data))]
        private void OnDataChanged() { }
    }
}";

        var result = GeneratorTestHelper.RunGenerator(source);

        GeneratorTestHelper.AssertNoErrors(result);
        GeneratorTestHelper.AssertGeneratedContains(result, "__reactive_Data_version");
        GeneratorTestHelper.AssertGeneratedContains(result, "Data?.Version ?? -1");
    }

    [Test]
    public void VersionHashSet_WithContainerParam_GeneratesCorrectCall()
    {
        var source = @"
namespace ReactiveBinding.Test
{
    public partial class TestClass : IReactiveObserver
    {
        [ReactiveSource]
        private VersionHashSet<int> UniqueIds = new();

        [ReactiveBind(nameof(UniqueIds))]
        private void OnUniqueIdsChanged(VersionHashSet<int> ids) { }
    }
}";

        var result = GeneratorTestHelper.RunGenerator(source);

        GeneratorTestHelper.AssertNoErrors(result);
        GeneratorTestHelper.AssertGeneratedContains(result, "__reactive_UniqueIds_version");
        GeneratorTestHelper.AssertGeneratedContains(result, "OnUniqueIdsChanged(UniqueIds)");
    }

    [Test]
    public void VersionContainer_MultipleBinds_GeneratesAllCallbacks()
    {
        var source = @"
namespace ReactiveBinding.Test
{
    public partial class TestClass : IReactiveObserver
    {
        [ReactiveSource]
        private VersionList<int> Items = new();

        [ReactiveBind(nameof(Items))]
        private void OnItemsChanged1() { }

        [ReactiveBind(nameof(Items))]
        private void OnItemsChanged2(VersionList<int> items) { }
    }
}";

        var result = GeneratorTestHelper.RunGenerator(source);

        GeneratorTestHelper.AssertNoErrors(result);
        GeneratorTestHelper.AssertGeneratedContains(result, "OnItemsChanged1()");
        GeneratorTestHelper.AssertGeneratedContains(result, "OnItemsChanged2(Items)");
    }

    [Test]
    public void VersionContainer_WithOldNewParams_ProducesError()
    {
        var source = @"
namespace ReactiveBinding.Test
{
    public partial class TestClass : IReactiveObserver
    {
        [ReactiveSource]
        private VersionList<int> Items = new();

        [ReactiveBind(nameof(Items))]
        private void OnItemsChanged(VersionList<int> oldItems, VersionList<int> newItems) { }
    }
}";

        var result = GeneratorTestHelper.RunGenerator(source);

        GeneratorTestHelper.AssertHasDiagnostic(result, "RB3004");
    }

    [Test]
    public void MixedVersionAndNonVersion_WithNoParams_Works()
    {
        var source = @"
namespace ReactiveBinding.Test
{
    public partial class TestClass : IReactiveObserver
    {
        [ReactiveSource]
        private VersionList<int> Items = new();

        [ReactiveSource]
        private int Count;

        [ReactiveBind(nameof(Items), nameof(Count))]
        private void OnDataChanged() { }
    }
}";

        var result = GeneratorTestHelper.RunGenerator(source);

        GeneratorTestHelper.AssertNoErrors(result);
        GeneratorTestHelper.AssertGeneratedContains(result, "__reactive_Items_version");
        GeneratorTestHelper.AssertGeneratedContains(result, "__reactive_Count");
        GeneratorTestHelper.AssertGeneratedContains(result, "OnDataChanged()");
    }

    [Test]
    public void MixedVersionAndNonVersion_WithParams_Works()
    {
        var source = @"
namespace ReactiveBinding.Test
{
    public partial class TestClass : IReactiveObserver
    {
        [ReactiveSource]
        private VersionList<int> Items = new();

        [ReactiveSource]
        private int Count;

        [ReactiveBind(nameof(Items), nameof(Count))]
        private void OnDataChanged(VersionList<int> items, int count) { }
    }
}";

        var result = GeneratorTestHelper.RunGenerator(source);

        GeneratorTestHelper.AssertNoErrors(result);
        GeneratorTestHelper.AssertGeneratedContains(result, "__reactive_Items_version");
        GeneratorTestHelper.AssertGeneratedContains(result, "__reactive_Count");
        GeneratorTestHelper.AssertGeneratedContains(result, "OnDataChanged(Items, __reactive_Count)");
    }

    [Test]
    public void MixedVersionAndNonVersion_With2NParams_ProducesError()
    {
        var source = @"
namespace ReactiveBinding.Test
{
    public partial class TestClass : IReactiveObserver
    {
        [ReactiveSource]
        private VersionList<int> Items = new();

        [ReactiveSource]
        private int Count;

        [ReactiveBind(nameof(Items), nameof(Count))]
        private void OnDataChanged(VersionList<int> oldItems, VersionList<int> newItems, int oldCount, int newCount) { }
    }
}";

        var result = GeneratorTestHelper.RunGenerator(source);

        GeneratorTestHelper.AssertHasDiagnostic(result, "RB3004");
    }

    [Test]
    public void MultipleVersionContainers_GeneratesCorrectCode()
    {
        var source = @"
namespace ReactiveBinding.Test
{
    public partial class TestClass : IReactiveObserver
    {
        [ReactiveSource]
        private VersionList<int> Items = new();

        [ReactiveSource]
        private VersionDictionary<string, int> Data = new();

        [ReactiveBind(nameof(Items), nameof(Data))]
        private void OnBothChanged(VersionList<int> items, VersionDictionary<string, int> data) { }
    }
}";

        var result = GeneratorTestHelper.RunGenerator(source);

        GeneratorTestHelper.AssertNoErrors(result);
        GeneratorTestHelper.AssertGeneratedContains(result, "__reactive_Items_version");
        GeneratorTestHelper.AssertGeneratedContains(result, "__reactive_Data_version");
        GeneratorTestHelper.AssertGeneratedContains(result, "OnBothChanged(Items, Data)");
    }

    [Test]
    public void VersionContainer_AsProperty_GeneratesVersionComparison()
    {
        var source = @"
namespace ReactiveBinding.Test
{
    public partial class TestClass : IReactiveObserver
    {
        private VersionList<int> _items = new();

        [ReactiveSource]
        private VersionList<int> Items => _items;

        [ReactiveBind(nameof(Items))]
        private void OnItemsChanged(VersionList<int> items) { }
    }
}";

        var result = GeneratorTestHelper.RunGenerator(source);

        GeneratorTestHelper.AssertNoErrors(result);
        GeneratorTestHelper.AssertGeneratedContains(result, "__reactive_Items_version");
        GeneratorTestHelper.AssertGeneratedContains(result, "Items?.Version ?? -1");
    }

    [Test]
    public void VersionContainer_InitializedWithNull_HandlesNullSafely()
    {
        var source = @"
namespace ReactiveBinding.Test
{
    public partial class TestClass : IReactiveObserver
    {
        [ReactiveSource]
        private VersionList<int>? Items;

        [ReactiveBind(nameof(Items))]
        private void OnItemsChanged() { }
    }
}";

        var result = GeneratorTestHelper.RunGenerator(source);

        GeneratorTestHelper.AssertNoErrors(result);
        // Null-safe version access
        GeneratorTestHelper.AssertGeneratedContains(result, "Items?.Version ?? -1");
    }
}
