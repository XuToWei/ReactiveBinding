using System.IO;
using NUnit.Framework;
using ReactiveBinding;

namespace ReactiveBinding.SourceGenerator.Tests;

[TestFixture]
public class AdvancedSyncTests
{
    private const string RaidModel = @"
namespace Advanced
{
    public enum RaidPhase : byte { Lobby, Combat, Results }

    public partial class Modifier : IVersionSync
    {
        [VersionField] private string __Id;
        [VersionField] private int __Power;
    }

    public partial class Item : IVersionSync
    {
        [VersionField] private int __TemplateId;
        [VersionField] private string __Name;
        [VersionField] private int __Durability;
        [VersionField] private VersionSyncList<Modifier> __Modifiers;
    }

    public partial class Player : IVersionSync
    {
        [VersionField] private int __PlayerId;
        [VersionField] private string __Name;
        [VersionField] private int __Health;
        [VersionField] private Item __Equipped;
        [VersionField] private VersionSyncList<Item> __Inventory;
        [VersionField] private VersionSyncDictionary<string, int> __Resources;
        [VersionField] private VersionSyncHashSet<string> __Effects;
    }

    public partial class RaidWorld : IVersionSync, IReactiveObserver
    {
        [VersionField] private long __Tick;
        [VersionField] private RaidPhase __Phase;
        [VersionField] private VersionSyncDictionary<int, Player> __Players;
        [VersionField] private VersionSyncList<string> __EventLog;

        [ReactiveSource] private RaidWorld State => this;
        public int NotificationCount { get; private set; }

        [ReactiveBind(nameof(State))]
        private void OnStateChanged()
        {
            NotificationCount++;
        }
    }
}";

    private static SyncContext Attach(dynamic root)
    {
        var context = new SyncContext();
        ((IVersionSync)root).AttachTo(context);
        return context;
    }

