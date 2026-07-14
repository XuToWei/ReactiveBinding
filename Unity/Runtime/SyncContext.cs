#nullable disable
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;

namespace ReactiveBinding
{
    /// <summary>
    /// Flat-registry synchronization kernel. Holds the registry state — every <see cref="IVersionSync"/> node keyed
    /// by a stable global id (<see cref="__Objects"/>) and the id allocator (<see cref="__NextId"/>) — plus the
    /// user-facing operations <see cref="CaptureFull"/> / <see cref="CaptureDelta"/> / <see cref="Apply"/>. The
    /// caller owns the output stream: pass a <see cref="BinaryWriter"/> to capture into and a
    /// <see cref="BinaryReader"/> to apply from. All registration / resolve / record (de)serialization is generated
    /// inline on the nodes; the context itself has no per-node behaviour.
    /// </summary>
    /// <remarks>
    /// Model: <b>full snapshot (keyframe) + optional coalesced deltas</b>. Seed the same root on both sides with
    /// <c>root.AttachTo(ctx)</c> (registration only, both deterministically assign the root id 1). Both capture
    /// methods write nodes by ascending id (ids are assigned pre-order, parent &lt; descendants) using the frame
    /// <c>[byte isFull][varuint id + payload ...][varuint 0][varuint tombstoneCount][varuint ids ...]</c>. Full
    /// capture scans the registry; delta capture sorts only ids enlisted on clean-to-dirty transitions. The consumer applies
    /// with <see cref="Apply"/>; for a full snapshot it resets and drops any registered node the snapshot did not
    /// mention. Resetting detaches the stale subtree root, clears version/dirty state, and retains its internal
    /// ownership tree, field values, and container contents for reuse.
    /// Reference fields serialize as the referenced node's id; the consumer creates the node on first sight (inline
    /// in the node's <c>__Apply</c>) using the field's static type, so no type tags travel on the wire.
    /// A producer/consumer pair has one authoritative id allocator. Independent bidirectional writers must use
    /// separate contexts/id namespaces so newly allocated ids cannot collide.
    /// </remarks>
    public class SyncContext
    {
        private sealed class SyncNodeReferenceComparer : IEqualityComparer<IVersionSync>
        {
            public static readonly SyncNodeReferenceComparer Instance = new SyncNodeReferenceComparer();
            public bool Equals(IVersionSync x, IVersionSync y) => ReferenceEquals(x, y);
            public int GetHashCode(IVersionSync obj) => RuntimeHelpers.GetHashCode(obj);
        }

        /// <summary>The registry: id → node. Exposed so generated node code can register / resolve / remove inline.</summary>
        public Dictionary<int, IVersionSync> __Objects = new Dictionary<int, IVersionSync>();

        /// <summary>Next id to hand out (the root deterministically gets 1).</summary>
        public int __NextId = 1;

        /// <summary>Allocates a node id in the supported range 1 through <see cref="int.MaxValue"/> - 1.</summary>
        public int __AllocateId()
        {
            if (__NextId <= 0 || __NextId >= int.MaxValue)
                throw new System.InvalidOperationException("The sync node id space is exhausted or invalid.");

            int id = __NextId;
            __NextId = checked(id + 1);
            return id;
        }

        // Reused scratch state: capture/apply allocate nothing after warm-up. Dirty/tombstone ids are kept in a
        // set for duplicate-safe enlistment and a list for deterministic ascending-id wire order.
        private HashSet<int> __seen = new HashSet<int>();
        private List<int> __stale = new List<int>();
        private List<int> __active = new List<int>();
        private HashSet<int> __dirtySet = new HashSet<int>();
        private List<int> __dirty = new List<int>();
        private HashSet<int> __tombstoneSet = new HashSet<int>();
        private List<int> __tombstones = new List<int>();
        private List<IVersionSync> __written = new List<IVersionSync>();
        private HashSet<IVersionSync> __touchedVersions =
            new HashSet<IVersionSync>(SyncNodeReferenceComparer.Instance);

        private bool __ShouldScanDenseIds()
            => __NextId <= (long)__Objects.Count * 2 + 1;

        private void __CollectActiveIds()
        {
            __active.Clear();
            foreach (var id in __Objects.Keys) __active.Add(id);
            __active.Sort();
        }

