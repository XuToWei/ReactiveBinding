#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;

namespace ReactiveBinding
{
    /// <summary>
    /// A set that tracks modifications via a version number AND participates in flat-registry data synchronization
    /// (<see cref="IVersionSync"/>). Use this — not the version-only <see cref="VersionHashSet{T}"/> — for a
    /// <c>[VersionField]</c> set inside an <see cref="IVersionSync"/> class. Elements must be scalar (object
    /// elements are unsupported, VS2001); structural changes are coalesced into a per-frame op log. Standalone
    /// type (not a subclass).
    /// </summary>
    /// <typeparam name="T">The type of elements in the set.</typeparam>
    public class VersionSyncHashSet<T> : ISet<T>, IReadOnlyCollection<T>, IVersionSync
    {
        private readonly HashSet<T> m_Set;
        private int m_Version;

        /// <summary>Creates a new empty VersionSyncHashSet.</summary>
        public VersionSyncHashSet() { m_Set = new HashSet<T>(); }

        /// <summary>Creates a new VersionSyncHashSet with the specified comparer.</summary>
        public VersionSyncHashSet(IEqualityComparer<T> comparer) { m_Set = new HashSet<T>(comparer); }

        /// <summary>Creates a new VersionSyncHashSet containing elements from the specified collection.</summary>
        public VersionSyncHashSet(IEnumerable<T> collection)
        {
            m_Set = new HashSet<T>(collection);
            foreach (var item in m_Set) if (item is IVersion v) v.__Parent = this;
        }

        /// <summary>Creates a new VersionSyncHashSet containing elements from the specified collection with the specified comparer.</summary>
        public VersionSyncHashSet(IEnumerable<T> collection, IEqualityComparer<T> comparer)
        {
            m_Set = new HashSet<T>(collection, comparer);
            foreach (var item in m_Set) if (item is IVersion v) v.__Parent = this;
        }

#if UNITY_2021_2_OR_NEWER || NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
        /// <summary>Creates a new VersionSyncHashSet with the specified initial capacity.</summary>
        public VersionSyncHashSet(int capacity) { m_Set = new HashSet<T>(capacity); }

        /// <summary>Creates a new VersionSyncHashSet with the specified initial capacity and comparer.</summary>
        public VersionSyncHashSet(int capacity, IEqualityComparer<T> comparer) { m_Set = new HashSet<T>(capacity, comparer); }
#endif

        /// <inheritdoc/>
        public int __Version => m_Version;

        /// <inheritdoc/>
        public IVersion __Parent { get; set; }

        /// <inheritdoc/>
        public void __IncrementVersion()
        {
            m_Version = VersionCounter.Next();
            if (__Parent != null) __Parent.__IncrementVersion();
        }

        /// <inheritdoc/>
        public int Count => m_Set.Count;

        /// <inheritdoc/>
        public bool IsReadOnly => false;

        /// <inheritdoc/>
        public bool Add(T item)
        {
            var added = m_Set.Add(item);
            if (added)
            {
                if (item is IVersion v) v.__Parent = this;
                __IncrementVersion(); __LogOp(__OP_ADD, item);
            }
            return added;
        }

        /// <inheritdoc/>
        void ICollection<T>.Add(T item) => Add(item);

        /// <inheritdoc/>
        public void Clear()
        {
            foreach (var item in m_Set) if (item is IVersion v) v.__Parent = null;
            m_Set.Clear();
            __IncrementVersion(); __LogOp(__OP_CLEAR, default);
        }

        /// <inheritdoc/>
        public bool Contains(T item) => m_Set.Contains(item);

        /// <inheritdoc/>
        public void CopyTo(T[] array, int arrayIndex) => m_Set.CopyTo(array, arrayIndex);

        /// <inheritdoc/>
        public void ExceptWith(IEnumerable<T> other)
        {
            var countBefore = m_Set.Count;
            foreach (var item in other)
            {
                if (m_Set.Remove(item)) if (item is IVersion v) v.__Parent = null;
            }
            if (m_Set.Count != countBefore) { __IncrementVersion(); __MarkFull(); }
        }

