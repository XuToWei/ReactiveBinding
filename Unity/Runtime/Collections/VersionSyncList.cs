#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;

namespace ReactiveBinding
{
    /// <summary>
    /// A list that tracks modifications via a version number AND participates in flat-registry data synchronization
    /// (<see cref="IVersionSync"/>). Use this — not the version-only <see cref="VersionList{T}"/> — for a
    /// <c>[VersionField]</c> container inside an <see cref="IVersionSync"/> class. Scalar elements serialize inline
    /// via owner-injected delegates; object elements (<c>T</c> is an <see cref="IVersionSync"/> type) sync as
    /// referenced registry nodes. Structural changes are coalesced into a per-frame op log. This is a standalone
    /// type (not a subclass of <see cref="VersionList{T}"/>).
    /// </summary>
    /// <typeparam name="T">The type of elements in the list.</typeparam>
    public class VersionSyncList<T> : IList<T>, IReadOnlyList<T>, IVersionSync
    {
        private readonly List<T> m_List;
        private int m_Version;

        /// <summary>Creates a new empty VersionSyncList.</summary>
        public VersionSyncList() { m_List = new List<T>(); }

        /// <summary>Creates a new VersionSyncList with the specified initial capacity.</summary>
        public VersionSyncList(int capacity) { m_List = new List<T>(capacity); }

