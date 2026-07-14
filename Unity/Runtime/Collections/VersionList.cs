#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;

namespace ReactiveBinding
{
    /// <summary>
    /// A list that tracks modifications via a version number. The <see cref="__Version"/> property increments on
    /// each Add, Remove, Insert, Clear, or index-set operation. If elements implement <see cref="IVersion"/> they
    /// share this container as <see cref="__Parent"/>, so element property changes bubble up the version chain.
    /// This is the version-tracking-only container; for flat-registry data synchronization use the separate
    /// <see cref="VersionSyncList{T}"/>.
    /// </summary>
    /// <typeparam name="T">The type of elements in the list.</typeparam>
    public class VersionList<T> : IList<T>, IReadOnlyList<T>, IVersion
    {
        private readonly List<T> m_List;

        /// <summary>Creates a new empty VersionList.</summary>
        public VersionList() { m_List = new List<T>(); }

        /// <summary>Creates a new VersionList with the specified initial capacity.</summary>
        public VersionList(int capacity) { m_List = new List<T>(capacity); }

        /// <summary>Creates a new VersionList containing elements from the specified collection.</summary>
        public VersionList(IEnumerable<T> collection)
        {
            m_List = new List<T>(collection);
            VersionOwnership.EnsureCanAttachAll(this, m_List);
            foreach (var item in m_List) if (item is IVersion v) v.__Parent = this;
        }

        /// <inheritdoc/>
        public int __Version { get; set; }

        /// <inheritdoc/>
        public int Version => __Version;

        /// <inheritdoc/>
        public IVersion __Parent { get; set; }

        /// <inheritdoc/>
        public void __IncrementVersion()
        {
            __Version = VersionCounter.Next();
            if (__Parent != null) __Parent.__IncrementVersion();
        }

        /// <inheritdoc/>
        public void __Reset()
        {
            for (int i = 0; i < m_List.Count; i++)
                if (m_List[i] is IVersion v)
                {
                    v.__Reset();
                    v.__Parent = this;   // keep ownership links inside the reusable subtree
                }
            __Version = 0; __Parent = null;   // keeps contents; detaches for reuse
        }