        /// <summary>
        /// Enlists a node whose dirty state just transitioned from clean to dirty. Duplicate, zero, negative, and
        /// no-longer-registered ids are ignored. Generated nodes and sync containers call this after setting their
        /// local dirty flag; <see cref="CaptureDelta"/> then visits only enlisted ids.
        /// </summary>
        public void __EnlistDirty(int id)
        {
            if (id <= 0 || !__Objects.ContainsKey(id) || !__dirtySet.Add(id)) return;
            __dirty.Add(id);
        }

        /// <summary>
        /// Queues a removed node id for the next delta. Call this while the old non-zero id is still available,
        /// before resetting/unregistering the node. Tombstones are emitted after all normal node records, so parent
        /// reference changes are applied before the removed consumer subtree is reset.
        /// </summary>
        public void __RecordTombstone(int id)
        {
            if (id <= 0 || !__tombstoneSet.Add(id)) return;
            __tombstones.Add(id);
        }

        /// <summary>
        /// Records <paramref name="node"/> and every sync ancestor in its ownership chain for one coalesced version
        /// update at the end of <see cref="Apply"/>. Generated/container apply code calls this instead of recursively
        /// incrementing versions for every node record.
        /// </summary>
        public void __TouchVersion(IVersionSync node)
        {
            for (IVersion current = node; current != null; current = VersionOwnership.GetParent(current))
                if (current is IVersionSync sync && !__touchedVersions.Add(sync)) break;
        }

        /// <summary>
        /// Releases excess registry capacity without changing ids or <see cref="__NextId"/>. This replaces the
        /// exposed dictionary instance, so callers must not retain an alias to <see cref="__Objects"/> across this call.
        /// </summary>
        public void Compact()
        {
            __Objects = new Dictionary<int, IVersionSync>(__Objects);
        }

        /// <summary>
        /// Releases excess capacity held by reusable capture/apply scratch collections. Pending dirty ids and
        /// tombstones are preserved. Call at a low-frequency maintenance point after a workload peak.
        /// </summary>
        public void TrimScratch()
        {
            __seen = new HashSet<int>(__seen);
            __stale = new List<int>(__stale);
            __active = new List<int>(__active);
            __dirtySet = new HashSet<int>(__dirtySet);
            __dirty = new List<int>(__dirty);
            __tombstoneSet = new HashSet<int>(__tombstoneSet);
            __tombstones = new List<int>(__tombstones);
            __written = new List<IVersionSync>(__written);
            __touchedVersions = new HashSet<IVersionSync>(__touchedVersions, SyncNodeReferenceComparer.Instance);
        }

        /// <summary>
        /// Full keyframe: writes a <c>[byte 1]</c> marker then every registered node's full record into
        /// <paramref name="writer"/> (scanning by ascending id, so parents precede descendants), and clears all
        /// pending dirty state (a keyframe supersedes it). The caller owns <paramref name="writer"/> / its stream
        /// — ship those bytes and apply on the consumer with <see cref="Apply"/>.
        /// </summary>
        public void CaptureFull(BinaryWriter writer)
        {
            writer.Write((byte)1);   // full marker
            __written.Clear();
            if (__ShouldScanDenseIds())
            {
                for (int id = 1; id < __NextId; id++)
                    if (__Objects.TryGetValue(id, out var node))
                    {
                        node.__CaptureFull(writer);
                        __written.Add(node);
                    }
            }
            else
            {
                __CollectActiveIds();
                for (int i = 0; i < __active.Count; i++)
                    if (__Objects.TryGetValue(__active[i], out var node))
                    {
                        node.__CaptureFull(writer);
                        __written.Add(node);
                    }
                __active.Clear();
            }

            // Every frame has an explicit record terminator and trailer. A full snapshot supersedes tombstones.
            SyncWire.WriteVarInt32(writer, 0);
            SyncWire.WriteVarInt32(writer, 0);

            for (int i = 0; i < __written.Count; i++) __written[i].__ClearDirty();
            __written.Clear();
            __dirty.Clear();
            __dirtySet.Clear();
            __tombstones.Clear();
            __tombstoneSet.Clear();
        }

