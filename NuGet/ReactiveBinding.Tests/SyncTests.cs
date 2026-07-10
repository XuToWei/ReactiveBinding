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
        [VersionField] private int __Health;
        [VersionField] private string __Name;
    }
}";

    private const string NestedModel = @"
namespace Test
{
    public partial class Bag : IVersionSync
    {
        [VersionField] private int __Gold;
    }
    public partial class Player : IVersionSync
    {
        [VersionField] private int __Health;
        [VersionField] private Bag __Bag;
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

    // Caller owns the output stream: CaptureFull writes a complete snapshot into the supplied writer; grab the bytes.
    private static byte[] Snapshot(SyncContext ctx)
    {
        var ms = new System.IO.MemoryStream();
        var w = new System.IO.BinaryWriter(ms);
        ctx.CaptureFull(w);
        w.Flush();
        return ms.ToArray();
    }

    private static byte[] Full(SyncContext ctx) => Snapshot(ctx);

    private static byte[] Delta(SyncContext ctx) => Snapshot(ctx);

    private static void Apply(SyncContext ctx, byte[] data) => ctx.Apply(new System.IO.BinaryReader(new System.IO.MemoryStream(data)));

    // True-incremental helper: CaptureDelta writes the [byte 0] marker + one record per dirty node into a fresh
    // writer; the bytes are the self-contained frame business would ship.
    private static byte[] DeltaFrame(SyncContext ctx)
    {
        var ms = new System.IO.MemoryStream();
        var w = new System.IO.BinaryWriter(ms);
        ctx.CaptureDelta(w);
        w.Flush();
        return ms.ToArray();
    }

    // ---------- Generated shape ----------

