using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ReactiveBinding.Samples
{
    public enum AdvancedRaidPhase : byte
    {
        Lobby,
        Combat,
        Results
    }

    /// <summary>A leaf registry node stored inside an object's VersionSyncList.</summary>
    public partial class AdvancedRaidModifier : IVersionSync
    {
        [VersionField] private string __Id;
        [VersionField] private int __Power;
    }

    /// <summary>An object with both scalar fields and a container of sync-object children.</summary>
    public partial class AdvancedRaidItem : IVersionSync
    {
        [VersionField] private int __TemplateId;
        [VersionField] private string __Name;
        [VersionField] private int __Durability;
        [VersionField] private VersionSyncList<AdvancedRaidModifier> __Modifiers;
    }

    /// <summary>A sync-object element stored in VersionSyncHashSet with the type's default equality.</summary>
    public partial class AdvancedRaidAura : IVersionSync
    {
        [VersionField] private string __Id;
        [VersionField] private int __Stacks;
    }

    /// <summary>
    /// One player subtree. It combines a direct object reference, an object list, a scalar dictionary,
    /// a scalar hash set, and an object hash set. Every referenced object/container receives its own SyncContext id.
    /// </summary>
    public partial class AdvancedRaidPlayer : IVersionSync
    {
        [VersionField] private int __PlayerId;
        [VersionField] private string __Name;
        [VersionField] private int __Health;
        [VersionField] private AdvancedRaidItem __Equipped;
        [VersionField] private VersionSyncList<AdvancedRaidItem> __Inventory;
        [VersionField] private VersionSyncDictionary<string, int> __Resources;
        [VersionField] private VersionSyncHashSet<string> __Effects;
        [VersionField] private VersionSyncHashSet<AdvancedRaidAura> __Auras;
    }

    /// <summary>
    /// Session root. The object-valued dictionary demonstrates dynamic player joins/leaves while retaining
    /// statically typed construction on the consumer (no type tags are sent on the wire).
    /// </summary>
    public partial class AdvancedRaidWorld : IVersionSync
    {
        [VersionField] private long __Tick;
        [VersionField] private AdvancedRaidPhase __Phase;
        [VersionField] private VersionSyncDictionary<int, AdvancedRaidPlayer> __Players;
        [VersionField] private VersionSyncList<string> __EventLog;
    }

    /// <summary>
    /// Advanced end-to-end synchronization demo. Attach this component to a GameObject and run the scene, or use
    /// the context menu. It simulates a producer and consumer locally, but each captured byte array is an ordinary
    /// transport frame that could be sent over a network.
    ///
    /// The sequence demonstrates:
    /// 1. A full baseline that reconstructs a multi-player object graph.
    /// 2. One coalesced delta containing repeated scalar writes, deep object changes, container operations,
    ///    object replacement, and a newly attached player subtree.
    /// 3. A player removal delta. The removed consumer subtree is detached from the dictionary but deliberately
    ///    remains in the consumer registry because delta frames do not carry prune records.
    /// 4. A full keyframe that prunes that stale subtree.
    /// 5. ReactiveBind observing the consumer root's propagated version after every applied frame.
    /// </summary>
    public partial class AdvancedSyncSample : MonoBehaviour, IReactiveObserver
    {
        private SyncContext producerContext;
        private SyncContext consumerContext;
        private AdvancedRaidWorld producer;
        private AdvancedRaidWorld consumer;
        private int notificationCount;

        [ReactiveSource]
        private AdvancedRaidWorld ConsumerWorld => consumer;

        private void Start()
        {
            RunAdvancedDemo();
        }

        [ContextMenu("Run Advanced Sync Demo")]
        public void RunAdvancedDemo()
        {
            ResetChanges();
            notificationCount = 0;

            producer = CreateWorld();
            producerContext = new SyncContext();
            producer.AttachTo(producerContext);

            consumer = new AdvancedRaidWorld();
            consumerContext = new SyncContext();
            consumer.AttachTo(consumerContext);

            byte[] baseline = CaptureFull(producerContext);
            ApplyFrame("full baseline", baseline);

            // Multiple writes to the same field collapse to the final value in this delta.
            producer.Tick = 2;
            producer.Tick = 3;
            producer.Tick = 4;
            producer.Phase = AdvancedRaidPhase.Combat;
            producer.Players[1].Health = 90;
            producer.Players[1].Health = 72;

            // Independent deep nodes and containers all participate in the same frame.
            producer.Players[1].Equipped.Durability = 64;
            producer.Players[1].Equipped.Modifiers[0].Power = 99;
            producer.Players[1].Inventory.Add(CreateItem(3001, "Elixir", 2, "shield", 40));
            producer.Players[1].Resources["gold"] = 999;
            producer.Players[1].Resources.Remove("wood");
            producer.Players[1].Effects.Remove("ready");
            producer.Players[1].Effects.Add("stunned");
            FindAura(producer.Players[1].Auras, "ready-aura").Stacks = 2;
            producer.Players[1].Auras.Remove(FindAura(producer.Players[1].Auras, "shield-aura"));
            producer.Players[1].Auras.Add(new AdvancedRaidAura { Id = "stunned-aura", Stacks = 3 });

            // Replacing an object unregisters the old producer subtree and attaches the new one.
            producer.Players[2].Equipped = CreateItem(4002, "Bow", 88, "range", 16);

            // An object-valued dictionary SET carries the new player's id; its freshly attached subtree records
            // follow later in the same frame because parent/container ids are allocated before descendants.
            producer.Players[3] = CreatePlayer(3, "Carol");
            producer.EventLog.Add("combat-started");
            producer.EventLog.Add("boss-spawned");

            byte[] combatDelta = CaptureDelta(producerContext);
            ApplyFrame("mixed combat delta", combatDelta);

            // Capture a handle to the consumer-side node so the difference between delta removal and keyframe
            // pruning is visible in the log.
            IVersionSync stalePlayer = consumer.Players[2];
            int stalePlayerId = stalePlayer.SyncId;

            producer.Players.Remove(2);
            producer.EventLog.Add("player-2-left");
            byte[] leaveDelta = CaptureDelta(producerContext);
            ApplyFrame("player leave delta", leaveDelta);

            Debug.Log($"[AdvancedSync] after delta removal: in dictionary={consumer.Players.ContainsKey(2)}, " +
                      $"stale id {stalePlayerId} still registered={consumerContext.__Objects.ContainsKey(stalePlayerId)}");

            int registryBeforeKeyframe = consumerContext.__Objects.Count;
            byte[] keyframe = CaptureFull(producerContext);
            ApplyFrame("pruning keyframe", keyframe);

            Debug.Log($"[AdvancedSync] keyframe prune: registry {registryBeforeKeyframe} -> " +
                      $"{consumerContext.__Objects.Count}, stale node id={stalePlayer.SyncId}, " +
                      $"context cleared={stalePlayer.SyncContext == null}");
        }

        [ReactiveBind(nameof(ConsumerWorld))]
        private void OnConsumerWorldChanged()
        {
            notificationCount++;
            Debug.Log($"[AdvancedSync] ReactiveBind notification #{notificationCount}: {Describe(consumer)}");
        }

        private void ApplyFrame(string label, byte[] frame)
        {
            using (var stream = new MemoryStream(frame))
            using (var reader = new BinaryReader(stream))
            {
                consumerContext.Apply(reader);
            }

            ObserveChanges();
            Debug.Log($"[AdvancedSync] {label}: {frame.Length} bytes, " +
                      $"producer nodes={producerContext.__Objects.Count}, consumer nodes={consumerContext.__Objects.Count}");
        }

        private static byte[] CaptureFull(SyncContext context)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                context.CaptureFull(writer);
                writer.Flush();
                return stream.ToArray();
            }
        }

        private static byte[] CaptureDelta(SyncContext context)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                context.CaptureDelta(writer);
                writer.Flush();
                return stream.ToArray();
            }
        }

        private static AdvancedRaidWorld CreateWorld()
        {
            var world = new AdvancedRaidWorld
            {
                Tick = 1,
                Phase = AdvancedRaidPhase.Lobby,
                Players = new VersionSyncDictionary<int, AdvancedRaidPlayer>(),
                EventLog = new VersionSyncList<string>()
            };
            world.Players[1] = CreatePlayer(1, "Alice");
            world.Players[2] = CreatePlayer(2, "Bob");
            world.EventLog.Add("raid-created");
            return world;
        }

        private static AdvancedRaidPlayer CreatePlayer(int id, string name)
        {
            var player = new AdvancedRaidPlayer
            {
                PlayerId = id,
                Name = name,
                Health = 100,
                Equipped = CreateItem(1000 + id, "Weapon-" + id, 100, "attack", 10 + id),
                Inventory = new VersionSyncList<AdvancedRaidItem>(),
                Resources = new VersionSyncDictionary<string, int>(),
                Effects = new VersionSyncHashSet<string>(),
                Auras = new VersionSyncHashSet<AdvancedRaidAura>()
            };
            player.Inventory.Add(CreateItem(2000 + id, "Potion-" + id, 1, "heal", 25));
            player.Resources["gold"] = id * 100;
            player.Resources["wood"] = 20;
            player.Effects.Add("ready");
            player.Auras.Add(new AdvancedRaidAura { Id = "ready-aura", Stacks = 1 });
            player.Auras.Add(new AdvancedRaidAura { Id = "shield-aura", Stacks = 1 });
            return player;
        }

        private static AdvancedRaidAura FindAura(VersionSyncHashSet<AdvancedRaidAura> auras, string id)
        {
            foreach (AdvancedRaidAura aura in auras)
                if (aura.Id == id) return aura;
            throw new System.InvalidOperationException($"Aura '{id}' was not found.");
        }

        private static AdvancedRaidItem CreateItem(int templateId, string name, int durability,
            string modifierId, int modifierPower)
        {
            var item = new AdvancedRaidItem
            {
                TemplateId = templateId,
                Name = name,
                Durability = durability,
                Modifiers = new VersionSyncList<AdvancedRaidModifier>()
            };
            item.Modifiers.Add(new AdvancedRaidModifier { Id = modifierId, Power = modifierPower });
            return item;
        }

        private static string Describe(AdvancedRaidWorld world)
        {
            if (world == null || world.Players == null) return "world not initialized";

            var ids = new List<int>();
            foreach (var pair in world.Players) ids.Add(pair.Key);
            ids.Sort();

            var players = new List<string>();
            foreach (int id in ids)
            {
                AdvancedRaidPlayer player = world.Players[id];
                string weapon = player.Equipped == null ? "none" :
                    $"{player.Equipped.Name}/{player.Equipped.Durability}";
                players.Add($"{id}:{player.Name} HP={player.Health} weapon={weapon} " +
                            $"items={player.Inventory.Count} effects={player.Effects.Count} auras={player.Auras.Count}");
            }

            return $"tick={world.Tick}, phase={world.Phase}, players=[{string.Join("; ", players)}], " +
                   $"events={world.EventLog.Count}";
        }
    }
}
