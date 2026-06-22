using System.IO;

namespace ReactiveBinding
{
    /// <summary>
    /// The operation recursed over a node's sync children via <see cref="IVersionSync.__SyncChildren"/> — the
    /// generated inline handler applies it to each child and recurses.
    /// </summary>
    public enum SyncOp : byte
    {
        /// <summary>Register the child and its subtree (assign ids).</summary>
        Attach,
        /// <summary>Unregister the child and its subtree (emit removal records).</summary>
        Unregister,
        /// <summary>Write the child's full subtree into the recorder.</summary>
        WriteSubtree,
    }

    /// <summary>
    /// A node that participates in flat-registry synchronization. Declare a <see cref="VersionFieldAttribute"/>
    /// class as <c>: IVersionSync</c> to opt the whole class into sync — the generator then implements this
    /// interface and syncs every <c>[VersionField]</c>. The Version containers also implement it.
    /// </summary>
    /// <remarks>
    /// Model: <b>direct-write</b>. Every syncable node is registered in a <see cref="SyncContext"/> under a
    /// stable global <see cref="__SyncId"/>. A mutation (scalar setter, reference assignment, container op) writes
    /// its record straight into the context's stream the moment it happens — there is no dirty set and no
    /// deferred serialization. The wire is a flat list of self-describing records read until EOF:
    /// <list type="bullet">
    /// <item><c>[0][int id][payload]</c> — a node record; the node's <see cref="__Apply"/> reads one unit
    /// (an object reads one <c>[slot][payload]</c> field; a container reads one mode/op).</item>
    /// <item><c>[1][int id]</c> — a removal; the consumer drops the id.</item>
    /// </list>
    /// Object/container reference fields serialize as the referenced node's <see cref="__SyncId"/> (0 = null);
    /// the consumer creates the node on first sight via <see cref="SyncContext.Resolve{T}"/> using the field's
    /// static type (no type tags on the wire). A node's id is always less than its descendants' (ids are
    /// assigned pre-order), so a parent's reference record is read before the referenced node's own records.
    ///
    /// A node carries its own sync behaviour (the <see cref="SyncContext"/> is just registry state): it
    /// serializes/applies its own state (<see cref="__Commit"/> / <see cref="__Apply"/>), recurses an
    /// operation over its direct children (<see cref="__SyncChildren"/>), and registers itself + its subtree
    /// (<see cref="AttachTo"/>). The generator emits these inline against the context's exposed
    /// <see cref="SyncContext.__Objects"/> / <see cref="SyncContext.__NextId"/> / <see cref="SyncContext.__Writer"/>.
    /// </remarks>
    public interface IVersionSync : IVersion
    {
        /// <summary>Stable id assigned during attach (0 = not registered).</summary>
        int __SyncId { get; set; }

        /// <summary>The context this node is registered in, or null when detached.</summary>
        SyncContext __SyncContext { get; set; }

        /// <summary>Registers this node and its entire sync subtree into <paramref name="ctx"/> (seeding entry; no writing).</summary>
        void AttachTo(SyncContext ctx);

        /// <summary>Writes this node's full state (one or more records) into <see cref="SyncContext"/>'s recorder. Not recursive.</summary>
        void __Commit();

        /// <summary>Applies a single node record from <paramref name="reader"/> (the <c>[0][id]</c> header is already consumed). Mutates silently.</summary>
        void __Apply(System.IO.BinaryReader reader);

        /// <summary>
        /// Recurses <paramref name="op"/> over each direct sync child (reference fields / object container
        /// elements). The per-node register / unregister / write-subtree logic is generated inline.
        /// </summary>
        void __SyncChildren(SyncOp op);
    }
}