    [Test]
    public void SyncField_GeneratesFlatRegistryApi()
    {
        var result = GeneratorTestHelper.RunVersionFieldGenerator(ScalarModel);
        GeneratorTestHelper.AssertNoErrors(result);
        GeneratorTestHelper.AssertGeneratedContains(result, "public int __SyncId");
        GeneratorTestHelper.AssertGeneratedContains(result, "public ReactiveBinding.SyncContext __SyncContext");
        GeneratorTestHelper.AssertGeneratedContains(result, "public void __CaptureFull(System.IO.BinaryWriter writer)");
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
        [VersionField] private int __Health;
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
    public void Scalar_Recommit_IsFullSnapshot()
    {
        var r = GeneratorTestHelper.CompileAndRun(ScalarModel);
        dynamic a = r.Create("Test.PlayerData");
        var actx = Attach(a);
        a.Health = 1;
        var first = Full(actx);    // full snapshot
        var again = Delta(actx);   // nothing changed -> still a full snapshot, identical bytes
        Assert.That(again.Length, Is.GreaterThan(0));
        Assert.That(again, Is.EqualTo(first));   // every commit re-serializes the whole state
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

    // ---------- VersionSyncList<scalar> ----------

    private const string ListModel = @"
namespace Test
{
    public partial class Inv : IVersionSync
    {
        [VersionField] private VersionSyncList<int> __Nums;
    }
}";

    [Test]
    public void ListScalar_Full_RoundTrip()
    {
        var r = GeneratorTestHelper.CompileAndRun(ListModel);
        dynamic a = r.Create("Test.Inv");
        var actx = Attach(a);
        a.Nums = new ReactiveBinding.VersionSyncList<int>();
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
        a.Nums = new ReactiveBinding.VersionSyncList<int>();
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

    // ---------- VersionSyncList<IVersionSync> (object elements, by id) ----------

    private const string ObjListModel = @"
namespace Test
{
    public partial class Item : IVersionSync
    {
        [VersionField] private int __Qty;
    }
    public partial class Bag : IVersionSync
    {
        [VersionField] private VersionSyncList<Item> __Items;
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

    // ---------- VersionSyncDictionary<scalar,scalar> ----------

    private const string DictModel = @"
namespace Test
{
    public partial class Reg : IVersionSync
    {
        [VersionField] private VersionSyncDictionary<string, int> __Map;
    }
}";

    private const string DictObjectModel = @"
namespace Test
{
    public partial class Item : IVersionSync
    {
        [VersionField] private string __Name;
        [VersionField] private int __Qty;
    }
    public partial class Reg : IVersionSync
    {
        [VersionField] private VersionSyncDictionary<string, Item> __Map;
    }
}";

    private static dynamic NewNamedItem(dynamic r, string name, int qty)
    {
        dynamic item = r.Create("Test.Item");
        item.Name = name;
        item.Qty = qty;
        return item;
    }

    [Test]
    public void Dict_Full_RoundTrip()
    {
        var r = GeneratorTestHelper.CompileAndRun(DictModel);
        dynamic a = r.Create("Test.Reg");
        var actx = Attach(a);
        a.Map = new ReactiveBinding.VersionSyncDictionary<string, int>();
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
        a.Map = new ReactiveBinding.VersionSyncDictionary<string, int>();
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

    [Test]
    public void DictObject_Full_RoundTrip()
    {
        var r = GeneratorTestHelper.CompileAndRun(DictObjectModel);
        dynamic a = r.Create("Test.Reg");
        var actx = Attach(a);
        dynamic map = System.Activator.CreateInstance(r.Assembly.GetType("Test.Reg")!.GetProperty("Map")!.PropertyType);
        a.Map = map;
        a.Map["sword"] = NewNamedItem(r, "Sword", 1);
        a.Map["potion"] = NewNamedItem(r, "Potion", 3);

        dynamic b = r.Create("Test.Reg");
        var bctx = Attach(b);
        Apply(bctx, Full(actx));

        Assert.That((int)b.Map.Count, Is.EqualTo(2));
        Assert.That((string)b.Map["sword"].Name, Is.EqualTo("Sword"));
        Assert.That((int)b.Map["potion"].Qty, Is.EqualTo(3));
    }

    [Test]
    public void DictObject_TrueDelta_SetReplaceRemoveAndPrune()
    {
        var r = GeneratorTestHelper.CompileAndRun(DictObjectModel);
        dynamic a = r.Create("Test.Reg");
        var actx = Attach(a);
        dynamic map = System.Activator.CreateInstance(r.Assembly.GetType("Test.Reg")!.GetProperty("Map")!.PropertyType);
        a.Map = map;
        a.Map["sword"] = NewNamedItem(r, "Sword", 1);
        a.Map["potion"] = NewNamedItem(r, "Potion", 3);

        dynamic b = r.Create("Test.Reg");
        var bctx = Attach(b);
        Apply(bctx, Full(actx));
        ReactiveBinding.IVersionSync oldSword = (ReactiveBinding.IVersionSync)b.Map["sword"];
        int oldSwordId = oldSword.__SyncId;
        ReactiveBinding.IVersionSync oldPotion = (ReactiveBinding.IVersionSync)b.Map["potion"];
        int oldPotionId = oldPotion.__SyncId;

        a.Map["sword"] = NewNamedItem(r, "Axe", 2);
        a.Map.Remove("potion");
        a.Map["shield"] = NewNamedItem(r, "Shield", 1);
        Apply(bctx, DeltaFrame(actx));

        Assert.That((string)b.Map["sword"].Name, Is.EqualTo("Axe"));
        Assert.That((bool)b.Map.ContainsKey("potion"), Is.False);
        Assert.That((string)b.Map["shield"].Name, Is.EqualTo("Shield"));
        Assert.That((object)oldSword.__Parent, Is.Null);
        Assert.That((object)oldPotion.__Parent, Is.Null);
        Assert.That(bctx.__Objects.ContainsKey(oldSwordId), Is.True);
        Assert.That(bctx.__Objects.ContainsKey(oldPotionId), Is.True);

        Apply(bctx, Full(actx));

        Assert.That(bctx.__Objects.ContainsKey(oldSwordId), Is.False);
        Assert.That(bctx.__Objects.ContainsKey(oldPotionId), Is.False);
        Assert.That(oldSword.__SyncId, Is.EqualTo(0));
        Assert.That(oldPotion.__SyncId, Is.EqualTo(0));
    }

    // ---------- VersionSyncHashSet<scalar> ----------

    private const string SetModel = @"
namespace Test
{
    public partial class Tg : IVersionSync
    {
        [VersionField] private VersionSyncHashSet<string> __Tags;
    }
}";

    private const string SetObjectModel = @"
namespace Test
{
    public partial class Item : IVersionSync
    {
        [VersionField] private string __Name;
        [VersionField] private int __Qty;
    }
    public partial class Tg : IVersionSync
    {
        [VersionField] private VersionSyncHashSet<Item> __Items;
    }
}";

    private static dynamic FindNamedItem(dynamic items, string name)
    {
        foreach (dynamic item in items)
            if ((string)item.Name == name) return item;
        throw new AssertionException($"Item '{name}' not found.");
    }

    [Test]
    public void HashSet_Full_RoundTrip()
    {
        var r = GeneratorTestHelper.CompileAndRun(SetModel);
        dynamic a = r.Create("Test.Tg");
        var actx = Attach(a);
        a.Tags = new ReactiveBinding.VersionSyncHashSet<string>();
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
        a.Tags = new ReactiveBinding.VersionSyncHashSet<string>();
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

    [Test]
    public void HashSetObject_Full_RoundTrip()
    {
        var r = GeneratorTestHelper.CompileAndRun(SetObjectModel);
        dynamic a = r.Create("Test.Tg");
        var actx = Attach(a);
        dynamic items = System.Activator.CreateInstance(r.Assembly.GetType("Test.Tg")!.GetProperty("Items")!.PropertyType);
        a.Items = items;
        a.Items.Add(NewNamedItem(r, "Sword", 1));
        a.Items.Add(NewNamedItem(r, "Potion", 3));

        dynamic b = r.Create("Test.Tg");
        var bctx = Attach(b);
        Apply(bctx, Full(actx));

        Assert.That((int)b.Items.Count, Is.EqualTo(2));
        Assert.That((int)FindNamedItem(b.Items, "Sword").Qty, Is.EqualTo(1));
        Assert.That((int)FindNamedItem(b.Items, "Potion").Qty, Is.EqualTo(3));
    }

    [Test]
    public void HashSetObject_TrueDelta_AddRemoveAndPrune()
    {
        var r = GeneratorTestHelper.CompileAndRun(SetObjectModel);
        dynamic a = r.Create("Test.Tg");
        var actx = Attach(a);
        dynamic items = System.Activator.CreateInstance(r.Assembly.GetType("Test.Tg")!.GetProperty("Items")!.PropertyType);
        a.Items = items;
        a.Items.Add(NewNamedItem(r, "Sword", 1));
        dynamic potion = NewNamedItem(r, "Potion", 3); a.Items.Add(potion);

        dynamic b = r.Create("Test.Tg");
        var bctx = Attach(b);
        Apply(bctx, Full(actx));
        ReactiveBinding.IVersionSync oldPotion = (ReactiveBinding.IVersionSync)FindNamedItem(b.Items, "Potion");
        int oldPotionId = oldPotion.__SyncId;

        a.Items.Remove(potion);
        a.Items.Add(NewNamedItem(r, "Shield", 2));
        FindNamedItem(a.Items, "Sword").Qty = 9;
        Apply(bctx, DeltaFrame(actx));

        Assert.That((int)b.Items.Count, Is.EqualTo(2));
        Assert.That((int)FindNamedItem(b.Items, "Sword").Qty, Is.EqualTo(9));
        Assert.That((int)FindNamedItem(b.Items, "Shield").Qty, Is.EqualTo(2));
        Assert.That((object)oldPotion.__Parent, Is.Null);
        Assert.That(bctx.__Objects.ContainsKey(oldPotionId), Is.True);

        Apply(bctx, Full(actx));

        Assert.That(bctx.__Objects.ContainsKey(oldPotionId), Is.False);
        Assert.That(oldPotion.__SyncId, Is.EqualTo(0));
    }

    // ---------- True incremental (direct-write delta) round-trips ----------
    // Baseline with one full snapshot, mutate, then ship ONLY the bytes appended since
    // (prefixed with a [byte 0] marker) — exercising the per-mutation direct-write path, not a re-CaptureFull.

    [Test]
    public void Scalar_TrueDelta_OnlyChangedField()
    {
        var r = GeneratorTestHelper.CompileAndRun(ScalarModel);
        dynamic a = r.Create("Test.PlayerData");
        var actx = Attach(a);
        a.Health = 100; a.Name = "abc";

        dynamic b = r.Create("Test.PlayerData");
        var bctx = Attach(b);
        Apply(bctx, Full(actx));               // baseline

        a.Health = 150;                        // only Health -> one [id][slot][int] record
        Apply(bctx, DeltaFrame(actx));

        Assert.That((int)b.Health, Is.EqualTo(150));
        Assert.That((string)b.Name, Is.EqualTo("abc"));
    }

    [Test]
    public void Nested_TrueDelta_InnerField()
    {
        var r = GeneratorTestHelper.CompileAndRun(NestedModel);
        dynamic a = r.Create("Test.Player");
        var actx = Attach(a);
        a.Health = 1; a.Bag = r.Create("Test.Bag"); a.Bag.Gold = 10;

        dynamic b = r.Create("Test.Player");
        var bctx = Attach(b);
        Apply(bctx, Full(actx));

        a.Bag.Gold = 99;                       // inner object reports itself by its own id
        Apply(bctx, DeltaFrame(actx));

        Assert.That((int)b.Bag.Gold, Is.EqualTo(99));
        Assert.That((int)b.Health, Is.EqualTo(1));
    }

    [Test]
    public void Nested_TrueDelta_Reassign_ShipsNewSubtree()
    {
        var r = GeneratorTestHelper.CompileAndRun(NestedModel);
        dynamic a = r.Create("Test.Player");
        var actx = Attach(a);
        a.Bag = r.Create("Test.Bag"); a.Bag.Gold = 1;

        dynamic b = r.Create("Test.Player");
        var bctx = Attach(b);
        Apply(bctx, Full(actx));

        dynamic nb = r.Create("Test.Bag"); nb.Gold = 7;
        a.Bag = nb;                            // parent ref record + brand-new subtree shipped in the frame
        Apply(bctx, DeltaFrame(actx));

        Assert.That((int)b.Bag.Gold, Is.EqualTo(7));
    }

    [Test]
    public void Nested_TrueDelta_SetNull()
    {
        var r = GeneratorTestHelper.CompileAndRun(NestedModel);
        dynamic a = r.Create("Test.Player");
        var actx = Attach(a);
        a.Bag = r.Create("Test.Bag"); a.Bag.Gold = 5;

        dynamic b = r.Create("Test.Player");
        var bctx = Attach(b);
        Apply(bctx, Full(actx));

        a.Bag = null;                          // parent ref record with child id 0
        Apply(bctx, DeltaFrame(actx));

        Assert.That((object)b.Bag, Is.Null);
    }

    [Test]
    public void ListScalar_TrueDelta_AddRemove()
    {
        var r = GeneratorTestHelper.CompileAndRun(ListModel);
        dynamic a = r.Create("Test.Inv");
        var actx = Attach(a);
        a.Nums = new ReactiveBinding.VersionSyncList<int>();
        a.Nums.Add(10); a.Nums.Add(20);

        dynamic b = r.Create("Test.Inv");
        var bctx = Attach(b);
        Apply(bctx, Full(actx));

        a.Nums.Add(30); a.Nums.RemoveAt(0);    // two structural records; last container record wins
        Apply(bctx, DeltaFrame(actx));

        Assert.That((int)b.Nums.Count, Is.EqualTo(2));
        Assert.That((int)b.Nums[0], Is.EqualTo(20));
        Assert.That((int)b.Nums[1], Is.EqualTo(30));
    }

    [Test]
    public void ListObject_TrueDelta_AddElementAndField()
    {
        var r = GeneratorTestHelper.CompileAndRun(ObjListModel);
        dynamic a = r.Create("Test.Bag");
        var actx = Attach(a);
        dynamic list = System.Activator.CreateInstance(r.Assembly.GetType("Test.Bag")!.GetProperty("Items")!.PropertyType); a.Items = list;
        dynamic it = r.Create("Test.Item"); it.Qty = 5; a.Items.Add(it);

        dynamic b = r.Create("Test.Bag");
        var bctx = Attach(b);
        Apply(bctx, Full(actx));

        dynamic it2 = r.Create("Test.Item"); it2.Qty = 7; a.Items.Add(it2);  // container record + new element subtree
        a.Items[0].Qty = 9;                                                   // existing element field, by its own id
        Apply(bctx, DeltaFrame(actx));

        Assert.That((int)b.Items.Count, Is.EqualTo(2));
        Assert.That((int)b.Items[0].Qty, Is.EqualTo(9));
        Assert.That((int)b.Items[1].Qty, Is.EqualTo(7));
    }

    [Test]
    public void Dict_TrueDelta_SetRemove()
    {
        var r = GeneratorTestHelper.CompileAndRun(DictModel);
        dynamic a = r.Create("Test.Reg");
        var actx = Attach(a);
        a.Map = new ReactiveBinding.VersionSyncDictionary<string, int>();
        a.Map["a"] = 1; a.Map["b"] = 2;

        dynamic b = r.Create("Test.Reg");
        var bctx = Attach(b);
        Apply(bctx, Full(actx));

        a.Map["c"] = 3; a.Map.Remove("a");
        Apply(bctx, DeltaFrame(actx));

        Assert.That((int)b.Map.Count, Is.EqualTo(2));
        Assert.That((bool)b.Map.ContainsKey("a"), Is.False);
        Assert.That((int)b.Map["c"], Is.EqualTo(3));
    }

    [Test]
    public void HashSet_TrueDelta_AddRemove()
    {
        var r = GeneratorTestHelper.CompileAndRun(SetModel);
        dynamic a = r.Create("Test.Tg");
        var actx = Attach(a);
        a.Tags = new ReactiveBinding.VersionSyncHashSet<string>();
        a.Tags.Add("x"); a.Tags.Add("y");

        dynamic b = r.Create("Test.Tg");
        var bctx = Attach(b);
        Apply(bctx, Full(actx));

        a.Tags.Add("z"); a.Tags.Remove("x");
        Apply(bctx, DeltaFrame(actx));

        Assert.That((bool)b.Tags.Contains("x"), Is.False);
        Assert.That((bool)b.Tags.Contains("y"), Is.True);
        Assert.That((bool)b.Tags.Contains("z"), Is.True);
    }

    [Test]
    public void Keyframe_Prunes_NodesLeakedByDeltaRemoval()
    {
        var r = GeneratorTestHelper.CompileAndRun(ObjListModel);
        dynamic a = r.Create("Test.Bag");
        var actx = Attach(a);
        dynamic list = System.Activator.CreateInstance(r.Assembly.GetType("Test.Bag")!.GetProperty("Items")!.PropertyType); a.Items = list;
        dynamic it = r.Create("Test.Item"); it.Qty = 5; a.Items.Add(it);

        dynamic b = r.Create("Test.Bag");
        var bctx = Attach(b);
        Apply(bctx, Full(actx));

        a.Items.RemoveAt(0);                   // delta marker=0 -> consumer keeps the orphaned element node
        Apply(bctx, DeltaFrame(actx));
        Assert.That((int)b.Items.Count, Is.EqualTo(0));
        int leaked = bctx.__Objects.Count;     // bag + items list + orphaned item

        Apply(bctx, Full(actx));               // full keyframe prunes the orphan
        Assert.That(bctx.__Objects.Count, Is.LessThan(leaked));
    }

    [Test]
    public void ApplyFull_AdvancesNextId_ForConsumerSideAdds()
    {
        var r = GeneratorTestHelper.CompileAndRun(ObjListModel);
        dynamic a = r.Create("Test.Bag");
        var actx = Attach(a);
        dynamic list = System.Activator.CreateInstance(r.Assembly.GetType("Test.Bag")!.GetProperty("Items")!.PropertyType); a.Items = list;
        dynamic it = r.Create("Test.Item"); it.Qty = 5; a.Items.Add(it);

        dynamic b = r.Create("Test.Bag");
        var bctx = Attach(b);
        Apply(bctx, Full(actx));

        Assert.That(bctx.__NextId, Is.GreaterThan((int)b.Items[0].__SyncId));
        var listId = (int)b.Items.__SyncId;
        dynamic local = r.Create("Test.Item"); local.Qty = 9;
        b.Items.Add(local);

        Assert.That((int)local.__SyncId, Is.GreaterThan((int)b.Items[0].__SyncId));
        Assert.That((object)bctx.__Objects[listId], Is.SameAs((object)b.Items));
    }

    [Test]
    public void ApplyDelta_SetNull_ClearsOldChildParent()
    {
        var r = GeneratorTestHelper.CompileAndRun(NestedModel);
        dynamic a = r.Create("Test.Player");
        var actx = Attach(a);
        a.Bag = r.Create("Test.Bag"); a.Bag.Gold = 5;

        dynamic b = r.Create("Test.Player");
        var bctx = Attach(b);
        Apply(bctx, Full(actx));
        ReactiveBinding.IVersion old = (ReactiveBinding.IVersion)b.Bag;

        a.Bag = null;
        Apply(bctx, DeltaFrame(actx));

        Assert.That((object)b.Bag, Is.Null);
        Assert.That((object)old.__Parent, Is.Null);
    }

    [Test]
    public void ApplyFull_PruneDetachesStaleNode()
    {
        var r = GeneratorTestHelper.CompileAndRun(ObjListModel);
        dynamic a = r.Create("Test.Bag");
        var actx = Attach(a);
        dynamic list = System.Activator.CreateInstance(r.Assembly.GetType("Test.Bag")!.GetProperty("Items")!.PropertyType); a.Items = list;
        dynamic it = r.Create("Test.Item"); it.Qty = 5; a.Items.Add(it);

        dynamic b = r.Create("Test.Bag");
        var bctx = Attach(b);
        Apply(bctx, Full(actx));
        dynamic staleNode = b.Items[0];
        ReactiveBinding.IVersionSync stale = (ReactiveBinding.IVersionSync)staleNode;
        int staleId = stale.__SyncId;

        // Full-snapshot pruning unregisters stale nodes through the complete reset lifecycle. Values are kept so
        // external holders can reuse the detached instance, while version/sync bookkeeping is reset.
        staleNode.Qty = 9;
        Assert.That(stale.__Version, Is.Not.EqualTo(0));
        Assert.That(stale.__IsDirty, Is.True);

        a.Items.RemoveAt(0);
        Apply(bctx, DeltaFrame(actx));
        Assert.That(bctx.__Objects.ContainsKey(staleId), Is.True);

        Apply(bctx, Full(actx));

        Assert.That(bctx.__Objects.ContainsKey(staleId), Is.False);
        Assert.That(stale.__SyncId, Is.EqualTo(0));
        Assert.That((object)stale.__SyncContext, Is.Null);
        Assert.That((object)stale.__Parent, Is.Null);
        Assert.That(stale.__Version, Is.EqualTo(0));
        Assert.That(stale.__IsDirty, Is.False);
        Assert.That((int)staleNode.Qty, Is.EqualTo(9));

        var reuseContext = new ReactiveBinding.SyncContext();
        stale.AttachTo(reuseContext);
        Assert.That(stale.__SyncId, Is.EqualTo(1));
        Assert.That((object)stale.__SyncContext, Is.SameAs(reuseContext));
        Assert.That((int)staleNode.Qty, Is.EqualTo(9));
    }

    [Test]
    public void Nested_TrueDelta_Reassign_ClearsOldParentAndKeyframePrunesOldNode()
    {
        var r = GeneratorTestHelper.CompileAndRun(NestedModel);
        dynamic a = r.Create("Test.Player");
        var actx = Attach(a);
        a.Bag = r.Create("Test.Bag"); a.Bag.Gold = 1;

        dynamic b = r.Create("Test.Player");
        var bctx = Attach(b);
        Apply(bctx, Full(actx));
        ReactiveBinding.IVersionSync oldBag = (ReactiveBinding.IVersionSync)b.Bag;
        int oldBagId = oldBag.__SyncId;

        dynamic newBag = r.Create("Test.Bag"); newBag.Gold = 7;
        a.Bag = newBag;
        Apply(bctx, DeltaFrame(actx));

        Assert.That((int)b.Bag.Gold, Is.EqualTo(7));
        Assert.That((object)oldBag.__Parent, Is.Null);
        Assert.That(bctx.__Objects.ContainsKey(oldBagId), Is.True);   // deltas do not prune removed nodes

        Apply(bctx, Full(actx));

        Assert.That(bctx.__Objects.ContainsKey(oldBagId), Is.False);
        Assert.That(oldBag.__SyncId, Is.EqualTo(0));
        Assert.That((object)oldBag.__SyncContext, Is.Null);
    }

    [Test]
    public void ListObject_TrueDelta_SetElementAndUpdateSibling_PrunesReplacedElementOnKeyframe()
    {
        var r = GeneratorTestHelper.CompileAndRun(ObjListModel);
        dynamic a = r.Create("Test.Bag");
        var actx = Attach(a);
        dynamic list = System.Activator.CreateInstance(r.Assembly.GetType("Test.Bag")!.GetProperty("Items")!.PropertyType); a.Items = list;
        dynamic first = r.Create("Test.Item"); first.Qty = 1; a.Items.Add(first);
        dynamic second = r.Create("Test.Item"); second.Qty = 2; a.Items.Add(second);

        dynamic b = r.Create("Test.Bag");
        var bctx = Attach(b);
        Apply(bctx, Full(actx));
        ReactiveBinding.IVersionSync oldFirst = (ReactiveBinding.IVersionSync)b.Items[0];
        int oldFirstId = oldFirst.__SyncId;

        dynamic replacement = r.Create("Test.Item"); replacement.Qty = 7;
        a.Items[0] = replacement;       // container SET op + brand-new child record
        a.Items[1].Qty = 22;            // sibling object field record in the same frame
        Apply(bctx, DeltaFrame(actx));

        Assert.That((int)b.Items.Count, Is.EqualTo(2));
        Assert.That((int)b.Items[0].Qty, Is.EqualTo(7));
        Assert.That((int)b.Items[1].Qty, Is.EqualTo(22));
        Assert.That((object)oldFirst.__Parent, Is.Null);
        Assert.That(bctx.__Objects.ContainsKey(oldFirstId), Is.True);

        Apply(bctx, Full(actx));

        Assert.That(bctx.__Objects.ContainsKey(oldFirstId), Is.False);
        Assert.That(oldFirst.__SyncId, Is.EqualTo(0));
        Assert.That((object)oldFirst.__SyncContext, Is.Null);
    }

    [Test]
    public void ListObject_TrueDelta_FullDirtyReorderWithNewAndExistingElements()
    {
        var r = GeneratorTestHelper.CompileAndRun(ObjListModel);
        dynamic a = r.Create("Test.Bag");
        var actx = Attach(a);
        dynamic list = System.Activator.CreateInstance(r.Assembly.GetType("Test.Bag")!.GetProperty("Items")!.PropertyType); a.Items = list;
        dynamic first = r.Create("Test.Item"); first.Qty = 1; a.Items.Add(first);

        dynamic b = r.Create("Test.Bag");
        var bctx = Attach(b);
        Apply(bctx, Full(actx));

        dynamic second = r.Create("Test.Item"); second.Qty = 2;
        a.Items.Add(second);            // attaches a new child
        a.Items[0].Qty = 9;             // existing child dirty record
        a.Items.Reverse();              // forces the list itself to emit a full contents record
        Apply(bctx, DeltaFrame(actx));

        Assert.That((int)b.Items.Count, Is.EqualTo(2));
        Assert.That((int)b.Items[0].Qty, Is.EqualTo(2));
        Assert.That((int)b.Items[1].Qty, Is.EqualTo(9));
        Assert.That((object)((ReactiveBinding.IVersion)b.Items[0]).__Parent, Is.SameAs((object)b.Items));
        Assert.That((object)((ReactiveBinding.IVersion)b.Items[1]).__Parent, Is.SameAs((object)b.Items));
    }

    // ---------- Coalescing & op-log (write-volume optimizations) ----------

    [Test]
    public void Scalar_TrueDelta_CoalescesFieldsUnderOneId()
    {
        var r = GeneratorTestHelper.CompileAndRun(ScalarModel);

        // One field changed -> one record.
        dynamic a1 = r.Create("Test.PlayerData"); var c1 = Attach(a1); a1.Health = 1; a1.Name = "x";
        dynamic b1 = r.Create("Test.PlayerData"); var cb1 = Attach(b1); Apply(cb1, Full(c1));
        a1.Health = 2; var f1 = DeltaFrame(c1);

        // Two fields of the same node changed -> still ONE record (shared id + mask), not two.
        dynamic a2 = r.Create("Test.PlayerData"); var c2 = Attach(a2); a2.Health = 1; a2.Name = "x";
        dynamic b2 = r.Create("Test.PlayerData"); var cb2 = Attach(b2); Apply(cb2, Full(c2));
        a2.Health = 2; a2.Name = "yy"; var f2 = DeltaFrame(c2);
        Apply(cb2, f2);

        Assert.That(f2.Length, Is.LessThan(2 * f1.Length));   // coalesced: id/mask written once, not per field
        Assert.That((int)b2.Health, Is.EqualTo(2));
        Assert.That((string)b2.Name, Is.EqualTo("yy"));
    }

    [Test]
    public void Scalar_TrueDelta_RepeatedWritesSendFinalValueOnce()
    {
        var r = GeneratorTestHelper.CompileAndRun(ScalarModel);
        dynamic a = r.Create("Test.PlayerData"); var actx = Attach(a); a.Health = 1; a.Name = "x";
        dynamic b = r.Create("Test.PlayerData"); var bctx = Attach(b); Apply(bctx, Full(actx));

        a.Health = 10; a.Health = 20; a.Health = 30;   // only the final value ships
        var f = DeltaFrame(actx);
        Apply(bctx, f);

        Assert.That((int)b.Health, Is.EqualTo(30));
        Assert.That(f.Length, Is.LessThan(15));        // a single small record, not three
    }

    [Test]
    public void ListObject_TrueDelta_AddDoesNotResendAllSubtrees()
    {
        var r = GeneratorTestHelper.CompileAndRun(ObjListModel);
        dynamic a = r.Create("Test.Bag");
        var actx = Attach(a);
        dynamic list = System.Activator.CreateInstance(r.Assembly.GetType("Test.Bag")!.GetProperty("Items")!.PropertyType); a.Items = list;
        for (int i = 0; i < 6; i++) { dynamic it = r.Create("Test.Item"); it.Qty = i; a.Items.Add(it); }

        dynamic b = r.Create("Test.Bag");
        var bctx = Attach(b);
        Apply(bctx, Full(actx));

        dynamic itN = r.Create("Test.Item"); itN.Qty = 99; a.Items.Add(itN);   // add one element
        var f = DeltaFrame(actx);
        Apply(bctx, f);

        Assert.That((int)b.Items.Count, Is.EqualTo(7));
        Assert.That((int)b.Items[6].Qty, Is.EqualTo(99));
        // Frame = container ADD op (id, op, new elem id) + the ONE new element's record — NOT all 7 subtrees.
        Assert.That(f.Length, Is.LessThan(40));
    }

    [Test]
    public void ListScalar_TrueDelta_MixedOpsReplayInOrder()
    {
        var r = GeneratorTestHelper.CompileAndRun(ListModel);
        dynamic a = r.Create("Test.Inv");
        var actx = Attach(a);
        a.Nums = new ReactiveBinding.VersionSyncList<int>();
        a.Nums.Add(10); a.Nums.Add(20); a.Nums.Add(30);

        dynamic b = r.Create("Test.Inv");
        var bctx = Attach(b);
        Apply(bctx, Full(actx));

        a.Nums.Add(40);          // [10,20,30,40]
        a.Nums.RemoveAt(0);      // [20,30,40]
        a.Nums.Insert(1, 99);    // [20,99,30,40]
        Apply(bctx, DeltaFrame(actx));

        Assert.That((int)b.Nums.Count, Is.EqualTo(4));
        Assert.That((int)b.Nums[0], Is.EqualTo(20));
        Assert.That((int)b.Nums[1], Is.EqualTo(99));
        Assert.That((int)b.Nums[2], Is.EqualTo(30));
        Assert.That((int)b.Nums[3], Is.EqualTo(40));
    }

    [Test]
    public void MixedFieldTypes_TrueDelta_ManyFieldsCoalesce()
    {
        var r = GeneratorTestHelper.CompileAndRun(MixedModel);
        var pclass = r.Assembly.GetType("Test.PClass")!;
        dynamic a = r.Create("Test.Player");
        var actx = Attach(a);
        a.Name = "Hero"; a.Health = 100; a.Mana = 50.5f; a.IsAlive = true; a.Experience = 1L;
        a.Class = (dynamic)System.Enum.ToObject(pclass, 1);

        dynamic b = r.Create("Test.Player");
        var bctx = Attach(b);
        Apply(bctx, Full(actx));

        a.Health = 80; a.Mana = 30f; a.IsAlive = false; a.Experience = 2L; a.Name = "Hi";   // 5 fields, one node
        var f = DeltaFrame(actx);
        Apply(bctx, f);

        Assert.That((int)b.Health, Is.EqualTo(80));
        Assert.That((float)b.Mana, Is.EqualTo(30f));
        Assert.That((bool)b.IsAlive, Is.False);
        Assert.That((long)b.Experience, Is.EqualTo(2L));
        Assert.That((string)b.Name, Is.EqualTo("Hi"));
        // One node record: [0][id 4][mask 2 (10 fields -> ushort)] + 5 payloads. Far below 5 separate [id][slot] records.
        Assert.That(f.Length, Is.LessThan(1 + 4 + 2 + 32));
    }

    // ---------- Object-pool reuse (__Reset) ----------

    [Test]
    public void Reset_DetachesSubtree_AndReAttachRoundTrips()
    {
        var r = GeneratorTestHelper.CompileAndRun(NestedModel);
        dynamic a = r.Create("Test.Player");
        var ctx1 = Attach(a);
        a.Health = 5; a.Bag = r.Create("Test.Bag"); a.Bag.Gold = 7;

        dynamic b1 = r.Create("Test.Player");
        var bctx1 = Attach(b1);
        Apply(bctx1, Full(ctx1));
        Assert.That((int)b1.Bag.Gold, Is.EqualTo(7));

        // Reset for reuse: whole subtree detaches (ids zeroed), contents kept.
        a.__Reset();
        Assert.That((int)a.__SyncId, Is.EqualTo(0));
        Assert.That((object)a.__SyncContext, Is.Null);
        Assert.That((int)a.Bag.__SyncId, Is.EqualTo(0));     // recursed into the reference field
        Assert.That((int)a.Bag.Gold, Is.EqualTo(7));         // value preserved

        // Re-attach the SAME instances to a fresh context, mutate, round-trip into a fresh consumer.
        var ctx2 = Attach(a);
        a.Health = 9; a.Bag.Gold = 11;
        dynamic b2 = r.Create("Test.Player");
        var bctx2 = Attach(b2);
        Apply(bctx2, Full(ctx2));
        Assert.That((int)b2.Health, Is.EqualTo(9));
        Assert.That((int)b2.Bag.Gold, Is.EqualTo(11));
    }

    [Test]
    public void Reset_RecursesContainerObjectElements()
    {
        var r = GeneratorTestHelper.CompileAndRun(ObjListModel);
        dynamic a = r.Create("Test.Bag");
        var ctx1 = Attach(a);
        dynamic list = System.Activator.CreateInstance(r.Assembly.GetType("Test.Bag")!.GetProperty("Items")!.PropertyType); a.Items = list;
        dynamic it = r.Create("Test.Item"); it.Qty = 5; a.Items.Add(it);

        a.__Reset();
        Assert.That((int)a.__SyncId, Is.EqualTo(0));
        Assert.That((int)a.Items.__SyncId, Is.EqualTo(0));    // container detached
        Assert.That((int)a.Items[0].__SyncId, Is.EqualTo(0)); // object element detached (recursed)
        Assert.That((int)a.Items.Count, Is.EqualTo(1));       // contents kept
        Assert.That((int)a.Items[0].Qty, Is.EqualTo(5));

        // Re-attach + round-trip.
        var ctx2 = Attach(a);
        dynamic b = r.Create("Test.Bag");
        var bctx = Attach(b);
        Apply(bctx, Full(ctx2));
        Assert.That((int)b.Items.Count, Is.EqualTo(1));
        Assert.That((int)b.Items[0].Qty, Is.EqualTo(5));
    }

    // ---------- Diagnostics ----------

    [Test]
    public void DictObjectKey_NotSupported_ReportsVS0001()
    {
        var source = @"
namespace Test
{
    public partial class V : IVersionSync
    {
        [VersionField] private int __X;
    }
    public partial class Reg : IVersionSync
    {
        [VersionField] private VersionSyncDictionary<V, int> __Map;
    }
}";
        var result = GeneratorTestHelper.RunVersionFieldGenerator(source);
        GeneratorTestHelper.AssertHasDiagnostic(result, "VS0001");
    }

    [Test]
    public void NonSyncContainerInSyncClass_NotSupported_ReportsVS0001()
    {
        // A version-only container (not IVersionSync) used as a synced [VersionField] must use the sync variant.
        var source = @"
namespace Test
{
    public partial class Inv : IVersionSync
    {
        [VersionField] private VersionList<int> __Nums;
    }
}";
        var result = GeneratorTestHelper.RunVersionFieldGenerator(source);
        GeneratorTestHelper.AssertHasDiagnostic(result, "VS0001");
    }


    [Test]
    public void ManySyncFields_UsesMultipleMasksAndRoundTrips()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("namespace Test { public partial class Big : IVersionSync {");
        for (int i = 0; i < 70; i++) sb.AppendLine($"    [VersionField] private int __F{i};");
        sb.AppendLine("} }");
        var source = sb.ToString();

        var result = GeneratorTestHelper.RunVersionFieldGenerator(source);
        GeneratorTestHelper.AssertNoErrors(result);
        GeneratorTestHelper.AssertGeneratedContains(result, "__dirtyMask0");
        GeneratorTestHelper.AssertGeneratedContains(result, "__dirtyMask1");
        GeneratorTestHelper.AssertGeneratedContains(result, "writer.Write((ulong)__mask0)");
        GeneratorTestHelper.AssertGeneratedContains(result, "writer.Write((byte)__mask1)");

        var r = GeneratorTestHelper.CompileAndRun(source);
        dynamic a = r.Create("Test.Big");
        var actx = Attach(a);
        a.F0 = 10;
        a.F63 = 630;
        a.F64 = 640;
        a.F69 = 690;

        dynamic b = r.Create("Test.Big");
        var bctx = Attach(b);
        Apply(bctx, Full(actx));

        Assert.That((int)b.F0, Is.EqualTo(10));
        Assert.That((int)b.F63, Is.EqualTo(630));
        Assert.That((int)b.F64, Is.EqualTo(640));
        Assert.That((int)b.F69, Is.EqualTo(690));

        a.F1 = 11;
        a.F64 = 641;
        a.F69 = 691;
        Apply(bctx, DeltaFrame(actx));

        Assert.That((int)b.F0, Is.EqualTo(10));
        Assert.That((int)b.F1, Is.EqualTo(11));
        Assert.That((int)b.F63, Is.EqualTo(630));
        Assert.That((int)b.F64, Is.EqualTo(641));
        Assert.That((int)b.F69, Is.EqualTo(691));
    }

    [Test]
    public void SyncObjectWithoutPublicParameterlessCtor_ReportsVS0002()
    {
        var source = @"
namespace Test
{
    public partial class Child : IVersionSync
    {
        public Child(int value) {}
        [VersionField] private int __Value;
    }
    public partial class Parent : IVersionSync
    {
        [VersionField] private Child __Child;
    }
}";
        var result = GeneratorTestHelper.RunVersionFieldGenerator(source);
        GeneratorTestHelper.AssertHasDiagnostic(result, "VS0002");
    }

    [Test]
    public void SyncObjectListElementWithoutPublicParameterlessCtor_ReportsVS0002()
    {
        var source = @"
namespace Test
{
    public partial class Child : IVersionSync
    {
        private Child() {}
        [VersionField] private int __Value;
    }
    public partial class Parent : IVersionSync
    {
        [VersionField] private VersionSyncList<Child> __Children;
    }
}";
        var result = GeneratorTestHelper.RunVersionFieldGenerator(source);
        GeneratorTestHelper.AssertHasDiagnostic(result, "VS0002");
    }

    [Test]
    public void SyncObjectDictionaryValueWithoutPublicParameterlessCtor_ReportsVS0002()
    {
        var source = @"
namespace Test
{
    public partial class Child : IVersionSync
    {
        private Child() {}
        [VersionField] private int __Value;
    }
    public partial class Parent : IVersionSync
    {
        [VersionField] private VersionSyncDictionary<string, Child> __Children;
    }
}";
        var result = GeneratorTestHelper.RunVersionFieldGenerator(source);
        GeneratorTestHelper.AssertHasDiagnostic(result, "VS0002");
    }

    [Test]
    public void SyncObjectHashSetElementWithoutPublicParameterlessCtor_ReportsVS0002()
    {
        var source = @"
namespace Test
{
    public partial class Child : IVersionSync
    {
        private Child() {}
        [VersionField] private int __Value;
    }
    public partial class Parent : IVersionSync
    {
        [VersionField] private VersionSyncHashSet<Child> __Children;
    }
}";
        var result = GeneratorTestHelper.RunVersionFieldGenerator(source);
        GeneratorTestHelper.AssertHasDiagnostic(result, "VS0002");
    }

    // ---------- Deeply nested sync graph ----------

    private const string DeepNestedModel = @"
namespace Test
{
    public partial class Mod : IVersionSync
    {
        [VersionField] private string __Key;
        [VersionField] private int __Value;
    }
    public partial class Slot : IVersionSync
    {
        [VersionField] private string __Name;
        [VersionField] private int __Level;
        [VersionField] private VersionSyncList<Mod> __Mods;
        [VersionField] private VersionSyncDictionary<string, Mod> __ModMap;
        [VersionField] private VersionSyncHashSet<Mod> __ModSet;
        [VersionField] private VersionSyncDictionary<string, int> __Stats;
        [VersionField] private VersionSyncHashSet<string> __Flags;
    }
    public partial class Loadout : IVersionSync
    {
        [VersionField] private string __Title;
        [VersionField] private Slot __Primary;
        [VersionField] private VersionSyncList<Slot> __Backpack;
        [VersionField] private VersionSyncDictionary<string, Slot> __SlotMap;
        [VersionField] private VersionSyncHashSet<Slot> __SlotSet;
        [VersionField] private VersionSyncDictionary<string, int> __Counters;
    }
    public partial class Account : IVersionSync
    {
        [VersionField] private string __UserId;
        [VersionField] private Loadout __Active;
        [VersionField] private VersionSyncList<Loadout> __Saved;
        [VersionField] private VersionSyncHashSet<string> __Tags;
    }
}";

    private static dynamic NewDeepMod(dynamic r, string key, int value)
    {
        dynamic mod = r.Create("Test.Mod");
        mod.Key = key;
        mod.Value = value;
        return mod;
    }

    private static dynamic FindDeepMod(dynamic mods, string key)
    {
        foreach (dynamic mod in mods)
            if ((string)mod.Key == key) return mod;
        throw new AssertionException($"Mod '{key}' not found.");
    }

    private static dynamic FindDeepSlot(dynamic slots, string name)
    {
        foreach (dynamic slot in slots)
            if ((string)slot.Name == name) return slot;
        throw new AssertionException($"Slot '{name}' not found.");
    }

    private static dynamic NewDeepSlot(dynamic r, string name, int level, params (string key, int value)[] mods)
    {
        dynamic slot = r.Create("Test.Slot");
        slot.Name = name;
        slot.Level = level;
        dynamic modList = System.Activator.CreateInstance(r.Assembly.GetType("Test.Slot")!.GetProperty("Mods")!.PropertyType);
        slot.Mods = modList;
        foreach (var mod in mods) slot.Mods.Add(NewDeepMod(r, mod.key, mod.value));
        dynamic modMap = System.Activator.CreateInstance(r.Assembly.GetType("Test.Slot")!.GetProperty("ModMap")!.PropertyType);
        slot.ModMap = modMap;
        slot.ModMap["core"] = NewDeepMod(r, "core", level * 10);
        dynamic modSet = System.Activator.CreateInstance(r.Assembly.GetType("Test.Slot")!.GetProperty("ModSet")!.PropertyType);
        slot.ModSet = modSet;
        slot.ModSet.Add(NewDeepMod(r, "set-" + name, level));
        slot.Stats = new ReactiveBinding.VersionSyncDictionary<string, int>();
        slot.Stats["durability"] = 100;
        slot.Flags = new ReactiveBinding.VersionSyncHashSet<string>();
        slot.Flags.Add("equipped");
        return slot;
    }

    private static dynamic NewDeepLoadout(dynamic r, string title)
    {
        dynamic loadout = r.Create("Test.Loadout");
        loadout.Title = title;
        loadout.Primary = NewDeepSlot(r, title + "-primary", 1, ("atk", 10), ("spd", 3));
        dynamic backpack = System.Activator.CreateInstance(r.Assembly.GetType("Test.Loadout")!.GetProperty("Backpack")!.PropertyType);
        loadout.Backpack = backpack;
        loadout.Backpack.Add(NewDeepSlot(r, title + "-potion", 1, ("heal", 25)));
        loadout.Backpack.Add(NewDeepSlot(r, title + "-bomb", 2, ("blast", 40)));
        dynamic slotMap = System.Activator.CreateInstance(r.Assembly.GetType("Test.Loadout")!.GetProperty("SlotMap")!.PropertyType);
        loadout.SlotMap = slotMap;
        loadout.SlotMap["reserve"] = NewDeepSlot(r, title + "-reserve", 3, ("guard", 12));
        dynamic slotSet = System.Activator.CreateInstance(r.Assembly.GetType("Test.Loadout")!.GetProperty("SlotSet")!.PropertyType);
        loadout.SlotSet = slotSet;
        loadout.SlotSet.Add(NewDeepSlot(r, title + "-trinket", 1, ("luck", 4)));
        loadout.Counters = new ReactiveBinding.VersionSyncDictionary<string, int>();
        loadout.Counters["wins"] = 1;
        return loadout;
    }

    [Test]
    public void DeepNested_Full_RoundTrip()
    {
        var r = GeneratorTestHelper.CompileAndRun(DeepNestedModel);
        dynamic a = r.Create("Test.Account");
        var actx = Attach(a);
        a.UserId = "u1";
        a.Active = NewDeepLoadout(r, "raid");
        dynamic saved = System.Activator.CreateInstance(r.Assembly.GetType("Test.Account")!.GetProperty("Saved")!.PropertyType);
        a.Saved = saved;
        a.Saved.Add(NewDeepLoadout(r, "arena"));
        a.Tags = new ReactiveBinding.VersionSyncHashSet<string>();
        a.Tags.Add("founder");

        dynamic b = r.Create("Test.Account");
        var bctx = Attach(b);
        Apply(bctx, Full(actx));

        Assert.That((string)b.UserId, Is.EqualTo("u1"));
        Assert.That((string)b.Active.Title, Is.EqualTo("raid"));
        Assert.That((string)b.Active.Primary.Mods[0].Key, Is.EqualTo("atk"));
        Assert.That((int)b.Active.Primary.Mods[1].Value, Is.EqualTo(3));
        Assert.That((string)b.Active.Primary.ModMap["core"].Key, Is.EqualTo("core"));
        Assert.That((int)b.Active.Primary.ModMap["core"].Value, Is.EqualTo(10));
        Assert.That((int)b.Active.Primary.ModSet.Count, Is.EqualTo(1));
        Assert.That((string)FindDeepMod(b.Active.Primary.ModSet, "set-raid-primary").Key, Is.EqualTo("set-raid-primary"));
        Assert.That((int)b.Active.Backpack.Count, Is.EqualTo(2));
        Assert.That((string)b.Active.Backpack[1].Mods[0].Key, Is.EqualTo("blast"));
        Assert.That((string)b.Active.SlotMap["reserve"].Name, Is.EqualTo("raid-reserve"));
        Assert.That((int)b.Active.SlotMap["reserve"].Mods[0].Value, Is.EqualTo(12));
        Assert.That((int)b.Active.SlotSet.Count, Is.EqualTo(1));
        Assert.That((string)FindDeepSlot(b.Active.SlotSet, "raid-trinket").Mods[0].Key, Is.EqualTo("luck"));
        Assert.That((int)b.Active.Counters["wins"], Is.EqualTo(1));
        Assert.That((int)b.Saved.Count, Is.EqualTo(1));
        Assert.That((string)b.Saved[0].Primary.Name, Is.EqualTo("arena-primary"));
        Assert.That((bool)b.Tags.Contains("founder"), Is.True);
    }

    [Test]
    public void DeepNested_TrueDelta_MultiLevelMixedMutations()
    {
        var r = GeneratorTestHelper.CompileAndRun(DeepNestedModel);
        dynamic a = r.Create("Test.Account");
        var actx = Attach(a);
        a.UserId = "u1";
        a.Active = NewDeepLoadout(r, "raid");
        dynamic saved = System.Activator.CreateInstance(r.Assembly.GetType("Test.Account")!.GetProperty("Saved")!.PropertyType);
        a.Saved = saved;
        a.Saved.Add(NewDeepLoadout(r, "arena"));
        a.Tags = new ReactiveBinding.VersionSyncHashSet<string>();
        a.Tags.Add("founder");

        dynamic b = r.Create("Test.Account");
        var bctx = Attach(b);
        Apply(bctx, Full(actx));
        ReactiveBinding.IVersionSync oldSavedLoadout = (ReactiveBinding.IVersionSync)b.Saved[0];
        int oldSavedLoadoutId = oldSavedLoadout.__SyncId;

        a.Active.Primary.Mods[0].Value = 99;                       // depth: Account -> Active -> Primary -> Mods[0]
        a.Active.Backpack[1].Stats["durability"] = 45;             // nested dictionary node
        a.Active.Backpack[0].Flags.Add("locked");                  // nested hash set node
        a.Active.Primary.ModMap["core"].Value = 77;                // dictionary object value field
        ReactiveBinding.IVersionSync oldMapValue = (ReactiveBinding.IVersionSync)b.Active.Primary.ModMap["core"];
        int oldMapValueId = oldMapValue.__SyncId;
        a.Active.Primary.ModMap["core"] = NewDeepMod(r, "core2", 88);
        dynamic oldSetValue = null;
        foreach (dynamic m in a.Active.Primary.ModSet) { oldSetValue = m; break; }
        ReactiveBinding.IVersionSync oldSetConsumer = (ReactiveBinding.IVersionSync)FindDeepMod(b.Active.Primary.ModSet, "set-raid-primary");
        int oldSetConsumerId = oldSetConsumer.__SyncId;
        a.Active.Primary.ModSet.Remove(oldSetValue);
        a.Active.Primary.ModSet.Add(NewDeepMod(r, "set-new", 6));
        ReactiveBinding.IVersionSync oldSlotMapValue = (ReactiveBinding.IVersionSync)b.Active.SlotMap["reserve"];
        int oldSlotMapValueId = oldSlotMapValue.__SyncId;
        a.Active.SlotMap["reserve"].Mods[0].Value = 33;
        a.Active.SlotMap["reserve"] = NewDeepSlot(r, "raid-sentinel", 4, ("watch", 18));
        dynamic oldSlotSetValue = null;
        foreach (dynamic slot in a.Active.SlotSet) { oldSlotSetValue = slot; break; }
        ReactiveBinding.IVersionSync oldSlotSetConsumer = (ReactiveBinding.IVersionSync)FindDeepSlot(b.Active.SlotSet, "raid-trinket");
        int oldSlotSetConsumerId = oldSlotSetConsumer.__SyncId;
        a.Active.SlotSet.Remove(oldSlotSetValue);
        a.Active.SlotSet.Add(NewDeepSlot(r, "raid-charm", 2, ("ward", 7)));
        dynamic gem = NewDeepMod(r, "gem", 5); a.Active.Primary.Mods.Insert(1, gem);
        dynamic replacementSaved = NewDeepLoadout(r, "duel");
        a.Saved[0] = replacementSaved;                              // object-list SET + new subtree
        dynamic extra = NewDeepLoadout(r, "craft"); a.Saved.Add(extra);
        a.Tags.Remove("founder"); a.Tags.Add("veteran");

        Apply(bctx, DeltaFrame(actx));

        Assert.That((int)b.Active.Primary.Mods.Count, Is.EqualTo(3));
        Assert.That((int)b.Active.Primary.Mods[0].Value, Is.EqualTo(99));
        Assert.That((string)b.Active.Primary.Mods[1].Key, Is.EqualTo("gem"));
        Assert.That((int)b.Active.Backpack[1].Stats["durability"], Is.EqualTo(45));
        Assert.That((bool)b.Active.Backpack[0].Flags.Contains("locked"), Is.True);
        Assert.That((string)b.Active.Primary.ModMap["core"].Key, Is.EqualTo("core2"));
        Assert.That((int)b.Active.Primary.ModMap["core"].Value, Is.EqualTo(88));
        Assert.That((object)oldMapValue.__Parent, Is.Null);
        Assert.That((int)FindDeepMod(b.Active.Primary.ModSet, "set-new").Value, Is.EqualTo(6));
        Assert.That((object)oldSetConsumer.__Parent, Is.Null);
        Assert.That(bctx.__Objects.ContainsKey(oldMapValueId), Is.True);
        Assert.That(bctx.__Objects.ContainsKey(oldSetConsumerId), Is.True);
        Assert.That((string)b.Active.SlotMap["reserve"].Name, Is.EqualTo("raid-sentinel"));
        Assert.That((int)b.Active.SlotMap["reserve"].Mods[0].Value, Is.EqualTo(18));
        Assert.That((object)oldSlotMapValue.__Parent, Is.Null);
        Assert.That(bctx.__Objects.ContainsKey(oldSlotMapValueId), Is.True);
        Assert.That((string)FindDeepSlot(b.Active.SlotSet, "raid-charm").Mods[0].Key, Is.EqualTo("ward"));
        Assert.That((object)oldSlotSetConsumer.__Parent, Is.Null);
        Assert.That(bctx.__Objects.ContainsKey(oldSlotSetConsumerId), Is.True);
        Assert.That((int)b.Saved.Count, Is.EqualTo(2));
        Assert.That((string)b.Saved[0].Title, Is.EqualTo("duel"));
        Assert.That((string)b.Saved[1].Title, Is.EqualTo("craft"));
        Assert.That((object)oldSavedLoadout.__Parent, Is.Null);
        Assert.That(bctx.__Objects.ContainsKey(oldSavedLoadoutId), Is.True);
        Assert.That((bool)b.Tags.Contains("founder"), Is.False);
        Assert.That((bool)b.Tags.Contains("veteran"), Is.True);

        Apply(bctx, Full(actx));

        Assert.That(bctx.__Objects.ContainsKey(oldSavedLoadoutId), Is.False);
        Assert.That(oldSavedLoadout.__SyncId, Is.EqualTo(0));
        Assert.That((object)oldSavedLoadout.__SyncContext, Is.Null);
        Assert.That(bctx.__Objects.ContainsKey(oldSlotMapValueId), Is.False);
        Assert.That(oldSlotMapValue.__SyncId, Is.EqualTo(0));
        Assert.That((object)oldSlotMapValue.__SyncContext, Is.Null);
        Assert.That(bctx.__Objects.ContainsKey(oldSlotSetConsumerId), Is.False);
        Assert.That(oldSlotSetConsumer.__SyncId, Is.EqualTo(0));
        Assert.That((object)oldSlotSetConsumer.__SyncContext, Is.Null);
        Assert.That((int)b.Active.Primary.Mods[1].Value, Is.EqualTo(5));
    }

    // ---------- Mixed field types (mirrors the Unity SyncSample) ----------

    private const string MixedModel = @"
namespace Test
{
    public enum PClass : byte { Warrior, Mage, Rogue }
    public partial class Item : IVersionSync
    {
        [VersionField] private string __Name;
        [VersionField] private int __Count;
    }
    public partial class Stats : IVersionSync
    {
        [VersionField] private int __Strength;
        [VersionField] private int __Agility;
        [VersionField] private Item __Weapon;   // sync object nested inside a sync object
    }
    public partial class Player : IVersionSync
    {
        [VersionField] private string __Name;
        [VersionField] private int __Health;
        [VersionField] private float __Mana;
        [VersionField] private bool __IsAlive;
        [VersionField] private long __Experience;
        [VersionField] private PClass __Class;
        [VersionField] private Stats __Stats;
        [VersionField] private VersionSyncList<Item> __Items;
        [VersionField] private VersionSyncDictionary<string, int> __Resources;
        [VersionField] private VersionSyncHashSet<string> __Buffs;
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
        a.Resources = new ReactiveBinding.VersionSyncDictionary<string, int>();
        a.Resources["gold"] = 100; a.Resources["wood"] = 20;
        a.Buffs = new ReactiveBinding.VersionSyncHashSet<string>(); a.Buffs.Add("haste");

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
        a.Resources = new ReactiveBinding.VersionSyncDictionary<string, int>();
        a.Resources["gold"] = 100; a.Resources["wood"] = 20;
        a.Buffs = new ReactiveBinding.VersionSyncHashSet<string>(); a.Buffs.Add("haste");

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