        /// <summary>
        /// Incremental frame: writes a <c>[byte 0]</c> marker then one record per enlisted dirty node into
        /// <paramref name="writer"/> (sorting by ascending id, so a parent's record precedes the children it must
        /// create on the consumer), followed by the zero-id terminator and pending tombstones. Dirty state and
        /// tombstones are committed only after the complete frame has been written.
        /// </summary>
        public void CaptureDelta(BinaryWriter writer)
        {
            writer.Write((byte)0);   // delta marker → consumer updates in place, no full-snapshot prune
            __written.Clear();
            __dirty.Sort();
            for (int i = 0; i < __dirty.Count; i++)
            {
                int id = __dirty[i];
                if (__tombstoneSet.Contains(id)) continue;
                if (__Objects.TryGetValue(id, out var node) && node.__IsDirty)
                {
                    node.__CaptureDelta(writer);
                    __written.Add(node);
                }
            }

            // A zero id ends normal records. Tombstones follow them so parent reference records always apply first.
            SyncWire.WriteVarInt32(writer, 0);
            __tombstones.Sort();
            SyncWire.WriteVarInt32(writer, __tombstones.Count);
            for (int i = 0; i < __tombstones.Count; i++)
                SyncWire.WriteVarInt32(writer, __tombstones[i]);

            for (int i = 0; i < __written.Count; i++) __written[i].__ClearDirty();
            __written.Clear();
            __dirty.Clear();
            __dirtySet.Clear();
            __tombstones.Clear();
            __tombstoneSet.Clear();
        }

        /// <summary>
        /// Applies one self-delimiting frame from <paramref name="reader"/>'s current position. Reads the leading
        /// full marker, positive-id node records through the zero-id terminator, then the tombstone trailer. For a
        /// full snapshot, any registered node the snapshot did not mention is
        /// reset and dropped from <see cref="__Objects"/> afterwards (it was removed on the producer since the last
        /// apply). Applying advances affected nodes' local versions so reactive observers can detect the change,
        /// but never marks outbound sync state dirty. Resetting preserves field values / container contents and
        /// internal child ownership, while clearing the stale subtree root's parent, versions, dirty state, sync
        /// ids, and context so externally held instances can be reused safely.
        /// </summary>
        public void Apply(BinaryReader reader)
        {
            __touchedVersions.Clear();
            bool full = reader.ReadByte() == 1;
            if (full) __seen.Clear();
            while (true)
            {
                int id = SyncWire.ReadVarInt32(reader);
                if (id == 0) break;
                if (id < 0 || id == int.MaxValue)
                    throw new InvalidDataException("Sync node ids must be between 1 and Int32.MaxValue - 1.");
                if (id >= __NextId) __NextId = id + 1;
                if (full) __seen.Add(id);
                var node = __Objects[id];
                node.__Apply(reader);
                node.__ClearDirty();
            }

            // Tombstones are deliberately applied only after all normal records. Parent reference records can
            // therefore detach/replace the old consumer node before its retained subtree is reset and removed.
            int tombstoneCount = SyncWire.ReadVarInt32(reader);
            if (tombstoneCount < 0)
                throw new InvalidDataException("The tombstone count cannot be negative.");
            for (int i = 0; i < tombstoneCount; i++)
            {
                int id = SyncWire.ReadVarInt32(reader);
                if (id <= 0 || id == int.MaxValue)
                    throw new InvalidDataException("Tombstone ids must be between 1 and Int32.MaxValue - 1.");
                if (__Objects.TryGetValue(id, out var node)) node.__Reset();
                __Objects.Remove(id);
            }

            if (full)
            {
                // Drop nodes absent from the full snapshot (removed since the last apply). The root (id 1) is
                // always serialized, so it is always in `__seen` and never pruned.
                __stale.Clear();
                foreach (var id in __Objects.Keys)
                    if (!__seen.Contains(id)) __stale.Add(id);
                for (int i = 0; i < __stale.Count; i++)
                {
                    int id = __stale[i];
                    if (__Objects.TryGetValue(id, out var node))
                        node.__Reset();
                    __Objects.Remove(id);
                }
            }

            if (__touchedVersions.Count != 0)
            {
                int version = VersionCounter.Next();
                foreach (var node in __touchedVersions)
                {
                    int id = node.__SyncId;
                    if (id != 0 && ReferenceEquals(node.__SyncContext, this)
                        && __Objects.TryGetValue(id, out var registered) && ReferenceEquals(registered, node))
                        node.__Version = version;
                }
            }
            __touchedVersions.Clear();
            __seen.Clear();
            __stale.Clear();
            __dirty.Clear();
            __dirtySet.Clear();
            __tombstones.Clear();
            __tombstoneSet.Clear();
        }
    }
}
