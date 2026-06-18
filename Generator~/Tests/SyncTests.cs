using System.Text;
using NUnit.Framework;

namespace ReactiveBinding.SourceGenerator.Tests;

[TestFixture]
public class SyncTests
{
    // ---------- Generated shape ----------

    [Test]
    public void SyncField_GeneratesSyncableInterface()
    {
        var source = @"
namespace Test
{
    public partial class PlayerData : IVersion
    {
        [VersionField][VersionSync] private int m_Health;
    }
}";
        var result = GeneratorTestHelper.RunVersionFieldGenerator(source);

        GeneratorTestHelper.AssertNoErrors(result);
        GeneratorTestHelper.AssertGeneratedContains(result, ": ReactiveBinding.IVersionSyncable");
        GeneratorTestHelper.AssertGeneratedContains(result, "public byte[] GetFull()");
        GeneratorTestHelper.AssertGeneratedContains(result, "public byte[] GetDelta()");
        GeneratorTestHelper.AssertGeneratedContains(result, "__syncDirty |= (1UL << 0);");
    }

    [Test]
    public void NonSyncField_DoesNotGenerateSyncable()
    {
        var source = @"
namespace Test
{
    public partial class PlayerData : IVersion
    {
        [VersionField] private int m_Health;
    }
}";
        var result = GeneratorTestHelper.RunVersionFieldGenerator(source);

        GeneratorTestHelper.AssertNoErrors(result);
        Assert.That(result.GeneratedSources.Any(s => s.Contains("IVersionSyncable")), Is.False);
        Assert.That(result.GeneratedSources.Any(s => s.Contains("__syncDirty")), Is.False);
    }

    // ---------- Round-trip (execution) ----------

    private const string ScalarModel = @"
namespace Test
{
    public partial class PlayerData : IVersion
    {
        [VersionField][VersionSync] private int m_Health;
        [VersionField][VersionSync] private string m_Name;
        [VersionField]              private int m_TempCache;
    }
}";

    [Test]
    public void FullRoundTrip()
    {
        var r = GeneratorTestHelper.CompileAndRun(ScalarModel);
        dynamic a = r.Create("Test.PlayerData");
        a.Health = 100;
        a.Name = "abc";
        a.Commit();
        byte[] full = a.GetFull();

        dynamic b = r.Create("Test.PlayerData");
        b.Apply(full);

        Assert.That((int)b.Health, Is.EqualTo(100));
        Assert.That((string)b.Name, Is.EqualTo("abc"));
    }

    [Test]
    public void Delta_OnlyChangedFieldApplied()
    {
        var r = GeneratorTestHelper.CompileAndRun(ScalarModel);
        dynamic a = r.Create("Test.PlayerData");
        a.Health = 100;
        a.Name = "abc";
        a.Commit();
        byte[] full = a.GetFull();

        dynamic b = r.Create("Test.PlayerData");
        b.Apply(full);

        a.Health = 150;                 // change one field only
        byte[] delta = a.GetDelta();
        b.ApplyDelta(delta);

        Assert.That((int)b.Health, Is.EqualTo(150));
        Assert.That((string)b.Name, Is.EqualTo("abc")); // untouched
    }

    [Test]
    public void Delta_MergesToLatestValue()
    {
        var r = GeneratorTestHelper.CompileAndRun(ScalarModel);
        dynamic a = r.Create("Test.PlayerData");
        a.Commit();
        byte[] full = a.GetFull();
        dynamic b = r.Create("Test.PlayerData");
        b.Apply(full);

        a.Health = 1;
        a.Health = 2;
        a.Health = 3;                   // merged: only latest survives
        b.ApplyDelta(a.GetDelta());

        Assert.That((int)b.Health, Is.EqualTo(3));
    }

    [Test]
    public void GetDelta_IsNonDestructive_CommitClears()
    {
        var r = GeneratorTestHelper.CompileAndRun(ScalarModel);
        dynamic a = r.Create("Test.PlayerData");
        a.Health = 5;
        a.Commit();

        a.Health = 9;
        byte[] d1 = a.GetDelta();
        byte[] d2 = a.GetDelta();
        Assert.That(d2, Is.EqualTo(d1));    // repeatable, not drained

        a.Commit();
        byte[] d3 = a.GetDelta();
        Assert.That(d3.Length, Is.EqualTo(8)); // just the empty mask (ulong)
    }

    // ---------- Nested sync object ----------

    private const string NestedModel = @"
namespace Test
{
    public partial class ChildData : IVersion
    {
        [VersionField][VersionSync] private int m_Hp;
    }
    public partial class RootData : IVersion
    {
        [VersionField][VersionSync] private ChildData m_Child;
        [VersionField][VersionSync] private int m_Gold;
    }
}";

