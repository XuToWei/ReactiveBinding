#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;

namespace ReactiveBinding
{
    /// <summary>
    /// A set implementation that tracks modifications via a version number.
    /// The Version property increments on each Add, Remove, or Clear operation.
    /// If elements implement IVersion, they will share this container as Parent,
    /// allowing element property changes to automatically increment the container's version.
    /// </summary>
    /// <typeparam name="T">The type of elements in the set.</typeparam>
    public class VersionHashSet<T> : ISet<T>, IReadOnlyCollection<T>, IVersion
    {
        private readonly HashSet<T> m_Set;
        private int m_Version;

        /// <summary>
        /// Creates a new empty VersionHashSet.
        /// </summary>
        public VersionHashSet()
        {
            m_Set = new HashSet<T>();
        }

        /// <summary>
        /// Creates a new VersionHashSet with the specified comparer.
        /// </summary>
        public VersionHashSet(IEqualityComparer<T> comparer)
        {
            m_Set = new HashSet<T>(comparer);
        }

        /// <summary>
        /// Creates a new VersionHashSet containing elements from the specified collection.
        /// </summary>
        public VersionHashSet(IEnumerable<T> collection)
        {
            m_Set = new HashSet<T>(collection);
            foreach (var item in m_Set)
            {
                AssignParent(item);
            }
        }

        /// <summary>
        /// Creates a new VersionHashSet containing elements from the specified collection with the specified comparer.
        /// </summary>
        public VersionHashSet(IEnumerable<T> collection, IEqualityComparer<T> comparer)
        {
            m_Set = new HashSet<T>(collection, comparer);
            foreach (var item in m_Set)
            {
                AssignParent(item);
            }
        }

#if UNITY_2021_2_OR_NEWER || NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
        /// <summary>
        /// Creates a new VersionHashSet with the specified initial capacity.
        /// </summary>
        public VersionHashSet(int capacity)
        {
            m_Set = new HashSet<T>(capacity);
        }

        /// <summary>
        /// Creates a new VersionHashSet with the specified initial capacity and comparer.
        /// </summary>
        public VersionHashSet(int capacity, IEqualityComparer<T> comparer)
        {
            m_Set = new HashSet<T>(capacity, comparer);
        }
#endif

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
        public int Count => m_Set.Count;

        /// <inheritdoc/>
        public bool IsReadOnly => false;

        /// <inheritdoc/>
        public bool Add(T item)
        {
            var added = m_Set.Add(item);
            if (added)
            {
                AssignParent(item);
                OnItemAdded(item);
                IncrementVersion();
                RecordAdd(item);
            }
            return added;
        }

        /// <inheritdoc/>
        void ICollection<T>.Add(T item)
        {
            if (m_Set.Add(item))
            {
                AssignParent(item);
                OnItemAdded(item);
                IncrementVersion();
                RecordAdd(item);
            }
        }

