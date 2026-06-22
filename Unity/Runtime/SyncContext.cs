#nullable disable
using System.Collections.Generic;
using System.IO;

namespace ReactiveBinding
{
    /// <summary>
    /// Flat-registry synchronization kernel. Holds the registry state — every <see cref="IVersionSync"/> node
    /// keyed by a stable global id (<see cref="__Objects"/>), the id allocator (<see cref="__NextId"/>), and the
    /// owned record stream (<see cref="__Writer"/>) — plus the two user-facing operations <see cref="Commit"/>
    /// and <see cref="Apply"/>. All registration / removal / resolve / subtree recursion is generated inline on
    /// the nodes (via their <c>AttachTo</c> / <c>__SyncChildren</c> / <c>__Commit</c> / <c>__Apply</c>), driving
    /// this state directly — the context itself has no per-node behaviour.
    /// </summary>
    /// <remarks>
    /// Model: <b>direct-write append-only log</b>. Seed the same root on both sides with <c>root.AttachTo(ctx)</c>
    /// (registration only, both deterministically assign the root id 1). Every mutation on the producer writes its
    /// record straight into <see cref="__Writer"/> the moment it happens, appending to a single never-reallocated
    /// stream. <see cref="Commit"/> advances a cursor and hands back that same stream positioned at the records
    /// written since the last call: the first commit (after attaching an empty root) is the full state, each later
    /// commit is the delta. Consume the returned stream (read it / ship its bytes) before the next mutation, then
    /// apply on the consumer with <see cref="Apply"/>.
    /// The wire is a flat list of records read until EOF: <c>[0][int id][payload]</c> (a node record — the
    /// node's <c>__Apply</c> reads one unit) or <c>[1][int id]</c> (a removal). Reference fields serialize as
    /// the referenced node's id; the consumer creates the node on first sight (inline in the node's
    /// <c>__Apply</c>) using the field's static type, so no type tags travel on the wire.
    /// </remarks>
    public class SyncContext
    {
        /// <summary>The registry: id → node. Exposed so generated node code can register / resolve / remove inline.</summary>
        public readonly Dictionary<int, IVersionSync> __Objects = new Dictionary<int, IVersionSync>();

        /// <summary>Next id to hand out (the root deterministically gets 1). Bumped by generated attach code.</summary>
        public int __NextId = 1;

        /// <summary>The record buffer every mutation writes into (over its own MemoryStream). Exposed so generated node code writes payloads directly.</summary>
        public BinaryWriter __Writer { get; } = new BinaryWriter(new MemoryStream());

        /// <summary>Stream length already handed out by previous <see cref="Commit"/> calls — the start of the next delta.</summary>
        private long __committed;

        /// <summary>
        /// Returns the writer's own stream (never a fresh one) positioned at the records written since the last
        /// call — the first commit (after attaching an empty root) is the full state, each later commit is the
        /// delta. Read from its current position to the end (e.g. wrap it in a <see cref="BinaryReader"/> and pass
        /// to <see cref="Apply"/>, or ship those bytes over a transport). Consume the stream before the next
        /// mutation, since the writer keeps appending to it.
        /// </summary>
        public MemoryStream Commit()
        {
            __Writer.Flush();
            var stream = (MemoryStream)__Writer.BaseStream;
            long start = __committed;
            __committed = stream.Length;   // everything up to here has now been handed out
            stream.Position = start;       // expose only the records written since the last commit
            return stream;
        }

        /// <summary>Applies a payload (full or delta): reads records from <paramref name="reader"/>'s current position until the end of its stream.</summary>
        public void Apply(BinaryReader reader)
        {
            var stream = reader.BaseStream;
            while (stream.Position < stream.Length)
            {
                byte tag = reader.ReadByte();
                if (tag == 1)
                {
                    __Objects.Remove(reader.ReadInt32());
                }
                else
                {
                    int id = reader.ReadInt32();
                    __Objects[id].__Apply(reader);
                }
            }
        }
    }
}
