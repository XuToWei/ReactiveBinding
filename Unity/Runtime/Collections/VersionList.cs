#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;

namespace ReactiveBinding
{
    /// <summary>
    /// A list implementation that tracks modifications via a version number.
    /// The __Version property increments on each Add, Remove, Insert, Clear, or index set operation.
    /// If elements implement IVersion, they will share this container as __Parent,
    /// allowing element property changes to automatically increment the container's version.
    /// </summary>
    /// <typeparam name="T">The type of elements in the list.</typeparam>
    public class VersionList<T> : IList<T>, IReadOnlyList<T>, IVersion, IVersionSync
    {
        private readonly List<T> m_List;
        private int m_Version;

        /// <summary>
        /// Creates a new empty VersionList.
        /// </summary>
        public VersionList()
        {
            m_List = new List<T>();
        }

        /// <summary>
        /// Creates a new VersionList with the specified initial capacity.
        /// </summary>
        public VersionList(int capacity)
        {
            m_List = new List<T>(capacity);
        }

        /// <summary>
        /// Creates a new VersionList containing elements from the specified collection.
        /// </summary>
        public VersionList(IEnumerable<T> collection)
        {
            m_List = new List<T>(collection);
            foreach (var item in m_List)
            {
                AssignParent(item);
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

        private void AssignParent(T item)
        {
            if (item is IVersion v) v.__Parent = this;
            // Sync: register an object element (and its subtree) when this list is attached to a context.
            if (__SyncContext != null && item is IVersionSync s) __Recurse(SyncOp.Attach, s);
        }

        private void ClearParent(T item)
        {
            if (item is IVersion v) v.__Parent = null;
            if (__SyncContext != null && item is IVersionSync s) __Recurse(SyncOp.Unregister, s);
        }

        protected virtual void OnItemAdded(T item) { }

        protected virtual void OnItemRemoved(T item) { }

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
                ClearParent(oldItem);
                OnItemRemoved(oldItem);
                m_List[index] = value;
                AssignParent(value);
                OnItemAdded(value);
                __IncrementVersion();
                RecordSet(index, value);
            }
        }

        /// <inheritdoc/>
        public void Add(T item)
        {
            m_List.Add(item);
            AssignParent(item);
            OnItemAdded(item);
            __IncrementVersion();
            RecordInsert(m_List.Count - 1, item);
        }

        /// <summary>
        /// Adds the elements of the specified collection to the end of the list.
        /// </summary>
        public void AddRange(IEnumerable<T> collection)
        {
            var items = collection is ICollection<T> c ? c : new List<T>(collection);
            m_List.AddRange(items);
            foreach (var item in items)
            {
                AssignParent(item);
                OnItemAdded(item);
            }
            __IncrementVersion();
            RecordReset();
        }

        /// <inheritdoc/>
        public void Clear()
        {
            foreach (var item in m_List)
            {
                ClearParent(item);
                OnItemRemoved(item);
            }
            m_List.Clear();
            __IncrementVersion();
            RecordClear();
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
            AssignParent(item);
            OnItemAdded(item);
            __IncrementVersion();
            RecordInsert(index, item);
        }

        /// <summary>
        /// Inserts the elements of a collection at the specified index.
        /// </summary>
        public void InsertRange(int index, IEnumerable<T> collection)
        {
            var items = collection is ICollection<T> c ? c : new List<T>(collection);
            m_List.InsertRange(index, items);
            foreach (var item in items)
            {
                AssignParent(item);
                OnItemAdded(item);
            }
            __IncrementVersion();
            RecordReset();
        }

        /// <inheritdoc/>
        public bool Remove(T item)
        {
            int idx = m_List.IndexOf(item);
            var removed = idx >= 0;
            if (removed)
            {
                m_List.RemoveAt(idx);
                ClearParent(item);
                OnItemRemoved(item);
                __IncrementVersion();
                RecordRemoveAt(idx);
            }
            return removed;
        }

        /// <inheritdoc/>
        public void RemoveAt(int index)
        {
            var item = m_List[index];
            m_List.RemoveAt(index);
            ClearParent(item);
            OnItemRemoved(item);
            __IncrementVersion();
            RecordRemoveAt(index);
        }

        /// <summary>
        /// Removes a range of elements from the list.
        /// </summary>
        public void RemoveRange(int index, int count)
        {
            for (int i = index; i < index + count; i++)
            {
                ClearParent(m_List[i]);
                OnItemRemoved(m_List[i]);
            }
            m_List.RemoveRange(index, count);
            __IncrementVersion();
            RecordReset();
        }

        /// <summary>
        /// Removes all elements that match the conditions defined by the specified predicate.
        /// </summary>
        public int RemoveAll(Predicate<T> match)
        {
            var count = m_List.RemoveAll(item =>
            {
                if (!match(item)) return false;
                ClearParent(item);
                OnItemRemoved(item);
                return true;
            });
            if (count > 0)
            {
                __IncrementVersion();
                RecordReset();
            }
            return count;
        }

        /// <summary>
        /// Reverses the order of the elements in the list.
        /// </summary>
        public void Reverse()
        {
            m_List.Reverse();
            __IncrementVersion();
            RecordReset();
        }

        /// <summary>
        /// Reverses the order of the elements in the specified range.
        /// </summary>
        public void Reverse(int index, int count)
        {
            m_List.Reverse(index, count);
            __IncrementVersion();
            RecordReset();
        }

        /// <summary>
        /// Sorts the elements in the list using the default comparer.
        /// </summary>
        public void Sort()
        {
            m_List.Sort();
            __IncrementVersion();
            RecordReset();
        }

        /// <summary>
        /// Sorts the elements in the list using the specified comparison.
        /// </summary>
        public void Sort(Comparison<T> comparison)
        {
            m_List.Sort(comparison);
            __IncrementVersion();
            RecordReset();
        }

