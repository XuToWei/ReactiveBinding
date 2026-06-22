using System.IO;
using NUnit.Framework;
using ReactiveBinding;

namespace ReactiveBinding.SourceGenerator.Tests;

[TestFixture]
public class SyncTests
{
    private const string ScalarModel = @"
namespace Test
{
    public partial class PlayerData : IVersionSync
    {
        [VersionField] private int m_Health;
        [VersionField] private string m_Name;
    }
}";

    private const string NestedModel = @"
namespace Test
{
    public partial class Bag : IVersionSync
    {
        [VersionField] private int m_Gold;
    }
    public partial class Player : IVersionSync
    {
        [VersionField] private int m_Health;
        [VersionField] private Bag m_Bag;
    }
}";

    // ---------- Sync helpers (flat-registry / SyncContext) ----------

    /// <summary>Creates a context and seeds it with the root (registers root + subtree).</summary>
    private static SyncContext Attach(dynamic root)
    {
        var ctx = new SyncContext();
        ((IVersionSync)root).AttachTo(ctx);
        return ctx;
    }

    // Both wrap the single Commit(): the first commit after attach is the full state, later commits are deltas.
    private static System.IO.MemoryStream Full(SyncContext ctx) => ctx.Commit();

    private static System.IO.MemoryStream Delta(SyncContext ctx) => ctx.Commit();

    private static void Apply(SyncContext ctx, System.IO.MemoryStream s) => ctx.Apply(new System.IO.BinaryReader(s));

    // ---------- Generated shape ----------

    [Test]
    public void SyncField_GeneratesFlatRegistryApi()
    {
        var result = GeneratorTestHelper.RunVersionFieldGenerator(ScalarModel);
        GeneratorTestHelper.AssertNoErrors(result);
        GeneratorTestHelper.AssertGeneratedContains(result, "public int __SyncId");
        GeneratorTestHelper.AssertGeneratedContains(result, "public ReactiveBinding.SyncContext __SyncContext");
        GeneratorTestHelper.AssertGeneratedContains(result, "public void __Commit()");
        GeneratorTestHelper.AssertGeneratedContains(result, "public void __Apply(System.IO.BinaryReader");
        GeneratorTestHelper.AssertGeneratedContains(result, "public void __SyncChildren(ReactiveBinding.SyncOp");
        GeneratorTestHelper.AssertGeneratedContains(result, "public void AttachTo(ReactiveBinding.SyncContext");
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
        Assert.That(result.GeneratedSources.Any(s => s.Contains("IVersionSync")), Is.False);
    }

    // ---------- Scalars ----------

    [Test]
    public void Scalar_Full_RoundTrip()
    {
        var r = GeneratorTestHelper.CompileAndRun(ScalarModel);
        dynamic a = r.Create("Test.PlayerData");
        var actx = Attach(a);
        a.Health = 100;
        a.Name = "abc";

        dynamic b = r.Create("Test.PlayerData");
        var bctx = Attach(b);
        Apply(bctx, Full(actx));

        Assert.That((int)b.Health, Is.EqualTo(100));
        Assert.That((string)b.Name, Is.EqualTo("abc"));
    }

    [Test]
    public void Scalar_Delta_OnlyChanged()
    {
        var r = GeneratorTestHelper.CompileAndRun(ScalarModel);
        dynamic a = r.Create("Test.PlayerData");
        var actx = Attach(a);
        a.Health = 100;
        a.Name = "abc";

        dynamic b = r.Create("Test.PlayerData");
        var bctx = Attach(b);
        Apply(bctx, Full(actx));   // full clears a's dirty

        a.Health = 150;            // only Health dirty
        Apply(bctx, Delta(actx));

        Assert.That((int)b.Health, Is.EqualTo(150));
        Assert.That((string)b.Name, Is.EqualTo("abc"));
    }

    [Test]
    public void Scalar_Delta_EmptyWhenNoChange()
    {
        var r = GeneratorTestHelper.CompileAndRun(ScalarModel);
        dynamic a = r.Create("Test.PlayerData");
        var actx = Attach(a);
        a.Health = 1;
        Full(actx);                // advances the cursor past the full state
        var delta = Delta(actx);   // nothing changed -> no records written
        Assert.That(delta.Length - delta.Position, Is.EqualTo(0));   // no unread records since the last commit
    }

    // ---------- Nested SyncObject ----------

