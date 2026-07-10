#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;

namespace ReactiveBinding
{
    /// <summary>
    /// A set that tracks modifications via a version number AND participates in flat-registry data synchronization
    /// (<see cref="IVersionSync"/>). Use this — not the version-only <see cref="VersionHashSet{T}"/> — for a
    /// <c>[VersionField]</c> set inside an <see cref="IVersionSync"/> class. Elements may be scalar or sync objects;
    /// structural changes are coalesced into a per-frame op log. Standalone type (not a subclass).
    /// </summary>
    /// <typeparam name="T">The type of elements in the set.</typeparam>
    public class VersionSyncHashSet<T> : ISet<T>, IReadOnlyCollection<T>, IVersionSync
    {
        private readonly HashSet<T> m_Set;
        private int m_Version;

        /// <summary>Creates a new empty VersionSyncHashSet.</summary>
        public VersionSyncHashSet() { m_Set = new HashSet<T>(); }

        /// <summary>Creates a new VersionSyncHashSet with the default comparer.</summary>
        /// <exception cref="NotSupportedException">Thrown when a custom comparer is supplied.</exception>
        public VersionSyncHashSet(IEqualityComparer<T> comparer) { m_Set = new HashSet<T>(__RequireDefaultComparer(comparer)); }

        /// <summary>Creates a new VersionSyncHashSet containing elements from the specified collection.</summary>
        public VersionSyncHashSet(IEnumerable<T> collection)
        {
            m_Set = new HashSet<T>(collection);
            VersionOwnership.EnsureCanAttachAll(this, m_Set);
            foreach (var item in m_Set) if (item is IVersion v) v.__Parent = this;
        }

        /// <summary>Creates a new VersionSyncHashSet containing elements from the specified collection with the specified comparer.</summary>
        public VersionSyncHashSet(IEnumerable<T> collection, IEqualityComparer<T> comparer)
        {
            m_Set = new HashSet<T>(collection, __RequireDefaultComparer(comparer));
            VersionOwnership.EnsureCanAttachAll(this, m_Set);
            foreach (var item in m_Set) if (item is IVersion v) v.__Parent = this;
        }

#if UNITY_2021_2_OR_NEWER || NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
        /// <summary>Creates a new VersionSyncHashSet with the specified initial capacity.</summary>
        public VersionSyncHashSet(int capacity) { m_Set = new HashSet<T>(capacity); }

        /// <summary>Creates a new VersionSyncHashSet with the specified initial capacity and comparer.</summary>
        public VersionSyncHashSet(int capacity, IEqualityComparer<T> comparer) { m_Set = new HashSet<T>(capacity, __RequireDefaultComparer(comparer)); }
#endif

        private static IEqualityComparer<T> __RequireDefaultComparer(IEqualityComparer<T> comparer)
        {
            if (comparer == null || ReferenceEquals(comparer, EqualityComparer<T>.Default)) return comparer;
            throw new NotSupportedException(
                "VersionSyncHashSet does not support custom element comparers because both sync peers must use " +
                "identical element equality. Use the default comparer or a non-sync VersionHashSet.");
        }

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

        private void __AttachElement(T item)
        {
            if (item is IVersion v) v.__Parent = this;
            if (__SyncContext != null && __objectElems && item is IVersionSync s) __Recurse(SyncOp.Attach, s);
        }

        private void __DetachElement(T item)
        {
            if (item is IVersion v) v.__Parent = null;
            if (__SyncContext != null && __objectElems && item is IVersionSync s) __Recurse(SyncOp.Unregister, s);
        }

        private static void __ClearElementParent(T item)
        {
            if (item is IVersion v) v.__Parent = null;
        }

        private bool __TryGetStoredValue(T equalValue, out T storedValue)
        {
            foreach (var candidate in m_Set)
            {
                if (!m_Set.Comparer.Equals(candidate, equalValue)) continue;
                storedValue = candidate;
                return true;
            }
            storedValue = default;
            return false;
        }

        /// <inheritdoc/>
        public int Count => m_Set.Count;

        /// <inheritdoc/>
        public bool IsReadOnly => false;

        /// <inheritdoc/>
        public bool Add(T item)
        {
            if (m_Set.Contains(item)) return false;
            if (item is IVersion child) VersionOwnership.EnsureCanAttach(this, child);
            var added = m_Set.Add(item);
            if (added)
            {
                __AttachElement(item);
                __IncrementVersion(); __LogOp(__OP_ADD, item);
            }
            return added;
        }

        /// <inheritdoc/>
        void ICollection<T>.Add(T item) => Add(item);

        /// <inheritdoc/>
        public void Clear()
        {
            if (m_Set.Count == 0) return;
            var removed = new List<T>(m_Set);
            m_Set.Clear();
            foreach (var item in removed) __DetachElement(item);
            __IncrementVersion();
            if (__objectElems) __MarkFull(); else __LogOp(__OP_CLEAR, default);
        }

        /// <inheritdoc/>
        public bool Contains(T item) => m_Set.Contains(item);

        /// <inheritdoc/>
        public void CopyTo(T[] array, int arrayIndex) => m_Set.CopyTo(array, arrayIndex);