        /// <inheritdoc/>
        public IEnumerator<T> GetEnumerator() => m_Set.GetEnumerator();

        /// <inheritdoc/>
        IEnumerator IEnumerable.GetEnumerator() => m_Set.GetEnumerator();

        /// <inheritdoc/>
        public void IntersectWith(IEnumerable<T> other)
        {
            var otherSet = other is HashSet<T> hs ? hs : new HashSet<T>(other);
            var countBefore = m_Set.Count;
            m_Set.RemoveWhere(item =>
            {
                if (otherSet.Contains(item)) return false;
                if (item is IVersion v) v.__Parent = null;
                return true;
            });
            if (m_Set.Count != countBefore) { __IncrementVersion(); __MarkFull(); }
        }

        /// <inheritdoc/>
        public bool IsProperSubsetOf(IEnumerable<T> other) => m_Set.IsProperSubsetOf(other);

        /// <inheritdoc/>
        public bool IsProperSupersetOf(IEnumerable<T> other) => m_Set.IsProperSupersetOf(other);

        /// <inheritdoc/>
        public bool IsSubsetOf(IEnumerable<T> other) => m_Set.IsSubsetOf(other);

        /// <inheritdoc/>
        public bool IsSupersetOf(IEnumerable<T> other) => m_Set.IsSupersetOf(other);

        /// <inheritdoc/>
        public bool Overlaps(IEnumerable<T> other) => m_Set.Overlaps(other);

        /// <inheritdoc/>
        public bool Remove(T item)
        {
            var removed = m_Set.Remove(item);
            if (removed)
            {
                if (item is IVersion v) v.__Parent = null;
                __IncrementVersion(); __LogOp(__OP_REMOVE, item);
            }
            return removed;
        }

        /// <summary>Removes all elements that match the conditions defined by the specified predicate.</summary>
        public int RemoveWhere(Predicate<T> match)
        {
            var count = m_Set.RemoveWhere(item =>
            {
                if (!match(item)) return false;
                if (item is IVersion v) v.__Parent = null;
                return true;
            });
            if (count > 0) { __IncrementVersion(); __MarkFull(); }
            return count;
        }

        /// <inheritdoc/>
        public bool SetEquals(IEnumerable<T> other) => m_Set.SetEquals(other);

        /// <inheritdoc/>
        public void SymmetricExceptWith(IEnumerable<T> other)
        {
            var otherSet = other is HashSet<T> hs ? hs : new HashSet<T>(other);
            bool changed = false;
            foreach (var item in otherSet)
            {
                if (m_Set.Remove(item))
                {
                    if (item is IVersion v) v.__Parent = null;
                    changed = true;
                }
                else
                {
                    m_Set.Add(item);
                    if (item is IVersion v) v.__Parent = this;
                    changed = true;
                }
            }
            if (changed) { __IncrementVersion(); __MarkFull(); }
        }

        /// <inheritdoc/>
        public void UnionWith(IEnumerable<T> other)
        {
            var countBefore = m_Set.Count;
            foreach (var item in other)
            {
                if (m_Set.Add(item)) if (item is IVersion v) v.__Parent = this;
            }
            if (m_Set.Count != countBefore) { __IncrementVersion(); __MarkFull(); }
        }

        /// <summary>Sets the capacity to the actual number of elements in the set.</summary>
        public void TrimExcess()
        {
#if UNITY_2021_2_OR_NEWER || NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
            m_Set.TrimExcess();
#endif
        }

        /// <summary>Gets the comparer used to determine equality of values.</summary>
        public IEqualityComparer<T> Comparer => m_Set.Comparer;

        // ===== Synchronization (flat-registry) =====
        /// <inheritdoc/>
        public int __SyncId { get; set; }
        /// <inheritdoc/>
        public SyncContext __SyncContext { get; set; }

        private System.Action<System.IO.BinaryWriter, T> __wElem;
        private System.Func<System.IO.BinaryReader, T> __rElem;

        /// <summary>Owner injects element write/read delegates when the set is attached.</summary>
        public void __InitSync(System.Action<System.IO.BinaryWriter, T> wElem, System.Func<System.IO.BinaryReader, T> rElem)
        {
            __wElem = wElem;
            __rElem = rElem;
        }