    [Test]
    public void Nested_Full_RoundTrip()
    {
        var r = GeneratorTestHelper.CompileAndRun(NestedModel);
        dynamic a = r.Create("Test.Player");
        var actx = Attach(a);
        a.Health = 100;
        a.Bag = r.Create("Test.Bag");
        a.Bag.Gold = 50;

        dynamic b = r.Create("Test.Player");
        var bctx = Attach(b);
        Apply(bctx, Full(actx));

        Assert.That((int)b.Health, Is.EqualTo(100));
        Assert.That((int)b.Bag.Gold, Is.EqualTo(50));
    }

    [Test]
    public void Nested_Delta_InnerField()
    {
        var r = GeneratorTestHelper.CompileAndRun(NestedModel);
        dynamic a = r.Create("Test.Player");
        var actx = Attach(a);
        a.Health = 1;
        a.Bag = r.Create("Test.Bag");
        a.Bag.Gold = 10;

        dynamic b = r.Create("Test.Player");
        var bctx = Attach(b);
        Apply(bctx, Full(actx));

        a.Bag.Gold = 99;           // nested object reports itself by id (no tree walk)
        Apply(bctx, Delta(actx));

        Assert.That((int)b.Bag.Gold, Is.EqualTo(99));
        Assert.That((int)b.Health, Is.EqualTo(1));
    }

    [Test]
    public void Nested_Delta_Reassign()
    {
        var r = GeneratorTestHelper.CompileAndRun(NestedModel);
        dynamic a = r.Create("Test.Player");
        var actx = Attach(a);
        a.Bag = r.Create("Test.Bag");
        a.Bag.Gold = 1;

        dynamic b = r.Create("Test.Player");
        var bctx = Attach(b);
        Apply(bctx, Full(actx));

        dynamic nb = r.Create("Test.Bag");
        nb.Gold = 7;
        a.Bag = nb;                // reassign: old bag removed, new bag created
        Apply(bctx, Delta(actx));

        Assert.That((int)b.Bag.Gold, Is.EqualTo(7));
    }

    [Test]
    public void Nested_Delta_SetNull()
    {
        var r = GeneratorTestHelper.CompileAndRun(NestedModel);
        dynamic a = r.Create("Test.Player");
        var actx = Attach(a);
        a.Bag = r.Create("Test.Bag");
        a.Bag.Gold = 5;

        dynamic b = r.Create("Test.Player");
        var bctx = Attach(b);
        Apply(bctx, Full(actx));

        a.Bag = null;
        Apply(bctx, Delta(actx));

        Assert.That((object)b.Bag, Is.Null);
    }

    // ---------- VersionList<scalar> ----------

    private const string ListModel = @"
namespace Test
{
    public partial class Inv : IVersionSync
    {
        [VersionField] private VersionList<int> m_Nums;
    }
}";

    [Test]
    public void ListScalar_Full_RoundTrip()
    {
        var r = GeneratorTestHelper.CompileAndRun(ListModel);
        dynamic a = r.Create("Test.Inv");
        var actx = Attach(a);
        a.Nums = new ReactiveBinding.VersionList<int>();
        a.Nums.Add(10);
        a.Nums.Add(20);

        dynamic b = r.Create("Test.Inv");
        var bctx = Attach(b);
        Apply(bctx, Full(actx));

        Assert.That((int)b.Nums.Count, Is.EqualTo(2));
        Assert.That((int)b.Nums[0], Is.EqualTo(10));
        Assert.That((int)b.Nums[1], Is.EqualTo(20));
    }

    [Test]
    public void ListScalar_Delta_InsertRemove()
    {
        var r = GeneratorTestHelper.CompileAndRun(ListModel);
        dynamic a = r.Create("Test.Inv");
        var actx = Attach(a);
        a.Nums = new ReactiveBinding.VersionList<int>();
        a.Nums.Add(10);
        a.Nums.Add(20);

        dynamic b = r.Create("Test.Inv");
        var bctx = Attach(b);
        Apply(bctx, Full(actx));

        a.Nums.Add(30);
        a.Nums.RemoveAt(0);
        Apply(bctx, Delta(actx));

        Assert.That((int)b.Nums.Count, Is.EqualTo(2));
        Assert.That((int)b.Nums[0], Is.EqualTo(20));
        Assert.That((int)b.Nums[1], Is.EqualTo(30));
    }

    // ---------- VersionList<IVersionSync> (object elements, by id) ----------

    private const string ObjListModel = @"
namespace Test
{
    public partial class Item : IVersionSync
    {
        [VersionField] private int m_Qty;
    }
    public partial class Bag : IVersionSync
    {
        [VersionField] private VersionList<Item> m_Items;
    }
}";

