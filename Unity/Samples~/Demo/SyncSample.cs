using System.Collections.Generic;
using UnityEngine;

namespace ReactiveBinding.Samples
{
    /// <summary>Enum fields sync as their underlying integral type.</summary>
    public enum PlayerClass : byte { Warrior, Mage, Rogue }

    /// <summary>
    /// Data-synchronization model. Declaring a [VersionField] class as `: IVersionSync` opts the whole
    /// class into sync: every [VersionField] is synced and the class joins a SyncContext's flat registry.
    /// Fields must be [VersionField] (private, m_ prefix) and must NOT have an initializer.
    /// </summary>
    public partial class SyncInventoryItem : IVersionSync
    {
        [VersionField] private string m_Name;
        [VersionField] private int m_Count;
    }

    /// <summary>
    /// A nested sync object: its own registry node, so an internal field change syncs as its own record
    /// (independent of the parent — no tree walk).
    /// </summary>
    public partial class SyncStats : IVersionSync
    {
        [VersionField] private int m_Strength;
        [VersionField] private int m_Agility;

        // A nested sync object inside a nested sync object: deeper graph, still just its own registry node.
        [VersionField] private SyncInventoryItem m_Weapon;
    }

    public partial class SyncPlayer : IVersionSync
    {
        // --- Scalars: every primitive / string / enum is supported ---
        [VersionField] private string m_Name;
        [VersionField] private int m_Health;
        [VersionField] private float m_Mana;          // float (epsilon-compared)
        [VersionField] private bool m_IsAlive;        // bool
        [VersionField] private long m_Experience;     // 64-bit int
        [VersionField] private PlayerClass m_Class;   // enum

        // --- A nested sync object (its own node) ---
        [VersionField] private SyncStats m_Stats;

        // --- A version container of IVersionSync elements: each element is a registry node with its own
        // id, so an element's internal change syncs as its own record (no parent/list traversal). ---
        [VersionField] private VersionList<SyncInventoryItem> m_Items;

        // --- Scalar containers ---
        [VersionField] private VersionDictionary<string, int> m_Resources;  // name -> amount
        [VersionField] private VersionHashSet<string> m_Buffs;              // active buff ids
    }

    /// <summary>
    /// Self-contained data-sync demo. Attach to a GameObject and press Play; results are logged.
    /// No networking required — a "producer" and a "consumer" each hold a <see cref="SyncContext"/>;
    /// the <c>MemoryStream</c> Commit hands back is what you would normally ship over a transport or write to disk
    /// (here we just wrap it in a <c>BinaryReader</c> straight into the consumer).
    ///
    /// Model: every syncable object is registered in a SyncContext under a stable id, and the context owns
    /// the single never-reallocated stream that mutations write into (an append-only log). A mutation writes its
    /// record straight into that stream the moment it happens. <c>Commit</c> advances a cursor and returns that
    /// same stream positioned at the records written since the last call: the first commit (after attaching an
    /// empty root) is the full state, each later commit is the delta; <c>Apply</c> reads from a
    /// <c>BinaryReader</c>'s position to EOF and applies either onto a peer context. Both sides seed the same root object via
    /// <c>root.AttachTo(ctx)</c> (both get root id 1); every other node is created on the consumer the first
    /// time a reference to it is read.
    /// </summary>
    public class SyncSample : MonoBehaviour
    {
        private void Start()
        {
            RunDemo();
        }

        [ContextMenu("Run Sync Demo")]
        public void RunDemo()
        {
            // --- Producer: create a context and seed the root ---
            var producerCtx = new SyncContext();
            var producer = new SyncPlayer();
            producer.AttachTo(producerCtx);

            producer.Name = "Hero";
            producer.Health = 100;
            producer.Mana = 50.5f;
            producer.IsAlive = true;
            producer.Experience = 1500L;
            producer.Class = PlayerClass.Mage;

            producer.Stats = new SyncStats { Strength = 10, Agility = 7 };
            producer.Stats.Weapon = new SyncInventoryItem { Name = "Dagger", Count = 1 };

            producer.Items = new VersionList<SyncInventoryItem>();
            producer.Items.Add(new SyncInventoryItem { Name = "Sword", Count = 1 });

            producer.Resources = new VersionDictionary<string, int>();
            producer.Resources["gold"] = 100;
            producer.Resources["wood"] = 20;

            producer.Buffs = new VersionHashSet<string>();
            producer.Buffs.Add("haste");

            // --- First commit: the writer's stream positioned at every record so far = the full state ---
            var full = producerCtx.Commit();

            // --- Consumer: seed the same root, then apply the full snapshot ---
            var consumerCtx = new SyncContext();
            var consumer = new SyncPlayer();
            consumer.AttachTo(consumerCtx);
            consumerCtx.Apply(new System.IO.BinaryReader(full));   // normally you'd ship `full`'s bytes over a transport instead
            Debug.Log($"[Sync] after full  : {Describe(consumer)}");

            // --- Mutate a bit of everything, then the next Commit hands back only what changed ---
            producer.Health = 80;                    // scalar change
            producer.Mana = 30.0f;                   // float change
            producer.Class = PlayerClass.Warrior;    // enum change
            producer.Stats.Strength = 15;            // nested object reports itself by id (no tree walk)
            producer.Stats.Weapon.Count = 2;         // nested-of-nested object, also by its own id
            producer.Items[0].Count = 3;             // element reports itself by id
            producer.Items.Add(new SyncInventoryItem { Name = "Shield", Count = 1 }); // structural add
            producer.Resources["gold"] = 250;        // dictionary set
            producer.Resources.Remove("wood");       // dictionary remove
            producer.Buffs.Add("shield");            // set add
            producer.Buffs.Remove("haste");          // set remove

            var delta = producerCtx.Commit();
            long deltaBytes = delta.Length - delta.Position;   // unread records = the delta written since the last commit
            consumerCtx.Apply(new System.IO.BinaryReader(delta));
            Debug.Log($"[Sync] after delta : {Describe(consumer)} (delta = {deltaBytes} bytes)");
        }

        private static string Describe(SyncPlayer p)
        {
            return $"Name={p.Name}, Class={p.Class}, HP={p.Health}, MP={p.Mana}, Alive={p.IsAlive}, XP={p.Experience}, " +
                   $"Stats=({DescribeStats(p.Stats)}), Items=[{DescribeItems(p.Items)}], " +
                   $"Resources={{{DescribeResources(p.Resources)}}}, Buffs=[{DescribeBuffs(p.Buffs)}]";
        }

        private static string DescribeStats(SyncStats s)
        {
            if (s == null) return "none";
            var weapon = s.Weapon == null ? "none" : $"{s.Weapon.Name} x{s.Weapon.Count}";
            return $"STR {s.Strength}, AGI {s.Agility}, Weapon {weapon}";
        }

        private static string DescribeItems(VersionList<SyncInventoryItem> items)
        {
            if (items == null) return "none";
            var parts = new List<string>();
            foreach (var it in items)
                parts.Add($"{it.Name} x{it.Count}");
            return string.Join(", ", parts);
        }

        private static string DescribeResources(VersionDictionary<string, int> resources)
        {
            if (resources == null) return "none";
            var parts = new List<string>();
            foreach (var kv in resources)
                parts.Add($"{kv.Key}:{kv.Value}");
            return string.Join(", ", parts);
        }

        private static string DescribeBuffs(VersionHashSet<string> buffs)
        {
            if (buffs == null) return "none";
            var parts = new List<string>();
            foreach (var b in buffs)
                parts.Add(b);
            return string.Join(", ", parts);
        }
    }
}