        /// <inheritdoc/>
        public void Clear()
        {
            foreach (var item in m_Set)
            {
                ClearParent(item);
                OnItemRemoved(item);
            }
            m_Set.Clear();
            IncrementVersion();
            RecordClear();
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
                if (m_Set.Remove(item))
                {
                    ClearParent(item);
                    OnItemRemoved(item);
                }
            }
            if (m_Set.Count != countBefore)
            {
                IncrementVersion();
                RecordReset();
            }
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
                ClearParent(item);
                OnItemRemoved(item);
                return true;
            });
            if (m_Set.Count != countBefore)
            {
                IncrementVersion();
                RecordReset();
            }
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
                ClearParent(item);
                OnItemRemoved(item);
                IncrementVersion();
                RecordRemove(item);
            }
            return removed;
        }

        /// <summary>
        /// Removes all elements that match the conditions defined by the specified predicate.
        /// </summary>
        public int RemoveWhere(Predicate<T> match)
        {
            var count = m_Set.RemoveWhere(item =>
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
                    ClearParent(item);
                    OnItemRemoved(item);
                    changed = true;
                }
                else
                {
                    m_Set.Add(item);
                    AssignParent(item);
                    OnItemAdded(item);
                    changed = true;
                }
            }
            if (changed)
            {
                IncrementVersion();
                RecordReset();
            }
        }

        /// <inheritdoc/>
        public void UnionWith(IEnumerable<T> other)
        {
            var countBefore = m_Set.Count;
            foreach (var item in other)
            {
                if (m_Set.Add(item))
                {
                    AssignParent(item);
                    OnItemAdded(item);
                }
            }
            if (m_Set.Count != countBefore)
            {
                IncrementVersion();
                RecordReset();
            }
        }

        /// <summary>
        /// Sets the capacity to the actual number of elements in the set.
        /// </summary>
        public void TrimExcess()
        {
#if UNITY_2021_2_OR_NEWER || NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
            m_Set.TrimExcess();
#endif
        }

        /// <summary>
        /// Gets the comparer used to determine equality of values.
        /// </summary>
        public IEqualityComparer<T> Comparer => m_Set.Comparer;

        // ===== Synchronization (op-log) =====
        // Op types: 1 = Add(item), 2 = Remove(item), 3 = Clear. (Element internal patch is not supported.)
        private struct SyncOp { public byte Type; public T Value; }

        private bool m_SyncEnabled;
        private bool m_ResetPending;
        private bool m_SuppressRecord;
        private List<SyncOp> m_SyncOps;

        private bool ShouldRecord => m_SyncEnabled && !m_SuppressRecord;

        private void RecordAdd(T item)
        {
            if (!ShouldRecord || m_ResetPending) return;
            if (m_SyncOps == null) m_SyncOps = new List<SyncOp>();
            m_SyncOps.Add(new SyncOp { Type = 1, Value = item });
        }

        private void RecordRemove(T item)
        {
            if (!ShouldRecord || m_ResetPending) return;
            if (m_SyncOps == null) m_SyncOps = new List<SyncOp>();
            m_SyncOps.Add(new SyncOp { Type = 2, Value = item });
        }

        private void RecordClear()
        {
            if (!ShouldRecord || m_ResetPending) return;
            if (m_SyncOps == null) m_SyncOps = new List<SyncOp>();
            m_SyncOps.Add(new SyncOp { Type = 3 });
        }

        private void RecordReset()
        {
            if (!ShouldRecord) return;
            m_ResetPending = true;
            if (m_SyncOps != null) m_SyncOps.Clear();
        }

        /// <summary>Clears the accumulated increment and re-baselines (enables recording).</summary>
        public void ResetSync()
        {
            m_SyncEnabled = true;
            m_ResetPending = false;
            if (m_SyncOps != null) m_SyncOps.Clear();
        }

        /// <summary>Writes the full content via <paramref name="writeElem"/>.</summary>
        public void WriteFull(System.IO.BinaryWriter w, System.Action<System.IO.BinaryWriter, T> writeElem)
        {
            w.Write(m_Set.Count);
            foreach (var item in m_Set) writeElem(w, item);
        }

        /// <summary>Reads full content written by <see cref="WriteFull"/>.</summary>
        public void ReadFull(System.IO.BinaryReader r, System.Func<System.IO.BinaryReader, T> readElem)
        {
            m_SuppressRecord = true;
            ApplyClear();
            int count = r.ReadInt32();
            for (int i = 0; i < count; i++) ApplyAdd(readElem(r));
            m_SuppressRecord = false;
        }

        /// <summary>Writes the increment since the last <see cref="ResetSync"/>.</summary>
        public void WriteDelta(System.IO.BinaryWriter w, System.Action<System.IO.BinaryWriter, T> writeElem)
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
                if (op.Type == 1 || op.Type == 2) writeElem(w, op.Value);
            }
        }

        /// <summary>Applies an increment written by <see cref="WriteDelta"/>.</summary>
        public void ReadDelta(System.IO.BinaryReader r, System.Func<System.IO.BinaryReader, T> readElem)
        {
            m_SuppressRecord = true;
            byte mode = r.ReadByte();
            if (mode == 1)
            {
                ApplyClear();
                int count = r.ReadInt32();
                for (int i = 0; i < count; i++) ApplyAdd(readElem(r));
                m_SuppressRecord = false;
                return;
            }

            int opCount = r.ReadInt32();
            for (int i = 0; i < opCount; i++)
            {
                byte type = r.ReadByte();
                if (type == 1) ApplyAdd(readElem(r));
                else if (type == 2) ApplyRemove(readElem(r));
                else if (type == 3) ApplyClear();
            }
            m_SuppressRecord = false;
        }

        private void ApplyAdd(T item)
        {
            if (m_Set.Add(item)) AssignParent(item);
        }

        private void ApplyRemove(T item)
        {
            if (m_Set.Remove(item)) ClearParent(item);
        }

        private void ApplyClear()
        {
            foreach (var item in m_Set) ClearParent(item);
            m_Set.Clear();
        }
    }
}
