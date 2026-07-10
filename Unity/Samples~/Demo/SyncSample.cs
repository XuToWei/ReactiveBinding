using System.Collections.Generic;
using UnityEngine;

namespace ReactiveBinding.Samples
{
    /// <summary>Enum fields sync as their underlying integral type.</summary>
    public enum PlayerClass : byte { Warrior, Mage, Rogue }

    /// <summary>
    /// Data-synchronization model. Declaring a [VersionField] class as `: IVersionSync` opts the whole
    /// class into sync: every [VersionField] is synced and the class joins a SyncContext's flat registry.
    /// Fields must be [VersionField] (private, __ prefix) and must NOT have an initializer.
    /// </summary>
    public partial class SyncInventoryItem : IVersionSync
    {
        [VersionField] private string __Name;
        [VersionField] private int __Count;
    }

    /// <summary>
    /// A nested sync object: its own registry node, so an internal field change syncs as its own record
    /// (independent of the parent — no tree walk).
    /// </summary>
    public partial class SyncStats : IVersionSync
    {
        [VersionField] private int __Strength;
        [VersionField] private int __Agility;

        // A nested sync object inside a nested sync object: deeper graph, still just its own registry node.
        [VersionField] private SyncInventoryItem __Weapon;
    }

    public partial class SyncPlayer : IVersionSync
    {
        // --- Scalars: every primitive / string / enum is supported ---
        [VersionField] private string __Name;
        [VersionField] private int __Health;
        [VersionField] private float __Mana;          // float (epsilon-compared)
        [VersionField] private bool __IsAlive;        // bool
        [VersionField] private long __Experience;     // 64-bit int
        [VersionField] private PlayerClass __Class;   // enum

        // --- A nested sync object (its own node) ---
        [VersionField] private SyncStats __Stats;

        // --- A version container of IVersionSync elements: each element is a registry node with its own
        // id, so an element's internal change syncs as its own record (no parent/list traversal). ---
        [VersionField] private VersionSyncList<SyncInventoryItem> __Items;

        // --- Scalar containers ---
        [VersionField] private VersionSyncDictionary<string, int> __Resources;  // name -> amount
        [VersionField] private VersionSyncHashSet<string> __Buffs;              // active buff ids
    }

    /// <summary>
    /// Self-contained data-sync demo. Attach to a GameObject and press Play; results are logged.
    /// No networking required — a "producer" and a "consumer" each hold a <see cref="SyncContext"/>;
    /// the bytes written by <c>CaptureFull</c> into a caller-supplied <c>BinaryWriter</c> are what you would normally
    /// ship over a transport or write to disk (here we just feed them straight into the consumer).
    ///
    /// Model: every syncable object is registered in a SyncContext under a stable id. <c>CaptureFull(writer)</c>
    /// serializes the whole registry (every node, by ascending id) into the writer, a complete self-contained full
    /// snapshot. <c>Apply</c> reads that snapshot from a
    /// <c>BinaryReader</c> and rebuilds the peer to match: existing nodes update in place, referenced nodes are
    /// created on first sight, and any node the snapshot didn't mention is dropped. Both sides seed the same root
    /// object via <c>root.AttachTo(ctx)</c> (both get root id 1).
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

            producer.Items = new VersionSyncList<SyncInventoryItem>();
            producer.Items.Add(new SyncInventoryItem { Name = "Sword", Count = 1 });

            producer.Resources = new VersionSyncDictionary<string, int>();
            producer.Resources["gold"] = 100;
            producer.Resources["wood"] = 20;

            producer.Buffs = new VersionSyncHashSet<string>();
            producer.Buffs.Add("haste");

            // --- CaptureFull: serialize the whole registry into a caller-owned writer as a full snapshot ---
            var fullStream = new System.IO.MemoryStream();
            producerCtx.CaptureFull(new System.IO.BinaryWriter(fullStream));
            byte[] full = fullStream.ToArray();

            // --- Consumer: seed the same root, then apply the full snapshot ---
            var consumerCtx = new SyncContext();
            var consumer = new SyncPlayer();
            consumer.AttachTo(consumerCtx);
            consumerCtx.Apply(new System.IO.BinaryReader(new System.IO.MemoryStream(full)));   // normally you'd ship `full` over a transport instead
            Debug.Log($"[Sync] after full  : {Describe(consumer)}");

            // --- Mutate a bit of everything, then capture and apply an incremental delta ---
            producer.Health = 80;                    // scalar change
            producer.Mana = 30.0f;                   // float change
            producer.Class = PlayerClass.Warrior;    // enum change
            producer.Stats.Strength = 15;            // nested object
            producer.Stats.Weapon.Count = 2;         // nested-of-nested object
            producer.Items[0].Count = 3;             // element
            producer.Items.Add(new SyncInventoryItem { Name = "Shield", Count = 1 }); // structural add
            producer.Resources["gold"] = 250;        // dictionary set
            producer.Resources.Remove("wood");       // dictionary remove
            producer.Buffs.Add("shield");            // set add
            producer.Buffs.Remove("haste");          // set remove

            var deltaStream = new System.IO.MemoryStream();
            producerCtx.CaptureDelta(new System.IO.BinaryWriter(deltaStream));
            byte[] delta = deltaStream.ToArray();
            consumerCtx.Apply(new System.IO.BinaryReader(new System.IO.MemoryStream(delta)));
            Debug.Log($"[Sync] after delta : {Describe(consumer)} (delta = {delta.Length} bytes)");
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

        private static string DescribeItems(VersionSyncList<SyncInventoryItem> items)
        {
            if (items == null) return "none";
            var parts = new List<string>();
            foreach (var it in items)
                parts.Add($"{it.Name} x{it.Count}");
            return string.Join(", ", parts);
        }

        private static string DescribeResources(VersionSyncDictionary<string, int> resources)
        {
            if (resources == null) return "none";
            var parts = new List<string>();
            foreach (var kv in resources)
                parts.Add($"{kv.Key}:{kv.Value}");
            return string.Join(", ", parts);
        }

        private static string DescribeBuffs(VersionSyncHashSet<string> buffs)
        {
            if (buffs == null) return "none";
            var parts = new List<string>();
            foreach (var b in buffs)
                parts.Add(b);
            return string.Join(", ", parts);
        }
    }
}