        /// <inheritdoc/>
        public void ExceptWith(IEnumerable<T> other)
        {
            var otherSet = new HashSet<T>(other, m_Set.Comparer);
            var removed = new List<T>();
            foreach (var stored in m_Set)
            {
                if (otherSet.Contains(stored)) removed.Add(stored);
            }
            if (removed.Count == 0) return;
            foreach (var stored in removed)
            {
                m_Set.Remove(stored);
                __DetachElement(stored);
            }
            __IncrementVersion(); __MarkFull();
        }

        /// <inheritdoc/>
        public IEnumerator<T> GetEnumerator() => m_Set.GetEnumerator();

        /// <inheritdoc/>
        IEnumerator IEnumerable.GetEnumerator() => m_Set.GetEnumerator();

        /// <inheritdoc/>
        public void IntersectWith(IEnumerable<T> other)
        {
            var otherSet = new HashSet<T>(other, m_Set.Comparer);
            var countBefore = m_Set.Count;
            m_Set.RemoveWhere(item =>
            {
                if (otherSet.Contains(item)) return false;
                __DetachElement(item);
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
            if (!__TryGetStoredValue(item, out var storedItem)) return false;
            m_Set.Remove(storedItem);
            __DetachElement(storedItem);
            __IncrementVersion();
            if (__objectElems) __MarkFull(); else __LogOp(__OP_REMOVE, storedItem);
            return true;
        }

        /// <summary>Removes all elements that match the conditions defined by the specified predicate.</summary>
        public int RemoveWhere(Predicate<T> match)
        {
            if (match == null) throw new ArgumentNullException(nameof(match));
            var removed = new List<T>();
            foreach (var item in m_Set)
            {
                if (match(item)) removed.Add(item);
            }
            if (removed.Count == 0) return 0;
            foreach (var item in removed)
            {
                m_Set.Remove(item);
                __DetachElement(item);
            }
            __IncrementVersion(); __MarkFull();
            return removed.Count;
        }

        /// <inheritdoc/>
        public bool SetEquals(IEnumerable<T> other) => m_Set.SetEquals(other);

        /// <inheritdoc/>
        public void SymmetricExceptWith(IEnumerable<T> other)
        {
            var otherSet = new HashSet<T>(other, m_Set.Comparer);
            var removed = new List<T>();
            var added = new List<T>();
            foreach (var item in otherSet)
            {
                if (__TryGetStoredValue(item, out var stored)) removed.Add(stored);
                else added.Add(item);
            }
            VersionOwnership.EnsureCanAttachAll(this, added);
            if (removed.Count == 0 && added.Count == 0) return;
            foreach (var item in removed)
            {
                m_Set.Remove(item);
                __DetachElement(item);
            }
            foreach (var item in added)
            {
                m_Set.Add(item);
                __AttachElement(item);
            }
            __IncrementVersion(); __MarkFull();
        }

        /// <inheritdoc/>
        public void UnionWith(IEnumerable<T> other)
        {
            var incoming = new HashSet<T>(other, m_Set.Comparer);
            var added = new List<T>();
            foreach (var item in incoming)
            {
                if (!m_Set.Contains(item)) added.Add(item);
            }
            if (added.Count == 0) return;
            VersionOwnership.EnsureCanAttachAll(this, added);
            foreach (var item in added)
            {
                m_Set.Add(item);
                __AttachElement(item);
            }
            __IncrementVersion(); __MarkFull();
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

        private bool __objectElems;
        private System.Action<System.IO.BinaryWriter, T> __wElem;
        private System.Func<System.IO.BinaryReader, T> __rElem;
        private System.Func<IVersionSync> __newElem;

        /// <summary>Owner injects element write/read delegates when the set has scalar elements.</summary>
        public void __InitSync(System.Action<System.IO.BinaryWriter, T> wElem, System.Func<System.IO.BinaryReader, T> rElem)
        {
            __wElem = wElem;
            __rElem = rElem;
            __objectElems = false;
        }

        /// <summary>Owner injects an element factory when set elements are sync objects.</summary>
        public void __InitSync(System.Func<IVersionSync> newElem)
        {
            __newElem = newElem;
            __objectElems = true;
        }

        private void __WriteElem(System.IO.BinaryWriter writer, T item)
        {
            if (__objectElems) writer.Write(item == null ? 0 : ((IVersionSync)(object)item).__SyncId);
            else __wElem(writer, item);
        }

        private T __ReadElem(System.IO.BinaryReader reader)
        {
            if (!__objectElems) return __rElem(reader);
            int __id = reader.ReadInt32();
            if (__id == 0) return default;
            if (__SyncContext.__Objects.TryGetValue(__id, out var __n)) return (T)__n;
            var __e = __newElem(); __e.__SyncId = __id; __e.__SyncContext = __SyncContext; __SyncContext.__Objects[__id] = __e;
            return (T)__e;
        }

        // Subtree recursion driver: Attach assigns ids / registers (and marks the new node all-dirty so it
        // flushes in full); Unregister fully resets the leaving node.
        private void __Recurse(SyncOp op, IVersionSync child)
        {
            switch (op)
            {
                case SyncOp.Attach:
                    if (child.__SyncId == 0)
                    {
                        child.__SyncId = __SyncContext.__NextId++;
                        child.__SyncContext = __SyncContext;
                        __SyncContext.__Objects[child.__SyncId] = child;
                        child.__MarkAllDirty();
                        child.__SyncChildren(SyncOp.Attach);
                    }
                    else if (!ReferenceEquals(child.__SyncContext, __SyncContext)
                        || !__SyncContext.__Objects.TryGetValue(child.__SyncId, out var registered)
                        || !ReferenceEquals(registered, child))
                    {
                        throw new InvalidOperationException("The IVersionSync node is already attached to another SyncContext.");
                    }
                    break;
                case SyncOp.Unregister:
                    child.__Reset();
                    break;
            }
        }

        /// <inheritdoc/>
        public void AttachTo(SyncContext ctx)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            if (__Parent != null)
                throw new InvalidOperationException("A child node cannot be attached as a SyncContext root.");
            if (__SyncContext != null)
            {
                if (ReferenceEquals(__SyncContext, ctx)
                    && __SyncId != 0
                    && ctx.__Objects.TryGetValue(__SyncId, out var registered)
                    && ReferenceEquals(registered, this)) return;
                throw new InvalidOperationException("This IVersionSync node is already attached to another SyncContext.");
            }
            __SyncContext = ctx;
            __Recurse(SyncOp.Attach, this);
        }

        /// <inheritdoc/>
        public void __SyncChildren(SyncOp op)
        {
            if (!__objectElems) return;
            foreach (var item in m_Set)
                if (item is IVersionSync s) __Recurse(op, s);
        }

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
        public void __MarkAllDirty() { __MarkFull(); }

        /// <inheritdoc/>
        public void __ClearDirty() { __inDirty = false; __fullDirty = false; if (__ops != null) __ops.Clear(); }

        /// <inheritdoc/>
        public void __Reset()
        {
            foreach (var item in m_Set)
                if (item is IVersion v)
                {
                    v.__Reset();
                    v.__Parent = this;
                }
            if (__SyncContext != null) __SyncContext.__Objects.Remove(__SyncId);
            __SyncId = 0; __SyncContext = null;
            __inDirty = false; __fullDirty = false; if (__ops != null) __ops.Clear();
            m_Version = 0; __Parent = null;   // keeps contents; detaches for reuse
        }

        private void __MarkFull()
        {
            __EnsureDirty();
            __fullDirty = true;
            if (__ops != null) __ops.Clear();
        }

        private void __LogOp(byte op, T elem)
        {
            __EnsureDirty();
            if (!__inDirty) return;   // not attached
            if (__fullDirty) return;
            if (__ops == null) __ops = new System.Collections.Generic.List<(byte, T)>();
            __ops.Add((op, elem));
        }

        /// <summary>Full record (keyframe): [id][1][count][elements].</summary>
        public void __CaptureFull(System.IO.BinaryWriter writer)
        {
            writer.Write(__SyncId);
            writer.Write((byte)1);
            writer.Write(m_Set.Count);
            foreach (var e in m_Set) __WriteElem(writer, e);
        }

        /// <summary>Incremental record: [id][0][opCount][ops] — or a full [id][1][count][elements] when fully dirty.</summary>
        public void __CaptureDelta(System.IO.BinaryWriter writer)
        {
            writer.Write(__SyncId);
            if (__fullDirty || __ops == null || __ops.Count == 0)
            {
                writer.Write((byte)1);
                writer.Write(m_Set.Count);
                foreach (var e in m_Set) __WriteElem(writer, e);
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
                    case __OP_ADD: __WriteElem(writer, e.elem); break;
                    case __OP_REMOVE: __WriteElem(writer, e.elem); break;
                    case __OP_CLEAR: break;
                }
            }
        }

        /// <summary>Applies a node record and advances the local version without marking outbound sync state dirty.</summary>
        public void __Apply(System.IO.BinaryReader reader)
        {
            if (reader.ReadByte() == 1)
            {
                foreach (var e in m_Set) __ClearElementParent(e);
                m_Set.Clear();
                int n = reader.ReadInt32();
                for (int i = 0; i < n; i++) { var e = __ReadElem(reader); m_Set.Add(e); if (e is IVersion v) v.__Parent = this; }
                __IncrementVersion();
                return;
            }
            int ops = reader.ReadInt32();
            for (int k = 0; k < ops; k++)
            {
                byte op = reader.ReadByte();
                switch (op)
                {
                    case __OP_ADD: { var e = __ReadElem(reader); m_Set.Add(e); if (e is IVersion v) v.__Parent = this; break; }
                    case __OP_REMOVE: { var e = __ReadElem(reader); if (__TryGetStoredValue(e, out var stored)) { m_Set.Remove(stored); __ClearElementParent(stored); } break; }
                    case __OP_CLEAR: { foreach (var e in m_Set) __ClearElementParent(e); m_Set.Clear(); break; }
                }
            }
            __IncrementVersion();
        }
    }
}
