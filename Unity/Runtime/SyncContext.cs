using System.Collections.Generic;
using System.IO;

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
    /// methods scan the registry by ascending id (ids are assigned pre-order, parent &lt; descendants) and write a
    /// flat list of node records: <c>[byte isFull]</c> then <c>[int id][payload]</c> per node. The consumer applies
    /// with <see cref="Apply"/>; for a full snapshot it drops any registered node the snapshot did not mention.
    /// Reference fields serialize as the referenced node's id; the consumer creates the node on first sight (inline
    /// in the node's <c>__Apply</c>) using the field's static type, so no type tags travel on the wire.
    /// </remarks>
    public class SyncContext
    {
        /// <summary>The registry: id → node. Exposed so generated node code can register / resolve / remove inline.</summary>
        public readonly Dictionary<int, IVersionSync> __Objects = new Dictionary<int, IVersionSync>();

        /// <summary>Next id to hand out (the root deterministically gets 1). Bumped by generated attach code.</summary>
        public int __NextId = 1;

        // Reused across Apply calls so a full snapshot's prune pass allocates nothing.
        private readonly HashSet<int> __seen = new HashSet<int>();
        private readonly List<int> __stale = new List<int>();

        /// <summary>
        /// Full keyframe: writes a <c>[byte 1]</c> marker then every registered node's full record into
        /// <paramref name="writer"/> (scanning by ascending id, so parents precede descendants), and clears all
        /// pending dirty state (a keyframe supersedes it). The caller owns <paramref name="writer"/> / its stream
        /// — ship those bytes and apply on the consumer with <see cref="Apply"/>.
        /// </summary>
        public void CaptureFull(BinaryWriter writer)
        {
            writer.Write((byte)1);   // isFull marker
            for (int id = 1; id < __NextId; id++)
                if (__Objects.TryGetValue(id, out var node))
                {
                    node.__CaptureFull(writer);
                    node.__ClearDirty();
                }
        }

        /// <summary>
        /// Incremental frame: writes a <c>[byte 0]</c> (not-full) marker then one record per dirty node into
        /// <paramref name="writer"/> (scanning by ascending id, so a parent's record precedes the children it must
        /// create on the consumer), clearing each as it goes. Writes just the marker when nothing is dirty (every
        /// mutation since the last capture marks its node dirty — there is no recording flag). Removed object nodes
        /// are not signalled on the wire — ship a periodic full <see cref="CaptureFull"/> keyframe to prune them.
        /// </summary>
        public void CaptureDelta(BinaryWriter writer)
        {
            writer.Write((byte)0);   // not-full marker → consumer updates in place, no prune
            for (int id = 1; id < __NextId; id++)
                if (__Objects.TryGetValue(id, out var node) && node.__IsDirty)
                {
                    node.__CaptureDelta(writer);
                    node.__ClearDirty();
                }
        }

        /// <summary>
        /// Applies a payload from <paramref name="reader"/>'s current position to the end of its stream. Reads the
        /// leading full marker first; for a full snapshot, any registered node the snapshot did not mention is
        /// dropped from <see cref="__Objects"/> afterwards (it was removed on the producer since the last apply).
        /// </summary>
        public void Apply(BinaryReader reader)
        {
            var stream = reader.BaseStream;
            bool full = reader.ReadByte() == 1;
            if (full) __seen.Clear();
            while (stream.Position < stream.Length)
            {
                int id = reader.ReadInt32();
                if (full) __seen.Add(id);
                __Objects[id].__Apply(reader);
            }
            if (full)
            {
                // Drop nodes absent from the full snapshot (removed since the last apply). The root (id 1) is
                // always serialized, so it is always in `__seen` and never pruned.
                __stale.Clear();
                foreach (var id in __Objects.Keys)
                    if (!__seen.Contains(id)) __stale.Add(id);
                for (int i = 0; i < __stale.Count; i++) __Objects.Remove(__stale[i]);
            }
            __seen.Clear();
            __stale.Clear();
        }
    }
}
