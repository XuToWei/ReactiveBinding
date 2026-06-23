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
        private int m_Version;

        /// <summary>Creates a new empty VersionList.</summary>
        public VersionList() { m_List = new List<T>(); }

        /// <summary>Creates a new VersionList with the specified initial capacity.</summary>
        public VersionList(int capacity) { m_List = new List<T>(capacity); }

        /// <summary>Creates a new VersionList containing elements from the specified collection.</summary>
        public VersionList(IEnumerable<T> collection)
        {
            m_List = new List<T>(collection);
            foreach (var item in m_List) if (item is IVersion v) v.__Parent = this;
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
        public void __Reset()
        {
            for (int i = 0; i < m_List.Count; i++)
                if (m_List[i] is IVersion v) v.__Reset();   // recurse version elements
            m_Version = 0; __Parent = null;   // keeps contents; detaches for reuse
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
                m_List[index] = value;
                if (value is IVersion nv) nv.__Parent = this;
                __IncrementVersion();
            }
        }

        /// <inheritdoc/>
        public void Add(T item)
        {
            m_List.Add(item);
            if (item is IVersion v) v.__Parent = this;
            __IncrementVersion();
        }

        /// <summary>Adds the elements of the specified collection to the end of the list.</summary>
        public void AddRange(IEnumerable<T> collection)
        {
            var items = collection is ICollection<T> c ? c : new List<T>(collection);
            m_List.AddRange(items);
            foreach (var item in items) if (item is IVersion v) v.__Parent = this;
            __IncrementVersion();
        }

        /// <inheritdoc/>
        public void Clear()
        {
            foreach (var item in m_List) if (item is IVersion v) v.__Parent = null;
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
            m_List.Insert(index, item);
            if (item is IVersion v) v.__Parent = this;
            __IncrementVersion();
        }

        /// <summary>Inserts the elements of a collection at the specified index.</summary>
        public void InsertRange(int index, IEnumerable<T> collection)
        {
            var items = collection is ICollection<T> c ? c : new List<T>(collection);
            m_List.InsertRange(index, items);
            foreach (var item in items) if (item is IVersion v) v.__Parent = this;
            __IncrementVersion();
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
            for (int i = index; i < index + count; i++) if (m_List[i] is IVersion v) v.__Parent = null;
            m_List.RemoveRange(index, count);
            __IncrementVersion();
        }

        /// <summary>Removes all elements that match the conditions defined by the specified predicate.</summary>
        public int RemoveAll(Predicate<T> match)
        {
            var count = m_List.RemoveAll(item =>
            {
                if (!match(item)) return false;
                if (item is IVersion v) v.__Parent = null;
                return true;
            });
            if (count > 0) __IncrementVersion();
            return count;
        }

        /// <summary>Reverses the order of the elements in the list.</summary>
        public void Reverse() { m_List.Reverse(); __IncrementVersion(); }

        /// <summary>Reverses the order of the elements in the specified range.</summary>
        public void Reverse(int index, int count) { m_List.Reverse(index, count); __IncrementVersion(); }

        /// <summary>Sorts the elements in the list using the default comparer.</summary>
        public void Sort() { m_List.Sort(); __IncrementVersion(); }

        /// <summary>Sorts the elements in the list using the specified comparison.</summary>
        public void Sort(Comparison<T> comparison) { m_List.Sort(comparison); __IncrementVersion(); }

        /// <summary>Sorts the elements in the list using the specified comparer.</summary>
        public void Sort(IComparer<T> comparer) { m_List.Sort(comparer); __IncrementVersion(); }

        /// <summary>Sorts a range of elements using the specified comparer.</summary>
        public void Sort(int index, int count, IComparer<T> comparer) { m_List.Sort(index, count, comparer); __IncrementVersion(); }

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