    [Test]
    public void ListObject_Full_RoundTrip()
    {
        var r = GeneratorTestHelper.CompileAndRun(ObjListModel);
        dynamic a = r.Create("Test.Bag");
        var actx = Attach(a);
        dynamic list = System.Activator.CreateInstance(r.Assembly.GetType("Test.Bag")!.GetProperty("Items")!.PropertyType); a.Items = list;
        dynamic it = r.Create("Test.Item");
        it.Qty = 5;
        a.Items.Add(it);

        dynamic b = r.Create("Test.Bag");
        var bctx = Attach(b);
        Apply(bctx, Full(actx));

        Assert.That((int)b.Items.Count, Is.EqualTo(1));
        Assert.That((int)b.Items[0].Qty, Is.EqualTo(5));
    }

    [Test]
    public void ListObject_Delta_ElementField()
    {
        var r = GeneratorTestHelper.CompileAndRun(ObjListModel);
        dynamic a = r.Create("Test.Bag");
        var actx = Attach(a);
        dynamic list = System.Activator.CreateInstance(r.Assembly.GetType("Test.Bag")!.GetProperty("Items")!.PropertyType); a.Items = list;
        dynamic it = r.Create("Test.Item");
        it.Qty = 5;
        a.Items.Add(it);

        dynamic b = r.Create("Test.Bag");
        var bctx = Attach(b);
        Apply(bctx, Full(actx));

        a.Items[0].Qty = 9;        // element reports itself by id, independent of the list/parent
        Apply(bctx, Delta(actx));

        Assert.That((int)b.Items[0].Qty, Is.EqualTo(9));
    }

    [Test]
    public void ListObject_Delta_AddElement()
    {
        var r = GeneratorTestHelper.CompileAndRun(ObjListModel);
        dynamic a = r.Create("Test.Bag");
        var actx = Attach(a);
        dynamic list = System.Activator.CreateInstance(r.Assembly.GetType("Test.Bag")!.GetProperty("Items")!.PropertyType); a.Items = list;
        dynamic it = r.Create("Test.Item");
        it.Qty = 5;
        a.Items.Add(it);

        dynamic b = r.Create("Test.Bag");
        var bctx = Attach(b);
        Apply(bctx, Full(actx));

        dynamic it2 = r.Create("Test.Item");
        it2.Qty = 7;
        a.Items.Add(it2);          // structural add of a brand-new element
        Apply(bctx, Delta(actx));

        Assert.That((int)b.Items.Count, Is.EqualTo(2));
        Assert.That((int)b.Items[1].Qty, Is.EqualTo(7));
    }

    // ---------- VersionDictionary<scalar,scalar> ----------

    private const string DictModel = @"
namespace Test
{
    public partial class Reg : IVersionSync
    {
        [VersionField] private VersionDictionary<string, int> m_Map;
    }
}";

    [Test]
    public void Dict_Full_RoundTrip()
    {
        var r = GeneratorTestHelper.CompileAndRun(DictModel);
        dynamic a = r.Create("Test.Reg");
        var actx = Attach(a);
        a.Map = new ReactiveBinding.VersionDictionary<string, int>();
        a.Map["a"] = 1;
        a.Map["b"] = 2;

        dynamic b = r.Create("Test.Reg");
        var bctx = Attach(b);
        Apply(bctx, Full(actx));

        Assert.That((int)b.Map.Count, Is.EqualTo(2));
        Assert.That((int)b.Map["a"], Is.EqualTo(1));
        Assert.That((int)b.Map["b"], Is.EqualTo(2));
    }

    [Test]
    public void Dict_Delta_SetRemove()
    {
        var r = GeneratorTestHelper.CompileAndRun(DictModel);
        dynamic a = r.Create("Test.Reg");
        var actx = Attach(a);
        a.Map = new ReactiveBinding.VersionDictionary<string, int>();
        a.Map["a"] = 1;
        a.Map["b"] = 2;

        dynamic b = r.Create("Test.Reg");
        var bctx = Attach(b);
        Apply(bctx, Full(actx));

        a.Map["c"] = 3;
        a.Map.Remove("a");
        Apply(bctx, Delta(actx));

        Assert.That((int)b.Map.Count, Is.EqualTo(2));
        Assert.That((bool)b.Map.ContainsKey("a"), Is.False);
        Assert.That((int)b.Map["c"], Is.EqualTo(3));
    }

    // ---------- VersionHashSet<scalar> ----------