    [Test]
    public void Nested_FullRoundTrip()
    {
        var r = GeneratorTestHelper.CompileAndRun(NestedModel);
        dynamic a = r.Create("Test.RootData");
        a.Child = r.Create("Test.ChildData");
        a.Child.Hp = 10;
        a.Gold = 5;
        a.Commit();
        byte[] full = a.GetFull();

        dynamic b = r.Create("Test.RootData");
        b.Apply(full);

        Assert.That((int)b.Child.Hp, Is.EqualTo(10));
        Assert.That((int)b.Gold, Is.EqualTo(5));
    }

    [Test]
    public void Nested_InternalChange_Patches()
    {
        var r = GeneratorTestHelper.CompileAndRun(NestedModel);
        dynamic a = r.Create("Test.RootData");
        a.Child = r.Create("Test.ChildData");
        a.Child.Hp = 10;
        a.Gold = 5;
        a.Commit();
        byte[] full = a.GetFull();
        dynamic b = r.Create("Test.RootData");
        b.Apply(full);

        a.Child.Hp = 99;                 // internal change only -> Patch
        b.ApplyDelta(a.GetDelta());

        Assert.That((int)b.Child.Hp, Is.EqualTo(99));
        Assert.That((int)b.Gold, Is.EqualTo(5));
    }

    [Test]
    public void Nested_Reassign_Replaces()
    {
        var r = GeneratorTestHelper.CompileAndRun(NestedModel);
        dynamic a = r.Create("Test.RootData");
        a.Child = r.Create("Test.ChildData");
        a.Child.Hp = 10;
        a.Commit();
        byte[] full = a.GetFull();
        dynamic b = r.Create("Test.RootData");
        b.Apply(full);

        dynamic nc = r.Create("Test.ChildData");
        nc.Hp = 77;
        a.Child = nc;                    // whole-object reassign -> Replace
        b.ApplyDelta(a.GetDelta());

        Assert.That((int)b.Child.Hp, Is.EqualTo(77));
    }

    // ---------- Diagnostics ----------

    [Test]
    public void UnsupportedSyncType_ReportsVS2001()
    {
        var source = @"
namespace Test
{
    public class Plain { }
    public partial class PlayerData : IVersion
    {
        [VersionField][VersionSync] private Plain m_X;
    }
}";
        var result = GeneratorTestHelper.RunVersionFieldGenerator(source);
        GeneratorTestHelper.AssertHasDiagnostic(result, "VS2001");
    }

    [Test]
    public void TooManySyncFields_ReportsVS1001()
    {
        var sb = new StringBuilder();
        sb.AppendLine("namespace Test {");
        sb.AppendLine("public partial class Big : IVersion {");
        for (int i = 0; i < 65; i++)
            sb.AppendLine($"[VersionField][VersionSync] private int m_F{i};");
        sb.AppendLine("} }");

        var result = GeneratorTestHelper.RunVersionFieldGenerator(sb.ToString());
        GeneratorTestHelper.AssertHasDiagnostic(result, "VS1001");
    }

    // ---------- Containers (Phase 2) ----------

    private const string ListScalarModel = @"
namespace Test
{
    public partial class Inv : IVersion
    {
        [VersionField][VersionSync] private VersionList<int> m_Nums;
    }
}";

    [Test]
    public void ListScalar_FullAndDelta()
    {
        var r = GeneratorTestHelper.CompileAndRun(ListScalarModel);
        dynamic a = r.Create("Test.Inv");
        a.Nums = new ReactiveBinding.VersionList<int>();
        a.Nums.Add(10);
        a.Nums.Add(20);
        a.Commit();
        byte[] full = a.GetFull();

        dynamic b = r.Create("Test.Inv");
        b.Apply(full);
        Assert.That((int)b.Nums.Count, Is.EqualTo(2));
        Assert.That((int)b.Nums[0], Is.EqualTo(10));
        Assert.That((int)b.Nums[1], Is.EqualTo(20));

        a.Nums.Add(30);
        a.Nums.RemoveAt(0);            // [20, 30]
        b.ApplyDelta(a.GetDelta());

        Assert.That((int)b.Nums.Count, Is.EqualTo(2));
        Assert.That((int)b.Nums[0], Is.EqualTo(20));
        Assert.That((int)b.Nums[1], Is.EqualTo(30));
    }

    private const string ListObjModel = @"
namespace Test
{
    public partial class Item : IVersion
    {
        [VersionField][VersionSync] private int m_Qty;
        [VersionField][VersionSync] private string m_Label;
    }
    public partial class Bag : IVersion
    {
        [VersionField][VersionSync] private VersionList<Item> m_Items;
    }
}";