        /// <inheritdoc/>
        public void Reset() => __Reset();

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
                if (VersionOwnership.AreSame(oldItem, value)) return;
                if (value is IVersion nv) VersionOwnership.EnsureCanAttach(this, nv);
                m_List[index] = value;
                if (oldItem is IVersion ov) ov.__Parent = null;
                if (value is IVersion attached) attached.__Parent = this;
                __IncrementVersion();
            }
        }

        /// <inheritdoc/>
        public void Add(T item)
        {
            if (item is IVersion child) VersionOwnership.EnsureCanAttach(this, child);
            m_List.Add(item);
            if (item is IVersion v) v.__Parent = this;
            __IncrementVersion();
        }

        /// <summary>Adds the elements of the specified collection to the end of the list.</summary>
        public void AddRange(IEnumerable<T> collection)
        {
            if (collection == null) throw new ArgumentNullException(nameof(collection));
            if (collection is ICollection<T> stable)
            {
                if (stable.Count == 0) return;
                VersionOwnership.EnsureCanAttachAll(this, stable);
                int first = m_List.Count;
                m_List.AddRange(stable);
                for (int i = first; i < m_List.Count; i++)
                    if (m_List[i] is IVersion v) v.__Parent = this;
                __IncrementVersion();
                return;
            }

            var items = new List<T>(collection);
            if (items.Count == 0) return;
            VersionOwnership.EnsureCanAttachAll(this, items);
            int start = m_List.Count;
            m_List.AddRange(items);
            for (int i = start; i < m_List.Count; i++)
                if (m_List[i] is IVersion v) v.__Parent = this;
            __IncrementVersion();
        }

        /// <inheritdoc/>
        public void Clear()
        {
            if (m_List.Count == 0) return;
            for (int i = 0; i < m_List.Count; i++)
                if (m_List[i] is IVersion v) v.__Parent = null;
            m_List.Clear();
            __IncrementVersion();
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
            if (index < 0 || index > m_List.Count) throw new ArgumentOutOfRangeException(nameof(index));
            if (item is IVersion child) VersionOwnership.EnsureCanAttach(this, child);
            m_List.Insert(index, item);
            if (item is IVersion v) v.__Parent = this;
            __IncrementVersion();
        }

        /// <summary>Inserts the elements of a collection at the specified index.</summary>
        public void InsertRange(int index, IEnumerable<T> collection)
        {
            if (index < 0 || index > m_List.Count) throw new ArgumentOutOfRangeException(nameof(index));
            if (collection == null) throw new ArgumentNullException(nameof(collection));
            if (collection is ICollection<T> stable)
            {
                if (stable.Count == 0) return;
                VersionOwnership.EnsureCanAttachAll(this, stable);
                int count = stable.Count;
                m_List.InsertRange(index, stable);
                for (int i = index; i < index + count; i++)
                    if (m_List[i] is IVersion v) v.__Parent = this;
                __IncrementVersion();
                return;
            }

            var items = new List<T>(collection);
            if (items.Count == 0) return;
            VersionOwnership.EnsureCanAttachAll(this, items);
            m_List.InsertRange(index, items);
            for (int i = index; i < index + items.Count; i++)
                if (m_List[i] is IVersion v) v.__Parent = this;
            __IncrementVersion();
        }

        /// <inheritdoc/>
        public bool Remove(T item)
        {
            int idx = m_List.IndexOf(item);
            var removed = idx >= 0;
            if (removed)
            {
                var storedItem = m_List[idx];
                m_List.RemoveAt(idx);
                if (storedItem is IVersion v) v.__Parent = null;
                __IncrementVersion();
            }
            return removed;
        }

        /// <inheritdoc/>
        public void RemoveAt(int index)
        {
            var item = m_List[index];
            m_List.RemoveAt(index);
            if (item is IVersion v) v.__Parent = null;
            __IncrementVersion();
        }

        /// <summary>Removes a range of elements from the list.</summary>
        public void RemoveRange(int index, int count)
        {
            __ValidateRange(index, count);
            if (count == 0) return;
            int end = index + count;
            for (int i = index; i < end; i++)
                if (m_List[i] is IVersion v) v.__Parent = null;
            m_List.RemoveRange(index, count);
            __IncrementVersion();
        }

        /// <summary>Removes all elements that match the conditions defined by the specified predicate.</summary>
        public int RemoveAll(Predicate<T> match)
        {
            if (match == null) throw new ArgumentNullException(nameof(match));
            int write = 0;
            int originalCount = m_List.Count;
            for (int read = 0; read < originalCount; read++)
            {
                var item = m_List[read];
                if (match(item))
                {
                    if (item is IVersion v) v.__Parent = null;
                    continue;
                }
                if (write != read) m_List[write] = item;
                write++;
            }
            int count = originalCount - write;
            if (count == 0) return 0;
            m_List.RemoveRange(write, count);
            __IncrementVersion();
            return count;
        }

        private void __ValidateRange(int index, int count)
        {
            if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            if (m_List.Count - index < count) throw new ArgumentException("The range exceeds the list bounds.");
        }

        /// <summary>Reverses the order of the elements in the list.</summary>
        public void Reverse() { if (m_List.Count <= 1) return; m_List.Reverse(); __IncrementVersion(); }

        /// <summary>Reverses the order of the elements in the specified range.</summary>
        public void Reverse(int index, int count) { m_List.Reverse(index, count); if (count > 1) __IncrementVersion(); }

        /// <summary>Sorts the elements in the list using the default comparer.</summary>
        public void Sort() { if (m_List.Count <= 1) return; m_List.Sort(); __IncrementVersion(); }

        /// <summary>Sorts the elements in the list using the specified comparison.</summary>
        public void Sort(Comparison<T> comparison) { m_List.Sort(comparison); if (m_List.Count > 1) __IncrementVersion(); }

        /// <summary>Sorts the elements in the list using the specified comparer.</summary>
        public void Sort(IComparer<T> comparer) { m_List.Sort(comparer); if (m_List.Count > 1) __IncrementVersion(); }

        /// <summary>Sorts a range of elements using the specified comparer.</summary>
        public void Sort(int index, int count, IComparer<T> comparer) { m_List.Sort(index, count, comparer); if (count > 1) __IncrementVersion(); }

        /// <summary>Sorts only when the list is not already ordered; returns whether its order changed.</summary>
        public bool SortIfNeeded() => SortIfNeeded(0, m_List.Count, Comparer<T>.Default);

        /// <summary>Sorts only when the list is not already ordered by <paramref name="comparer"/>.</summary>
        public bool SortIfNeeded(IComparer<T> comparer) => SortIfNeeded(0, m_List.Count, comparer);

        /// <summary>Sorts only when the list is not already ordered by <paramref name="comparison"/>.</summary>
        public bool SortIfNeeded(Comparison<T> comparison)
        {
            if (comparison == null) throw new ArgumentNullException(nameof(comparison));
            if (m_List.Count <= 1) return false;
            for (int i = 1; i < m_List.Count; i++)
                if (comparison(m_List[i - 1], m_List[i]) > 0)
                {
                    m_List.Sort(comparison);
                    __IncrementVersion();
                    return true;
                }
            return false;
        }

        /// <summary>Sorts a range only when it is not already ordered; returns whether its order changed.</summary>
        public bool SortIfNeeded(int index, int count, IComparer<T> comparer)
        {
            __ValidateRange(index, count);
            if (count <= 1) return false;
            comparer ??= Comparer<T>.Default;
            int end = index + count;
            for (int i = index + 1; i < end; i++)
                if (comparer.Compare(m_List[i - 1], m_List[i]) > 0)
                {
                    m_List.Sort(index, count, comparer);
                    __IncrementVersion();
                    return true;
                }
            return false;
        }

        /// <summary>Sets the capacity to the actual number of elements in the list.</summary>
        public void TrimExcess() => m_List.TrimExcess();

        /// <summary>Gets or sets the total number of elements the internal data structure can hold.</summary>
        public int Capacity
        {
            get => m_List.Capacity;
            set => m_List.Capacity = value;
        }
    }
}