    private const string SetModel = @"
namespace Test
{
    public partial class Tg : IVersionSync
    {
        [VersionField] private VersionHashSet<string> m_Tags;
    }
}";

    [Test]
    public void HashSet_Full_RoundTrip()
    {
        var r = GeneratorTestHelper.CompileAndRun(SetModel);
        dynamic a = r.Create("Test.Tg");
        var actx = Attach(a);
        a.Tags = new ReactiveBinding.VersionHashSet<string>();
        a.Tags.Add("x");
        a.Tags.Add("y");

        dynamic b = r.Create("Test.Tg");
        var bctx = Attach(b);
        Apply(bctx, Full(actx));

        Assert.That((int)b.Tags.Count, Is.EqualTo(2));
        Assert.That((bool)b.Tags.Contains("x"), Is.True);
        Assert.That((bool)b.Tags.Contains("y"), Is.True);
    }

    [Test]
    public void HashSet_Delta_AddRemove()
    {
        var r = GeneratorTestHelper.CompileAndRun(SetModel);
        dynamic a = r.Create("Test.Tg");
        var actx = Attach(a);
        a.Tags = new ReactiveBinding.VersionHashSet<string>();
        a.Tags.Add("x");
        a.Tags.Add("y");

        dynamic b = r.Create("Test.Tg");
        var bctx = Attach(b);
        Apply(bctx, Full(actx));

        a.Tags.Add("z");
        a.Tags.Remove("x");
        Apply(bctx, Delta(actx));

        Assert.That((bool)b.Tags.Contains("x"), Is.False);
        Assert.That((bool)b.Tags.Contains("y"), Is.True);
        Assert.That((bool)b.Tags.Contains("z"), Is.True);
    }

    // ---------- Diagnostics ----------

    [Test]
    public void DictObjectValue_NotSupported_ReportsVS2001()
    {
        var source = @"
namespace Test
{
    public partial class V : IVersionSync
    {
        [VersionField] private int m_X;
    }
    public partial class Reg : IVersionSync
    {
        [VersionField] private VersionDictionary<string, V> m_Map;
    }
}";
        var result = GeneratorTestHelper.RunVersionFieldGenerator(source);
        GeneratorTestHelper.AssertHasDiagnostic(result, "VS2001");
    }

    // ---------- Mixed field types (mirrors the Unity SyncSample) ----------

    private const string MixedModel = @"
namespace Test
{
    public enum PClass : byte { Warrior, Mage, Rogue }
    public partial class Item : IVersionSync
    {
        [VersionField] private string m_Name;
        [VersionField] private int m_Count;
    }
    public partial class Stats : IVersionSync
    {
        [VersionField] private int m_Strength;
        [VersionField] private int m_Agility;
        [VersionField] private Item m_Weapon;   // sync object nested inside a sync object
    }
    public partial class Player : IVersionSync
    {
        [VersionField] private string m_Name;
        [VersionField] private int m_Health;
        [VersionField] private float m_Mana;
        [VersionField] private bool m_IsAlive;
        [VersionField] private long m_Experience;
        [VersionField] private PClass m_Class;
        [VersionField] private Stats m_Stats;
        [VersionField] private VersionList<Item> m_Items;
        [VersionField] private VersionDictionary<string, int> m_Resources;
        [VersionField] private VersionHashSet<string> m_Buffs;
    }
}";