        /// <summary>
        /// Sorts the elements in the list using the specified comparer.
        /// </summary>
        public void Sort(IComparer<T> comparer)
        {
            m_List.Sort(comparer);
            __IncrementVersion();
            RecordReset();
        }

        /// <summary>
        /// Sorts a range of elements using the specified comparer.
        /// </summary>
        public void Sort(int index, int count, IComparer<T> comparer)
        {
            m_List.Sort(index, count, comparer);
            __IncrementVersion();
            RecordReset();
        }

        /// <summary>
        /// Sets the capacity to the actual number of elements in the list.
        /// </summary>
        public void TrimExcess() => m_List.TrimExcess();

        /// <summary>
        /// Gets or sets the total number of elements the internal data structure can hold.
        /// </summary>
        public int Capacity
        {
            get => m_List.Capacity;
            set => m_List.Capacity = value;
        }

        // ===== Synchronization (flat-registry, direct-write model) =====
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

        // Generic register / unregister / write-subtree recursion driver (inline against the context state).
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
                        child.__SyncChildren(SyncOp.Attach);
                    }
                    break;
                case SyncOp.Unregister:
                    if (child.__SyncId != 0)
                    {
                        var __w = __SyncContext.__Writer;
                        __SyncContext.__Objects.Remove(child.__SyncId);
                        __w.Write((byte)1); __w.Write(child.__SyncId);
                        child.__SyncChildren(SyncOp.Unregister);
                        child.__SyncId = 0; child.__SyncContext = null;
                    }
                    break;
                case SyncOp.WriteSubtree:
                    child.__Commit();
                    child.__SyncChildren(SyncOp.WriteSubtree);
                    break;
            }
        }

        /// <inheritdoc/>
        public void AttachTo(SyncContext ctx) { __SyncContext = ctx; __Recurse(SyncOp.Attach, this); }

        // Each op writes its record straight into the recorder the moment it happens.
        // Op record: [0][id][mode=0][opcode][data]. Opcodes: 1 insert, 2 removeAt, 3 set, 4 clear.
        private void RecordInsert(int index, T value)
        {
            if (__SyncContext == null) return;
            var w = __SyncContext.__Writer;
            w.Write((byte)0); w.Write(__SyncId); w.Write((byte)0); w.Write((byte)1); w.Write(index); __WriteElem(w, value);
            if (__objectElems && value != null) __Recurse(SyncOp.WriteSubtree, (IVersionSync)(object)value);
        }
        private void RecordRemoveAt(int index)
        {
            if (__SyncContext == null) return;
            var w = __SyncContext.__Writer;
            w.Write((byte)0); w.Write(__SyncId); w.Write((byte)0); w.Write((byte)2); w.Write(index);
        }
        private void RecordSet(int index, T value)
        {
            if (__SyncContext == null) return;
            var w = __SyncContext.__Writer;
            w.Write((byte)0); w.Write(__SyncId); w.Write((byte)0); w.Write((byte)3); w.Write(index); __WriteElem(w, value);
            if (__objectElems && value != null) __Recurse(SyncOp.WriteSubtree, (IVersionSync)(object)value);
        }
        private void RecordClear()
        {
            if (__SyncContext == null) return;
            var w = __SyncContext.__Writer;
            w.Write((byte)0); w.Write(__SyncId); w.Write((byte)0); w.Write((byte)4);
        }
        // Batch op: resend the whole container as a full record (mode 1) plus object-element subtrees.
        private void RecordReset() { if (__SyncContext != null) __Recurse(SyncOp.WriteSubtree, this); }

        /// <inheritdoc/>
        public void __SyncChildren(SyncOp op)
        {
            if (!__objectElems) return;
            for (int i = 0; i < m_List.Count; i++)
                if (m_List[i] is IVersionSync s) __Recurse(op, s);
        }

        /// <summary>Full snapshot of this node: one record [0][id][mode=1][count][elements].</summary>
        public void __Commit()
        {
            var writer = __SyncContext.__Writer;
            writer.Write((byte)0); writer.Write(__SyncId);
            writer.Write((byte)1);
            writer.Write(m_List.Count);
            for (int i = 0; i < m_List.Count; i++) __WriteElem(writer, m_List[i]);
        }

        /// <summary>Applies a single node record (full or one op). Mutates silently: sets element __Parent but
        /// never touches the registry (creation is via the context's Resolve; removal via removal records).</summary>
        public void __Apply(System.IO.BinaryReader reader)
        {
            byte mode = reader.ReadByte();
            if (mode == 1)
            {
                foreach (var e in m_List) __ClearParentSilent(e);
                m_List.Clear();
                int n = reader.ReadInt32();
                for (int i = 0; i < n; i++) { var e = __ReadElem(reader); m_List.Add(e); __SetParentSilent(e); }
                return;
            }

            byte code = reader.ReadByte();
            if (code == 1) { int idx = reader.ReadInt32(); var e = __ReadElem(reader); m_List.Insert(idx, e); __SetParentSilent(e); }
            else if (code == 2) { int idx = reader.ReadInt32(); __ClearParentSilent(m_List[idx]); m_List.RemoveAt(idx); }
            else if (code == 3) { int idx = reader.ReadInt32(); var e = __ReadElem(reader); __ClearParentSilent(m_List[idx]); m_List[idx] = e; __SetParentSilent(e); }
            else if (code == 4) { foreach (var e in m_List) __ClearParentSilent(e); m_List.Clear(); }
        }

        private void __SetParentSilent(T item) { if (item is IVersion v) v.__Parent = this; }
        private static void __ClearParentSilent(T item) { if (item is IVersion v) v.__Parent = null; }
    }
}
