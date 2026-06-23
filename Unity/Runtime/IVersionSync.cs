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
        /// <summary>Unregister the child and its subtree (drop their registry entries; no writing).</summary>
        Unregister,
    }

    /// <summary>
    /// A node that participates in flat-registry synchronization. Declare a <see cref="VersionFieldAttribute"/>
    /// class as <c>: IVersionSync</c> to opt the whole class into sync — the generator then implements this
    /// interface and syncs every <c>[VersionField]</c>. The Version containers also implement it.
    /// </summary>
    /// <remarks>
    /// Model: <b>full snapshot (keyframe) + optional coalesced deltas</b>. Every syncable node is registered in a
    /// <see cref="SyncContext"/> under a stable global <see cref="__SyncId"/>. <see cref="SyncContext.CaptureFull"/>
    /// writes every node's full record (a self-contained keyframe); a mutation marks its node dirty and
    /// <see cref="SyncContext.CaptureDelta"/> writes one record per dirty node. Both share a <c>[byte isFull]</c>
    /// marker followed by a flat list of node records read until EOF:
    /// <c>[int id][payload]</c> — <see cref="__Apply"/> reads one node (an object reads <c>[mask][changed
    /// payloads]</c>; a container reads <c>[full]</c> then its full contents or an op log). When the marker is full,
    /// the consumer drops any registered node the snapshot did not mention. Object/container reference fields
    /// serialize as the referenced node's <see cref="__SyncId"/> (0 = null); the consumer creates the node on first
    /// sight inline in <see cref="__Apply"/> (looked up in <see cref="SyncContext.__Objects"/>, else <c>new</c>)
    /// using the field's static type (no type tags on the wire). A node's id is always less than its descendants'
    /// (ids assigned pre-order) and the dirty set preserves parent-before-child order, so a parent's reference
    /// record is read before the referenced node's own records.
    ///
    /// A node carries its own sync behaviour (the <see cref="SyncContext"/> is just registry state): it
    /// serializes/applies its own state (<see cref="__CaptureFull"/> / <see cref="__CaptureDelta"/> / <see cref="__Apply"/>),
    /// tracks/clears its change set (<see cref="__MarkAllDirty"/> / <see cref="__ClearDirty"/>), recurses an
    /// operation over its direct children (<see cref="__SyncChildren"/>), and registers itself + its subtree
    /// (<see cref="AttachTo"/>). The generator emits these inline against the context's exposed
    /// <see cref="SyncContext.__Objects"/> / <see cref="SyncContext.__NextId"/>, writing into the caller-supplied
    /// <see cref="System.IO.BinaryWriter"/>.
    /// </remarks>
    public interface IVersionSync : IVersion
    {
        /// <summary>Stable id assigned during attach (0 = not registered).</summary>
        int __SyncId { get; set; }

        /// <summary>The context this node is registered in, or null when detached.</summary>
        SyncContext __SyncContext { get; set; }

        /// <summary>Registers this node and its entire sync subtree into <paramref name="ctx"/> (seeding entry; no writing).</summary>
        void AttachTo(SyncContext ctx);

        /// <summary>
        /// Writes this node's <b>full</b> record into <paramref name="writer"/> (keyframe): an object node writes
        /// <c>[id][full-mask][all payloads]</c>; a container writes <c>[id][1][count][elems]</c>. Not recursive —
        /// <see cref="SyncContext.CaptureFull"/> calls it on every registered node by ascending id.
        /// </summary>
        void __CaptureFull(System.IO.BinaryWriter writer);

        /// <summary>
        /// Writes this node's <b>changed</b> record into <paramref name="writer"/> during an incremental flush: an
        /// object node writes <c>[id][dirty-mask][changed payloads]</c>; a container writes <c>[id][0][op log]</c> (or
        /// <c>[id][1][count][elems]</c> when fully dirty). Called once per dirty node by <see cref="SyncContext.CaptureDelta"/>.
        /// </summary>
        void __CaptureDelta(System.IO.BinaryWriter writer);

        /// <summary>True when this node has pending changes to flush (a non-empty field mask / container op set).</summary>
        bool __IsDirty { get; }

        /// <summary>Resets this node's pending dirty state (mask / container op log). Called after a flush or commit.</summary>
        void __ClearDirty();

        /// <summary>
        /// Marks this whole node dirty (object: all field bits; container: full contents), so the next
        /// <see cref="SyncContext.CaptureDelta"/> flushes it in full. Used when a freshly attached subtree must sync.
        /// </summary>
        void __MarkAllDirty();

        /// <summary>
        /// Applies one node record from <paramref name="reader"/> (the leading <c>[id]</c> header is already consumed):
        /// an object node reads <c>[mask][payloads]</c>; a container reads <c>[full]</c> then full contents or op log.
        /// Mutates silently.
        /// </summary>
        void __Apply(System.IO.BinaryReader reader);

        /// <summary>
        /// Recurses <paramref name="op"/> over each direct sync child (reference fields / object container
        /// elements). The per-node register / unregister / write-subtree logic is generated inline.
        /// </summary>
        void __SyncChildren(SyncOp op);
    }
}