        /// <summary>Creates a new VersionSyncList containing elements from the specified collection.</summary>
        public VersionSyncList(IEnumerable<T> collection)
        {
            m_List = new List<T>(collection);
            foreach (var item in m_List)
            {
                if (item is IVersion v) v.__Parent = this;
                if (__SyncContext != null && item is IVersionSync s) __Recurse(SyncOp.Attach, s);
            }
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

        /// <inheritdoc/>
        public int Count => m_List.Count;

        /// <inheritdoc/>
        public bool IsReadOnly => false;

        /// <inheritdoc/>
        public T this[int index]
        {
            get => m_List[index];
            set
            {
                var oldItem = m_List[index];
                if (EqualityComparer<T>.Default.Equals(oldItem, value)) return;
                if (oldItem is IVersion ov) ov.__Parent = null;
                if (__SyncContext != null && oldItem is IVersionSync os) __Recurse(SyncOp.Unregister, os);
                m_List[index] = value;
                if (value is IVersion nv) nv.__Parent = this;
                if (__SyncContext != null && value is IVersionSync ns) __Recurse(SyncOp.Attach, ns);
                __IncrementVersion(); __LogOp(__OP_SET, index, value);
            }
        }

        /// <inheritdoc/>
        public void Add(T item)
        {
            m_List.Add(item);
            if (item is IVersion v) v.__Parent = this;
            if (__SyncContext != null && item is IVersionSync s) __Recurse(SyncOp.Attach, s);
            __IncrementVersion(); __LogOp(__OP_ADD, 0, item);
        }

        /// <summary>Adds the elements of the specified collection to the end of the list.</summary>
        public void AddRange(IEnumerable<T> collection)
        {
            var items = collection is ICollection<T> c ? c : new List<T>(collection);
            m_List.AddRange(items);
            foreach (var item in items)
            {
                if (item is IVersion v) v.__Parent = this;
                if (__SyncContext != null && item is IVersionSync s) __Recurse(SyncOp.Attach, s);
            }
            __IncrementVersion(); __MarkFull();
        }

        /// <inheritdoc/>
        public void Clear()
        {
            foreach (var item in m_List)
            {
                if (item is IVersion v) v.__Parent = null;
                if (__SyncContext != null && item is IVersionSync s) __Recurse(SyncOp.Unregister, s);
            }
            m_List.Clear();
            __IncrementVersion(); __LogOp(__OP_CLEAR, 0, default);
        }

        /// <inheritdoc/>
        public bool Contains(T item) => m_List.Contains(item);

        /// <inheritdoc/>
        public void CopyTo(T[] array, int arrayIndex) => m_List.CopyTo(array, arrayIndex);

        /// <inheritdoc/>
        public IEnumerator<T> GetEnumerator() => m_List.GetEnumerator();

        /// <inheritdoc/>
        IEnumerator IEnumerable.GetEnumerator() => m_List.GetEnumerator();

        /// <inheritdoc/>
        public int IndexOf(T item) => m_List.IndexOf(item);

        /// <inheritdoc/>
        public void Insert(int index, T item)
        {
            m_List.Insert(index, item);
            if (item is IVersion v) v.__Parent = this;
            if (__SyncContext != null && item is IVersionSync s) __Recurse(SyncOp.Attach, s);
            __IncrementVersion(); __LogOp(__OP_INSERT, index, item);
        }

        /// <summary>Inserts the elements of a collection at the specified index.</summary>
        public void InsertRange(int index, IEnumerable<T> collection)
        {
            var items = collection is ICollection<T> c ? c : new List<T>(collection);
            m_List.InsertRange(index, items);
            foreach (var item in items)
            {
                if (item is IVersion v) v.__Parent = this;
                if (__SyncContext != null && item is IVersionSync s) __Recurse(SyncOp.Attach, s);
            }
            __IncrementVersion(); __MarkFull();
        }

        /// <inheritdoc/>
        public bool Remove(T item)
        {
            int idx = m_List.IndexOf(item);
            var removed = idx >= 0;
            if (removed)
            {
                m_List.RemoveAt(idx);
                if (item is IVersion v) v.__Parent = null;
                if (__SyncContext != null && item is IVersionSync s) __Recurse(SyncOp.Unregister, s);
                __IncrementVersion(); __LogOp(__OP_REMOVEAT, idx, default);
            }
            return removed;
        }

        /// <inheritdoc/>
        public void RemoveAt(int index)
        {
            var item = m_List[index];
            m_List.RemoveAt(index);
            if (item is IVersion v) v.__Parent = null;
            if (__SyncContext != null && item is IVersionSync s) __Recurse(SyncOp.Unregister, s);
            __IncrementVersion(); __LogOp(__OP_REMOVEAT, index, default);
        }

        /// <summary>Removes a range of elements from the list.</summary>
        public void RemoveRange(int index, int count)
        {
            for (int i = index; i < index + count; i++)
            {
                var item = m_List[i];
                if (item is IVersion v) v.__Parent = null;
                if (__SyncContext != null && item is IVersionSync s) __Recurse(SyncOp.Unregister, s);
            }
            m_List.RemoveRange(index, count);
            __IncrementVersion(); __MarkFull();
        }

        /// <summary>Removes all elements that match the conditions defined by the specified predicate.</summary>
        public int RemoveAll(Predicate<T> match)
        {
            var count = m_List.RemoveAll(item =>
            {
                if (!match(item)) return false;
                if (item is IVersion v) v.__Parent = null;
                if (__SyncContext != null && item is IVersionSync s) __Recurse(SyncOp.Unregister, s);
                return true;
            });
            if (count > 0) { __IncrementVersion(); __MarkFull(); }
            return count;
        }

        /// <summary>Reverses the order of the elements in the list.</summary>
        public void Reverse() { m_List.Reverse(); __IncrementVersion(); __MarkFull(); }

        /// <summary>Reverses the order of the elements in the specified range.</summary>
        public void Reverse(int index, int count) { m_List.Reverse(index, count); __IncrementVersion(); __MarkFull(); }

        /// <summary>Sorts the elements in the list using the default comparer.</summary>
        public void Sort() { m_List.Sort(); __IncrementVersion(); __MarkFull(); }

        /// <summary>Sorts the elements in the list using the specified comparison.</summary>
        public void Sort(Comparison<T> comparison) { m_List.Sort(comparison); __IncrementVersion(); __MarkFull(); }

        /// <summary>Sorts the elements in the list using the specified comparer.</summary>
        public void Sort(IComparer<T> comparer) { m_List.Sort(comparer); __IncrementVersion(); __MarkFull(); }

        /// <summary>Sorts a range of elements using the specified comparer.</summary>
        public void Sort(int index, int count, IComparer<T> comparer) { m_List.Sort(index, count, comparer); __IncrementVersion(); __MarkFull(); }

        /// <summary>Sets the capacity to the actual number of elements in the list.</summary>
        public void TrimExcess() => m_List.TrimExcess();

        /// <summary>Gets or sets the total number of elements the internal data structure can hold.</summary>
        public int Capacity
        {
            get => m_List.Capacity;
            set => m_List.Capacity = value;
        }

        // ===== Synchronization (flat-registry) =====
        /// <inheritdoc/>
        public int __SyncId { get; set; }
        /// <inheritdoc/>
        public SyncContext __SyncContext { get; set; }

        private bool __objectElems;
        private System.Action<System.IO.BinaryWriter, T> __wElem;
        private System.Func<System.IO.BinaryReader, T> __rElem;
        private System.Func<IVersionSync> __newElem;

        /// <summary>Scalar-element mode: owner injects element write/read delegates.</summary>
        public void __InitSync(System.Action<System.IO.BinaryWriter, T> wElem, System.Func<System.IO.BinaryReader, T> rElem)
        {
            __wElem = wElem;
            __rElem = rElem;
            __objectElems = false;
        }

        /// <summary>Object-element mode: owner injects an element factory; elements sync as referenced nodes.</summary>
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
                        child.__MarkAllDirty();   // child before its descendants
                        child.__SyncChildren(SyncOp.Attach);
                    }
                    break;
                case SyncOp.Unregister:
                    child.__Reset();   // leaving the graph -> full reset (id/context/dirty/version/parent, recurses)
                    break;
            }
        }

        /// <inheritdoc/>
        public void AttachTo(SyncContext ctx) { __SyncContext = ctx; __Recurse(SyncOp.Attach, this); }

        /// <inheritdoc/>
        public void __SyncChildren(SyncOp op)
        {
            if (!__objectElems) return;
            for (int i = 0; i < m_List.Count; i++)
                if (m_List[i] is IVersionSync s) __Recurse(op, s);
        }

        // ----- Incremental recording: per-frame op log, coalesced into one record by CaptureDelta. -----
        private const byte __OP_ADD = 1, __OP_INSERT = 2, __OP_SET = 3, __OP_REMOVEAT = 4, __OP_CLEAR = 5;
        private bool __inDirty;     // has pending structural changes this frame (drives __IsDirty)
        private bool __fullDirty;   // flush the whole contents instead of the op log
        private System.Collections.Generic.List<(byte op, int idx, T elem)> __ops;

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
            for (int i = 0; i < m_List.Count; i++)
                if (m_List[i] is IVersionSync s) s.__Reset();   // recurse object elements
            if (__SyncContext != null) __SyncContext.__Objects.Remove(__SyncId);
            __SyncId = 0; __SyncContext = null;
            __inDirty = false; __fullDirty = false; if (__ops != null) __ops.Clear();
            m_Version = 0; __Parent = null;   // keeps contents; detaches for reuse
        }

        private void __MarkFull() { __EnsureDirty(); __fullDirty = true; }

        private void __LogOp(byte op, int idx, T elem)
        {
            __EnsureDirty();
            if (!__inDirty) return;   // not attached
            if (__ops == null) __ops = new System.Collections.Generic.List<(byte, int, T)>();
            __ops.Add((op, idx, elem));
        }

        /// <summary>Full record (keyframe): [id][1][count][elements].</summary>
        public void __CaptureFull(System.IO.BinaryWriter writer)
        {
            writer.Write(__SyncId);
            writer.Write((byte)1);
            writer.Write(m_List.Count);
            for (int i = 0; i < m_List.Count; i++) __WriteElem(writer, m_List[i]);
        }

        /// <summary>Incremental record: [id][0][opCount][ops] — or a full [id][1][count][elements] when fully dirty.</summary>
        public void __CaptureDelta(System.IO.BinaryWriter writer)
        {
            writer.Write(__SyncId);
            if (__fullDirty || __ops == null || __ops.Count == 0)
            {
                writer.Write((byte)1);
                writer.Write(m_List.Count);
                for (int i = 0; i < m_List.Count; i++) __WriteElem(writer, m_List[i]);
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
                    case __OP_INSERT: writer.Write(e.idx); __WriteElem(writer, e.elem); break;
                    case __OP_SET: writer.Write(e.idx); __WriteElem(writer, e.elem); break;
                    case __OP_REMOVEAT: writer.Write(e.idx); break;
                    case __OP_CLEAR: break;
                }
            }
        }

        /// <summary>Applies a node record: a full [1][count][elements] rebuild, or a [0][opCount][ops] replay.
        /// Mutates silently — sets element __Parent directly without triggering sync attach/unregister (object
        /// elements are resolved/created inline by __ReadElem).</summary>
        public void __Apply(System.IO.BinaryReader reader)
        {
            if (reader.ReadByte() == 1)
            {
                foreach (var e in m_List) if (e is IVersion v) v.__Parent = null;
                m_List.Clear();
                int n = reader.ReadInt32();
                for (int i = 0; i < n; i++) { var e = __ReadElem(reader); m_List.Add(e); if (e is IVersion v) v.__Parent = this; }
                return;
            }
            int ops = reader.ReadInt32();
            for (int k = 0; k < ops; k++)
            {
                byte op = reader.ReadByte();
                switch (op)
                {
                    case __OP_ADD: { var e = __ReadElem(reader); m_List.Add(e); if (e is IVersion v) v.__Parent = this; break; }
                    case __OP_INSERT: { int i = reader.ReadInt32(); var e = __ReadElem(reader); m_List.Insert(i, e); if (e is IVersion v) v.__Parent = this; break; }
                    case __OP_SET: { int i = reader.ReadInt32(); var e = __ReadElem(reader); if (m_List[i] is IVersion ov) ov.__Parent = null; m_List[i] = e; if (e is IVersion v) v.__Parent = this; break; }
                    case __OP_REMOVEAT: { int i = reader.ReadInt32(); if (m_List[i] is IVersion ov) ov.__Parent = null; m_List.RemoveAt(i); break; }
                    case __OP_CLEAR: { foreach (var e in m_List) if (e is IVersion v) v.__Parent = null; m_List.Clear(); break; }
                }
            }
        }
    }
}