        /// <inheritdoc/>
        public void AttachTo(SyncContext ctx)   // scalar elements: no children to recurse
        {
            __SyncContext = ctx;
            if (__SyncId == 0) { __SyncId = ctx.__NextId++; ctx.__Objects[__SyncId] = this; }
        }

        /// <inheritdoc/>
        public void __SyncChildren(SyncOp op) { }   // scalar elements only

        // ----- Incremental recording: per-frame op log, coalesced into one record by CaptureDelta. -----
        private const byte __OP_ADD = 1, __OP_REMOVE = 2, __OP_CLEAR = 3;
        private bool __inDirty;
        private bool __fullDirty;
        private System.Collections.Generic.List<(byte op, T elem)> __ops;

        private void __EnsureDirty()
        {
            if (__SyncContext != null) __inDirty = true;
        }

        /// <inheritdoc/>
        public bool __IsDirty => __inDirty;

        /// <inheritdoc/>
        public void __MarkAllDirty() { __EnsureDirty(); __fullDirty = true; }

        /// <inheritdoc/>
        public void __ClearDirty() { __inDirty = false; __fullDirty = false; if (__ops != null) __ops.Clear(); }

        /// <inheritdoc/>
        public void __Reset()
        {
            // Scalar elements (object elements are unsupported, VS2001) — nothing to recurse.
            if (__SyncContext != null) __SyncContext.__Objects.Remove(__SyncId);
            __SyncId = 0; __SyncContext = null;
            __inDirty = false; __fullDirty = false; if (__ops != null) __ops.Clear();
            m_Version = 0; __Parent = null;   // keeps contents; detaches for reuse
        }

        private void __MarkFull() { __EnsureDirty(); __fullDirty = true; }

        private void __LogOp(byte op, T elem)
        {
            __EnsureDirty();
            if (!__inDirty) return;   // not attached
            if (__ops == null) __ops = new System.Collections.Generic.List<(byte, T)>();
            __ops.Add((op, elem));
        }

        /// <summary>Full record (keyframe): [id][1][count][elements].</summary>
        public void __CaptureFull(System.IO.BinaryWriter writer)
        {
            writer.Write(__SyncId);
            writer.Write((byte)1);
            writer.Write(m_Set.Count);
            foreach (var e in m_Set) __wElem(writer, e);
        }

        /// <summary>Incremental record: [id][0][opCount][ops] — or a full [id][1][count][elements] when fully dirty.</summary>
        public void __CaptureDelta(System.IO.BinaryWriter writer)
        {
            writer.Write(__SyncId);
            if (__fullDirty || __ops == null || __ops.Count == 0)
            {
                writer.Write((byte)1);
                writer.Write(m_Set.Count);
                foreach (var e in m_Set) __wElem(writer, e);
                return;
            }
            writer.Write((byte)0);
            writer.Write(__ops.Count);
            for (int i = 0; i < __ops.Count; i++)
            {
                var e = __ops[i];
                writer.Write(e.op);
                switch (e.op)
                {
                    case __OP_ADD: __wElem(writer, e.elem); break;
                    case __OP_REMOVE: __wElem(writer, e.elem); break;
                    case __OP_CLEAR: break;
                }
            }
        }

        /// <summary>Applies a node record: a full [1][count][elements] rebuild, or a [0][opCount][ops] replay. Silently.</summary>
        public void __Apply(System.IO.BinaryReader reader)
        {
            if (reader.ReadByte() == 1)
            {
                m_Set.Clear();
                int n = reader.ReadInt32();
                for (int i = 0; i < n; i++) { m_Set.Add(__rElem(reader)); }
                return;
            }
            int ops = reader.ReadInt32();
            for (int k = 0; k < ops; k++)
            {
                byte op = reader.ReadByte();
                switch (op)
                {
                    case __OP_ADD: m_Set.Add(__rElem(reader)); break;
                    case __OP_REMOVE: m_Set.Remove(__rElem(reader)); break;
                    case __OP_CLEAR: m_Set.Clear(); break;
                }
            }
        }
    }
}
