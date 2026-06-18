#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;

namespace ReactiveBinding
{
    /// <summary>
    /// A list implementation that tracks modifications via a version number.
    /// The Version property increments on each Add, Remove, Insert, Clear, or index set operation.
    /// If elements implement IVersion, they will share this container as Parent,
    /// allowing element property changes to automatically increment the container's version.
    /// </summary>
    /// <typeparam name="T">The type of elements in the list.</typeparam>
    public class VersionList<T> : IList<T>, IReadOnlyList<T>, IVersion
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
        public int Version => m_Version;

        /// <inheritdoc/>
        public IVersion Parent { get; set; }

        /// <inheritdoc/>
        public void IncrementVersion()
        {
            m_Version = VersionCounter.Next();
            if (Parent != null) Parent.IncrementVersion();
        }

        private void AssignParent(T item)
        {
            if (item is IVersion v) v.Parent = this;
        }

        private void ClearParent(T item)
        {
            if (item is IVersion v) v.Parent = null;
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
                IncrementVersion();
                RecordSet(index, value);
            }
        }

        /// <inheritdoc/>
        public void Add(T item)
        {
            m_List.Add(item);
            AssignParent(item);
            OnItemAdded(item);
            IncrementVersion();
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
            IncrementVersion();
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
            IncrementVersion();
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
            IncrementVersion();
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
            IncrementVersion();
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
                IncrementVersion();
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
            IncrementVersion();
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
            IncrementVersion();
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
                IncrementVersion();
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
            IncrementVersion();
            RecordReset();
        }

        /// <summary>
        /// Reverses the order of the elements in the specified range.
        /// </summary>
        public void Reverse(int index, int count)
        {
            m_List.Reverse(index, count);
            IncrementVersion();
            RecordReset();
        }

        /// <summary>
        /// Sorts the elements in the list using the default comparer.
        /// </summary>
        public void Sort()
        {
            m_List.Sort();
            IncrementVersion();
            RecordReset();
        }

        /// <summary>
        /// Sorts the elements in the list using the specified comparison.
        /// </summary>
        public void Sort(Comparison<T> comparison)
        {
            m_List.Sort(comparison);
            IncrementVersion();
            RecordReset();
        }

        /// <summary>
        /// Sorts the elements in the list using the specified comparer.
        /// </summary>
        public void Sort(IComparer<T> comparer)
        {
            m_List.Sort(comparer);
            IncrementVersion();
            RecordReset();
        }

        /// <summary>
        /// Sorts a range of elements using the specified comparer.
        /// </summary>
        public void Sort(int index, int count, IComparer<T> comparer)
        {
            m_List.Sort(index, count, comparer);
            IncrementVersion();
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

        // ===== Synchronization (op-log) =====
        // Op types: 1 = Insert(index,value), 2 = RemoveAt(index), 3 = Set(index,value), 4 = Clear.
        private struct SyncOp { public byte Type; public int Index; public T Value; }

        private bool m_SyncEnabled;
        private bool m_ResetPending;
        private bool m_SuppressRecord;
        private int m_SyncWatermark;
        private List<SyncOp> m_SyncOps;

        private bool ShouldRecord => m_SyncEnabled && !m_SuppressRecord;

        private void RecordInsert(int index, T value)
        {
            if (!ShouldRecord || m_ResetPending) return;
            if (m_SyncOps == null) m_SyncOps = new List<SyncOp>();
            m_SyncOps.Add(new SyncOp { Type = 1, Index = index, Value = value });
        }

        private void RecordRemoveAt(int index)
        {
            if (!ShouldRecord || m_ResetPending) return;
            if (m_SyncOps == null) m_SyncOps = new List<SyncOp>();
            m_SyncOps.Add(new SyncOp { Type = 2, Index = index });
        }

        private void RecordSet(int index, T value)
        {
            if (!ShouldRecord || m_ResetPending) return;
            if (m_SyncOps == null) m_SyncOps = new List<SyncOp>();
            m_SyncOps.Add(new SyncOp { Type = 3, Index = index, Value = value });
        }

        private void RecordClear()
        {
            if (!ShouldRecord || m_ResetPending) return;
            if (m_SyncOps == null) m_SyncOps = new List<SyncOp>();
            m_SyncOps.Add(new SyncOp { Type = 4, Index = 0 });
        }

        private void RecordReset()
        {
            if (!ShouldRecord) return;
            m_ResetPending = true;
            if (m_SyncOps != null) m_SyncOps.Clear();
        }

        /// <summary>Clears the accumulated increment and re-baselines (enables recording, recurses elements).</summary>
        public void ResetSync()
        {
            m_SyncEnabled = true;
            m_ResetPending = false;
            if (m_SyncOps != null) m_SyncOps.Clear();
            m_SyncWatermark = VersionCounter.Current;
            for (int i = 0; i < m_List.Count; i++)
            {
                if (m_List[i] is IVersionSyncable s) s.ResetSync();
            }
        }

        /// <summary>Writes the full content. Element values are written via <paramref name="writeElem"/>.</summary>
        public void WriteFull(System.IO.BinaryWriter w, System.Action<System.IO.BinaryWriter, T> writeElem)
        {
            w.Write(m_List.Count);
            for (int i = 0; i < m_List.Count; i++) writeElem(w, m_List[i]);
        }

        /// <summary>Reads full content written by <see cref="WriteFull"/>.</summary>
        public void ReadFull(System.IO.BinaryReader r, System.Func<System.IO.BinaryReader, T> readElem)
        {
            m_SuppressRecord = true;
            ApplyClear();
            int count = r.ReadInt32();
            for (int i = 0; i < count; i++) ApplyInsert(m_List.Count, readElem(r));
            m_SuppressRecord = false;
        }

        /// <summary>
        /// Writes the merged increment since the last <see cref="ResetSync"/>.
        /// When <paramref name="patchElem"/> is non-null, changed IVersion elements are written as
        /// field-level patches (via the element's own WriteDelta) instead of whole-element resends.
        /// </summary>
        public void WriteDelta(System.IO.BinaryWriter w, System.Action<System.IO.BinaryWriter, T> writeElem,
            System.Action<System.IO.BinaryWriter, T> patchElem)
        {
            if (!m_SyncEnabled || m_ResetPending)
            {
                w.Write((byte)1); // reset-full
                WriteFull(w, writeElem);
                return;
            }

            w.Write((byte)0); // ops

            int opCount = m_SyncOps == null ? 0 : m_SyncOps.Count;
            w.Write(opCount);
            for (int i = 0; i < opCount; i++)
            {
                var op = m_SyncOps[i];
                w.Write(op.Type);
                if (op.Type == 1 || op.Type == 3) { w.Write(op.Index); writeElem(w, op.Value); }
                else if (op.Type == 2) { w.Write(op.Index); }
                // type 4 (Clear) carries nothing
            }

            // Element internal changes: field-level patch by current index (IVersion elements only).
            if (patchElem != null)
            {
                var changed = new List<int>();
                for (int i = 0; i < m_List.Count; i++)
                {
                    if (m_List[i] is IVersion v && v.Version > m_SyncWatermark) changed.Add(i);
                }
                w.Write(changed.Count);
                for (int j = 0; j < changed.Count; j++)
                {
                    w.Write(changed[j]);
                    patchElem(w, m_List[changed[j]]);
                }
            }
            else
            {
                w.Write(0);
            }
        }

        /// <summary>Applies an increment written by <see cref="WriteDelta"/>.</summary>
        public void ReadDelta(System.IO.BinaryReader r, System.Func<System.IO.BinaryReader, T> readElem,
            System.Action<System.IO.BinaryReader, T> patchInto)
        {
            m_SuppressRecord = true;
            byte mode = r.ReadByte();
            if (mode == 1)
            {
                ApplyClear();
                int count = r.ReadInt32();
                for (int i = 0; i < count; i++) ApplyInsert(m_List.Count, readElem(r));
                m_SuppressRecord = false;
                return;
            }

            int opCount = r.ReadInt32();
            for (int i = 0; i < opCount; i++)
            {
                byte type = r.ReadByte();
                if (type == 1) { int idx = r.ReadInt32(); ApplyInsert(idx, readElem(r)); }
                else if (type == 2) { int idx = r.ReadInt32(); ApplyRemoveAt(idx); }
                else if (type == 3) { int idx = r.ReadInt32(); ApplySet(idx, readElem(r)); }
                else if (type == 4) { ApplyClear(); }
            }

            int changedCount = r.ReadInt32();
            for (int j = 0; j < changedCount; j++)
            {
                int idx = r.ReadInt32();
                patchInto(r, m_List[idx]);
            }
            m_SuppressRecord = false;
        }

        private void ApplyInsert(int index, T value)
        {
            m_List.Insert(index, value);
            AssignParent(value);
            OnItemAdded(value);
        }

        private void ApplyRemoveAt(int index)
        {
            var item = m_List[index];
            m_List.RemoveAt(index);
            ClearParent(item);
            OnItemRemoved(item);
        }

        private void ApplySet(int index, T value)
        {
            var old = m_List[index];
            ClearParent(old);
            OnItemRemoved(old);
            m_List[index] = value;
            AssignParent(value);
            OnItemAdded(value);
        }

        private void ApplyClear()
        {
            for (int i = 0; i < m_List.Count; i++) ClearParent(m_List[i]);
            m_List.Clear();
        }
    }
}