    [Test]
    public void MixedFieldTypes_Full_RoundTrip()
    {
        var r = GeneratorTestHelper.CompileAndRun(MixedModel);
        var pclass = r.Assembly.GetType("Test.PClass")!;

        dynamic a = r.Create("Test.Player");
        var actx = Attach(a);
        a.Name = "Hero"; a.Health = 100; a.Mana = 50.5f; a.IsAlive = true; a.Experience = 1500L;
        a.Class = (dynamic)System.Enum.ToObject(pclass, 1);                       // Mage
        a.Stats = r.Create("Test.Stats"); a.Stats.Strength = 10; a.Stats.Agility = 7;
        a.Stats.Weapon = r.Create("Test.Item"); a.Stats.Weapon.Name = "Dagger"; a.Stats.Weapon.Count = 1;
        dynamic items = System.Activator.CreateInstance(
            r.Assembly.GetType("Test.Player")!.GetProperty("Items")!.PropertyType);
        a.Items = items;
        dynamic sword = r.Create("Test.Item"); sword.Name = "Sword"; sword.Count = 1; a.Items.Add(sword);
        a.Resources = new ReactiveBinding.VersionDictionary<string, int>();
        a.Resources["gold"] = 100; a.Resources["wood"] = 20;
        a.Buffs = new ReactiveBinding.VersionHashSet<string>(); a.Buffs.Add("haste");

        dynamic b = r.Create("Test.Player");
        var bctx = Attach(b);
        Apply(bctx, Full(actx));

        Assert.That((string)b.Name, Is.EqualTo("Hero"));
        Assert.That((int)b.Health, Is.EqualTo(100));
        Assert.That((float)b.Mana, Is.EqualTo(50.5f));
        Assert.That((bool)b.IsAlive, Is.True);
        Assert.That((long)b.Experience, Is.EqualTo(1500L));
        Assert.That((int)b.Class, Is.EqualTo(1));
        Assert.That((int)b.Stats.Strength, Is.EqualTo(10));
        Assert.That((int)b.Stats.Agility, Is.EqualTo(7));
        Assert.That((string)b.Stats.Weapon.Name, Is.EqualTo("Dagger"));
        Assert.That((int)b.Stats.Weapon.Count, Is.EqualTo(1));
        Assert.That((int)b.Items.Count, Is.EqualTo(1));
        Assert.That((string)b.Items[0].Name, Is.EqualTo("Sword"));
        Assert.That((int)b.Resources["gold"], Is.EqualTo(100));
        Assert.That((int)b.Resources["wood"], Is.EqualTo(20));
        Assert.That((bool)b.Buffs.Contains("haste"), Is.True);
    }

    [Test]
    public void MixedFieldTypes_Delta_RoundTrip()
    {
        var r = GeneratorTestHelper.CompileAndRun(MixedModel);
        var pclass = r.Assembly.GetType("Test.PClass")!;

        dynamic a = r.Create("Test.Player");
        var actx = Attach(a);
        a.Name = "Hero"; a.Health = 100; a.Mana = 50.5f; a.Class = (dynamic)System.Enum.ToObject(pclass, 1);
        a.Stats = r.Create("Test.Stats"); a.Stats.Strength = 10;
        a.Stats.Weapon = r.Create("Test.Item"); a.Stats.Weapon.Name = "Dagger"; a.Stats.Weapon.Count = 1;
        dynamic items = System.Activator.CreateInstance(
            r.Assembly.GetType("Test.Player")!.GetProperty("Items")!.PropertyType);
        a.Items = items;
        dynamic sword = r.Create("Test.Item"); sword.Name = "Sword"; sword.Count = 1; a.Items.Add(sword);
        a.Resources = new ReactiveBinding.VersionDictionary<string, int>();
        a.Resources["gold"] = 100; a.Resources["wood"] = 20;
        a.Buffs = new ReactiveBinding.VersionHashSet<string>(); a.Buffs.Add("haste");

        dynamic b = r.Create("Test.Player");
        var bctx = Attach(b);
        Apply(bctx, Full(actx));

        // Mutate a bit of everything.
        a.Health = 80;
        a.Mana = 30.0f;
        a.Class = (dynamic)System.Enum.ToObject(pclass, 0);   // Warrior
        a.Stats.Strength = 15;                        // nested object, by id
        a.Stats.Weapon.Count = 2;                     // nested-of-nested object, by its own id
        a.Items[0].Count = 3;                         // element, by id
        dynamic shield = r.Create("Test.Item"); shield.Name = "Shield"; shield.Count = 1; a.Items.Add(shield);
        a.Resources["gold"] = 250;
        a.Resources.Remove("wood");
        a.Buffs.Add("shield");
        a.Buffs.Remove("haste");

        Apply(bctx, Delta(actx));

        Assert.That((int)b.Health, Is.EqualTo(80));
        Assert.That((float)b.Mana, Is.EqualTo(30.0f));
        Assert.That((int)b.Class, Is.EqualTo(0));
        Assert.That((int)b.Stats.Strength, Is.EqualTo(15));
        Assert.That((int)b.Stats.Weapon.Count, Is.EqualTo(2));
        Assert.That((int)b.Items.Count, Is.EqualTo(2));
        Assert.That((int)b.Items[0].Count, Is.EqualTo(3));
        Assert.That((string)b.Items[1].Name, Is.EqualTo("Shield"));
        Assert.That((int)b.Resources["gold"], Is.EqualTo(250));
        Assert.That((bool)b.Resources.ContainsKey("wood"), Is.False);
        Assert.That((bool)b.Buffs.Contains("shield"), Is.True);
        Assert.That((bool)b.Buffs.Contains("haste"), Is.False);
    }
}