    private static byte[] CaptureFull(SyncContext context)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        context.CaptureFull(writer);
        writer.Flush();
        return stream.ToArray();
    }

    private static byte[] CaptureDelta(SyncContext context)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        context.CaptureDelta(writer);
        writer.Flush();
        return stream.ToArray();
    }

    private static void Apply(SyncContext context, byte[] frame)
    {
        using var stream = new MemoryStream(frame);
        using var reader = new BinaryReader(stream);
        context.Apply(reader);
    }

    private static dynamic NewContainer(CompiledResult result, string ownerType, string propertyName)
    {
        var property = result.Assembly.GetType(ownerType)!.GetProperty(propertyName)!;
        return System.Activator.CreateInstance(property.PropertyType)!;
    }

    private static dynamic NewModifier(CompiledResult result, string id, int power)
    {
        dynamic modifier = result.Create("Advanced.Modifier");
        modifier.Id = id;
        modifier.Power = power;
        return modifier;
    }

    private static dynamic NewItem(CompiledResult result, int templateId, string name, int durability,
        params (string id, int power)[] modifiers)
    {
        dynamic item = result.Create("Advanced.Item");
        item.TemplateId = templateId;
        item.Name = name;
        item.Durability = durability;
        item.Modifiers = NewContainer(result, "Advanced.Item", "Modifiers");
        foreach (var modifier in modifiers)
            item.Modifiers.Add(NewModifier(result, modifier.id, modifier.power));
        return item;
    }

    private static dynamic NewPlayer(CompiledResult result, int id, string name)
    {
        dynamic player = result.Create("Advanced.Player");
        player.PlayerId = id;
        player.Name = name;
        player.Health = 100;
        player.Equipped = NewItem(result, 1000 + id, "Weapon-" + id, 100, ("attack", 10 + id));
        player.Inventory = NewContainer(result, "Advanced.Player", "Inventory");
        player.Inventory.Add(NewItem(result, 2000 + id, "Potion-" + id, 1, ("heal", 25)));
        player.Resources = new VersionSyncDictionary<string, int>();
        player.Resources["gold"] = id * 100;
        player.Resources["wood"] = 20;
        player.Effects = new VersionSyncHashSet<string>();
        player.Effects.Add("ready");
        return player;
    }

    private static dynamic NewWorld(CompiledResult result)
    {
        dynamic world = result.Create("Advanced.RaidWorld");
        world.Tick = 1L;
        world.Phase = (dynamic)System.Enum.ToObject(result.Assembly.GetType("Advanced.RaidPhase")!, 0);
        world.Players = NewContainer(result, "Advanced.RaidWorld", "Players");
        world.Players[1] = NewPlayer(result, 1, "Alice");
        world.Players[2] = NewPlayer(result, 2, "Bob");
        world.EventLog = new VersionSyncList<string>();
        world.EventLog.Add("raid-created");
        return world;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new AssertionException("Repository root was not found from the test output directory.");
    }

    [Test]
    public void ComplexRaid_FullSnapshot_RebuildsMultiPlayerObjectGraph()
    {
        var result = GeneratorTestHelper.CompileAndRunAll(RaidModel);
        dynamic producer = NewWorld(result);
        var producerContext = Attach(producer);

        dynamic consumer = result.Create("Advanced.RaidWorld");
        var consumerContext = Attach(consumer);
        Apply(consumerContext, CaptureFull(producerContext));

        Assert.Multiple(() =>
        {
            Assert.That((long)consumer.Tick, Is.EqualTo(1));
            Assert.That((int)consumer.Phase, Is.EqualTo(0));
            Assert.That((int)consumer.Players.Count, Is.EqualTo(2));
            Assert.That((string)consumer.Players[1].Name, Is.EqualTo("Alice"));
            Assert.That((string)consumer.Players[2].Name, Is.EqualTo("Bob"));
            Assert.That((string)consumer.Players[1].Equipped.Name, Is.EqualTo("Weapon-1"));
            Assert.That((int)consumer.Players[1].Equipped.Modifiers[0].Power, Is.EqualTo(11));
            Assert.That((string)consumer.Players[2].Inventory[0].Name, Is.EqualTo("Potion-2"));
            Assert.That((int)consumer.Players[2].Resources["gold"], Is.EqualTo(200));
            Assert.That((bool)consumer.Players[2].Effects.Contains("ready"), Is.True);
            Assert.That((string)consumer.EventLog[0], Is.EqualTo("raid-created"));
        });
    }

    [Test]
    public void ComplexRaid_DeltaFrame_ConvergesMixedMutationsAndNewSubtrees()
    {
        var result = GeneratorTestHelper.CompileAndRunAll(RaidModel);
        dynamic producer = NewWorld(result);
        var producerContext = Attach(producer);
        dynamic consumer = result.Create("Advanced.RaidWorld");
        var consumerContext = Attach(consumer);
        Apply(consumerContext, CaptureFull(producerContext));

        // Repeated scalar writes coalesce to the final value while independent nodes and containers
        // contribute their own records to the same delta frame.
        producer.Tick = 2L;
        producer.Tick = 3L;
        producer.Tick = 4L;
        producer.Phase = (dynamic)System.Enum.ToObject(result.Assembly.GetType("Advanced.RaidPhase")!, 1);
        producer.Players[1].Health = 90;
        producer.Players[1].Health = 72;
        producer.Players[1].Equipped.Durability = 64;
        producer.Players[1].Equipped.Modifiers[0].Power = 99;
        producer.Players[1].Inventory.Add(NewItem(result, 3001, "Elixir", 2, ("shield", 40)));
        producer.Players[1].Resources["gold"] = 999;
        producer.Players[1].Resources.Remove("wood");
        producer.Players[1].Effects.Remove("ready");
        producer.Players[1].Effects.Add("stunned");
        producer.Players[2].Equipped = NewItem(result, 4002, "Bow", 88, ("range", 16));
        producer.Players[3] = NewPlayer(result, 3, "Carol");
        producer.EventLog.Add("combat-started");
        producer.EventLog.Add("boss-spawned");

        Apply(consumerContext, CaptureDelta(producerContext));

        Assert.Multiple(() =>
        {
            Assert.That((long)consumer.Tick, Is.EqualTo(4));
            Assert.That((int)consumer.Phase, Is.EqualTo(1));
            Assert.That((int)consumer.Players.Count, Is.EqualTo(3));
            Assert.That((int)consumer.Players[1].Health, Is.EqualTo(72));
            Assert.That((int)consumer.Players[1].Equipped.Durability, Is.EqualTo(64));
            Assert.That((int)consumer.Players[1].Equipped.Modifiers[0].Power, Is.EqualTo(99));
            Assert.That((int)consumer.Players[1].Inventory.Count, Is.EqualTo(2));
            Assert.That((string)consumer.Players[1].Inventory[1].Name, Is.EqualTo("Elixir"));
            Assert.That((int)consumer.Players[1].Inventory[1].Modifiers[0].Power, Is.EqualTo(40));
            Assert.That((int)consumer.Players[1].Resources["gold"], Is.EqualTo(999));
            Assert.That((bool)consumer.Players[1].Resources.ContainsKey("wood"), Is.False);
            Assert.That((bool)consumer.Players[1].Effects.Contains("ready"), Is.False);
            Assert.That((bool)consumer.Players[1].Effects.Contains("stunned"), Is.True);
            Assert.That((string)consumer.Players[2].Equipped.Name, Is.EqualTo("Bow"));
            Assert.That((int)consumer.Players[2].Equipped.Modifiers[0].Power, Is.EqualTo(16));
            Assert.That((string)consumer.Players[3].Name, Is.EqualTo("Carol"));
            Assert.That((string)consumer.Players[3].Inventory[0].Name, Is.EqualTo("Potion-3"));
            Assert.That((string)consumer.EventLog[2], Is.EqualTo("boss-spawned"));
        });
    }

    [Test]
    public void ComplexRaid_RemovalDeltaThenKeyframe_PrunesEntireStalePlayerSubtree()
    {
        var result = GeneratorTestHelper.CompileAndRunAll(RaidModel);
        dynamic producer = NewWorld(result);
        var producerContext = Attach(producer);
        dynamic consumer = result.Create("Advanced.RaidWorld");
        var consumerContext = Attach(consumer);
        Apply(consumerContext, CaptureFull(producerContext));

        IVersionSync stalePlayer = (IVersionSync)consumer.Players[2];
        IVersionSync staleEquipped = (IVersionSync)consumer.Players[2].Equipped;
        IVersionSync staleModifier = (IVersionSync)consumer.Players[2].Equipped.Modifiers[0];
        int stalePlayerId = stalePlayer.__SyncId;
        int staleEquippedId = staleEquipped.__SyncId;
        int staleModifierId = staleModifier.__SyncId;

        producer.Players.Remove(2);
        producer.EventLog.Add("player-2-left");
        Apply(consumerContext, CaptureDelta(producerContext));

        Assert.Multiple(() =>
        {
            Assert.That((bool)consumer.Players.ContainsKey(2), Is.False);
            Assert.That((object)stalePlayer.__Parent, Is.Null);
            Assert.That(consumerContext.__Objects.ContainsKey(stalePlayerId), Is.True);
            Assert.That(consumerContext.__Objects.ContainsKey(staleEquippedId), Is.True);
            Assert.That(consumerContext.__Objects.ContainsKey(staleModifierId), Is.True);
        });

        Apply(consumerContext, CaptureFull(producerContext));

        Assert.Multiple(() =>
        {
            Assert.That(consumerContext.__Objects.ContainsKey(stalePlayerId), Is.False);
            Assert.That(consumerContext.__Objects.ContainsKey(staleEquippedId), Is.False);
            Assert.That(consumerContext.__Objects.ContainsKey(staleModifierId), Is.False);
            Assert.That(stalePlayer.__SyncId, Is.Zero);
            Assert.That(staleEquipped.__SyncId, Is.Zero);
            Assert.That(staleModifier.__SyncId, Is.Zero);
            Assert.That((object)stalePlayer.__SyncContext, Is.Null);
            Assert.That((object)staleEquipped.__SyncContext, Is.Null);
            Assert.That((object)staleModifier.__SyncContext, Is.Null);
        });
    }

    [Test]
    public void ComplexRaid_ApplyThenObserve_NotifiesOnceForEachAppliedFrame()
    {
        var result = GeneratorTestHelper.CompileAndRunAll(RaidModel);
        dynamic producer = NewWorld(result);
        var producerContext = Attach(producer);
        dynamic consumer = result.Create("Advanced.RaidWorld");
        var consumerContext = Attach(consumer);

        consumer.ObserveChanges();
        Assert.That((int)consumer.NotificationCount, Is.EqualTo(1), "first observation initializes the cache");

        Apply(consumerContext, CaptureFull(producerContext));
        consumer.ObserveChanges();
        Assert.That((int)consumer.NotificationCount, Is.EqualTo(2));
        consumer.ObserveChanges();
        Assert.That((int)consumer.NotificationCount, Is.EqualTo(2), "no local version changed");

        // Two independently registered descendants change in the same frame. Apply advances their local
        // versions and propagates to the world, while the observer coalesces that state into one callback.
        producer.Players[1].Health = 65;
        producer.Players[1].Equipped.Modifiers[0].Power = 120;
        Apply(consumerContext, CaptureDelta(producerContext));
        consumer.ObserveChanges();

        Assert.Multiple(() =>
        {
            Assert.That((int)consumer.NotificationCount, Is.EqualTo(3));
            Assert.That((int)consumer.Players[1].Health, Is.EqualTo(65));
            Assert.That((int)consumer.Players[1].Equipped.Modifiers[0].Power, Is.EqualTo(120));
        });

        consumer.ObserveChanges();
        Assert.That((int)consumer.NotificationCount, Is.EqualTo(3));
    }

    [Test]
    public void AdvancedUnitySample_CompilesWithBothProductionGenerators()
    {
        string samplePath = Path.Combine(FindRepositoryRoot(), "Unity", "Samples~", "Demo", "AdvancedSyncSample.cs");
        string source = File.ReadAllText(samplePath) + @"

namespace UnityEngine
{
    public class MonoBehaviour { }

    [System.AttributeUsage(System.AttributeTargets.Method)]
    public sealed class ContextMenuAttribute : System.Attribute
    {
        public ContextMenuAttribute(string name) { }
    }

    public static class Debug
    {
        public static void Log(object message) { }
    }
}";

        var result = GeneratorTestHelper.CompileAndRunAll(source);
        dynamic sample = result.Create("ReactiveBinding.Samples.AdvancedSyncSample");

        Assert.Multiple(() =>
        {
            Assert.That(result.Assembly.GetType("ReactiveBinding.Samples.AdvancedRaidWorld"), Is.Not.Null);
            Assert.That(result.Assembly.GetType("ReactiveBinding.Samples.AdvancedSyncSample"), Is.Not.Null);
        });
        Assert.DoesNotThrow(() => sample.RunAdvancedDemo());
    }
}