    [Test]
    public void ListObject_ElementInternalChange_FieldLevelPatch()
    {
        var r = GeneratorTestHelper.CompileAndRun(ListObjModel);
        dynamic a = r.Create("Test.Bag");
        // VersionList<Test.Item> is only known at runtime; build it from the property's type.
        dynamic list = System.Activator.CreateInstance(r.Assembly.GetType("Test.Bag")!.GetProperty("Items")!.PropertyType);
        a.Items = list;
        dynamic it = r.Create("Test.Item");
        it.Qty = 1;
        string bigLabel = new string('x', 500);
        it.Label = bigLabel;           // large, unchanged field
        a.Items.Add(it);
        a.Commit();
        byte[] full = a.GetFull();

        dynamic b = r.Create("Test.Bag");
        b.Apply(full);
        Assert.That((int)b.Items[0].Qty, Is.EqualTo(1));
        Assert.That((string)b.Items[0].Label, Is.EqualTo(bigLabel));

        a.Items[0].Qty = 5;            // change ONE field of the element
        byte[] delta = a.GetDelta();
        b.ApplyDelta(delta);

        Assert.That((int)b.Items[0].Qty, Is.EqualTo(5));
        Assert.That((string)b.Items[0].Label, Is.EqualTo(bigLabel)); // preserved
        // Field-level patch: the large unchanged label is NOT resent in the delta.
        Assert.That(delta.Length, Is.LessThan(100));
        Assert.That(full.Length, Is.GreaterThan(500));
    }

    private const string DictObjModel = @"
namespace Test
{
    public partial class Cell : IVersion
    {
        [VersionField][VersionSync] private int m_A;
        [VersionField][VersionSync] private int m_B;
    }
    public partial class Grid : IVersion
    {
        [VersionField][VersionSync] private VersionDictionary<string, Cell> m_Cells;
    }
}";

    [Test]
    public void DictObject_ValueInternalChange_FieldLevelPatch()
    {
        var r = GeneratorTestHelper.CompileAndRun(DictObjModel);
        dynamic a = r.Create("Test.Grid");
        dynamic dict = System.Activator.CreateInstance(r.Assembly.GetType("Test.Grid")!.GetProperty("Cells")!.PropertyType);
        a.Cells = dict;
        dynamic cell = r.Create("Test.Cell");
        cell.A = 1;
        cell.B = 2;
        a.Cells["k"] = cell;
        a.Commit();
        byte[] full = a.GetFull();

        dynamic b = r.Create("Test.Grid");
        b.Apply(full);

        a.Cells["k"].A = 9;            // change one field of the value
        byte[] delta = a.GetDelta();
        b.ApplyDelta(delta);

        Assert.That((int)b.Cells["k"].A, Is.EqualTo(9));
        Assert.That((int)b.Cells["k"].B, Is.EqualTo(2)); // preserved
    }

    private const string DictModel = @"
namespace Test
{
    public partial class Reg : IVersion
    {
        [VersionField][VersionSync] private VersionDictionary<string, int> m_Map;
    }
}";

    [Test]
    public void Dictionary_FullAndDelta()
    {
        var r = GeneratorTestHelper.CompileAndRun(DictModel);
        dynamic a = r.Create("Test.Reg");
        a.Map = new ReactiveBinding.VersionDictionary<string, int>();
        a.Map["a"] = 1;
        a.Map["b"] = 2;
        a.Commit();
        byte[] full = a.GetFull();

        dynamic b = r.Create("Test.Reg");
        b.Apply(full);
        Assert.That((int)b.Map["a"], Is.EqualTo(1));
        Assert.That((int)b.Map.Count, Is.EqualTo(2));

        a.Map["a"] = 10;
        a.Map.Remove("b");
        a.Map["c"] = 3;
        b.ApplyDelta(a.GetDelta());

        Assert.That((int)b.Map["a"], Is.EqualTo(10));
        Assert.That((bool)b.Map.ContainsKey("b"), Is.False);
        Assert.That((int)b.Map["c"], Is.EqualTo(3));
    }

    private const string SetModel = @"
namespace Test
{
    public partial class Tags : IVersion
    {
        [VersionField][VersionSync] private VersionHashSet<int> m_Items;
    }
}";

    [Test]
    public void HashSet_FullAndDelta()
    {
        var r = GeneratorTestHelper.CompileAndRun(SetModel);
        dynamic a = r.Create("Test.Tags");
        a.Items = new ReactiveBinding.VersionHashSet<int>();
        a.Items.Add(1);
        a.Items.Add(2);
        a.Commit();
        byte[] full = a.GetFull();

        dynamic b = r.Create("Test.Tags");
        b.Apply(full);
        Assert.That((int)b.Items.Count, Is.EqualTo(2));
        Assert.That((bool)b.Items.Contains(1), Is.True);

        a.Items.Add(3);
        a.Items.Remove(1);
        b.ApplyDelta(a.GetDelta());

        Assert.That((int)b.Items.Count, Is.EqualTo(2));
        Assert.That((bool)b.Items.Contains(1), Is.False);
        Assert.That((bool)b.Items.Contains(2), Is.True);
        Assert.That((bool)b.Items.Contains(3), Is.True);
    }

    [Test]
    public async Task SyncWithoutVersionField_ReportsVS2002()
    {
        var source = @"
namespace Test
{
    public partial class PlayerData : IVersion
    {
        [VersionSync] private int m_Health;
    }
}";
        var diagnostics = await GeneratorTestHelper.RunVersionSyncFieldAnalyzer(source);
        Assert.That(diagnostics.Any(d => d.Id == "VS2002"), Is.True);
    }
}
