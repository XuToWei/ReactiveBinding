using System;
using System.Collections;
using System.Collections.Generic;

namespace ReactiveBinding
{
    /// <summary>
    /// A list implementation that tracks modifications via a version number.
    /// The Version property increments on each Add, Remove, Insert, Clear, or index set operation.
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
        }

        /// <inheritdoc/>
        public int Version => m_Version;

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
                m_List[index] = value;
                m_Version++;
            }
        }

        /// <summary>
        /// Manually increments the version number to trigger change detection.
        /// Use this when modifying items in the list without using list methods.
        /// </summary>
        public void IncrementVersion()
        {
            m_Version++;
        }

        /// <inheritdoc/>
        public void Add(T item)
        {
            m_List.Add(item);
            m_Version++;
        }

        /// <summary>
        /// Adds the elements of the specified collection to the end of the list.
        /// </summary>
        public void AddRange(IEnumerable<T> collection)
        {
            m_List.AddRange(collection);
            m_Version++;
        }

        /// <inheritdoc/>
        public void Clear()
        {
            m_List.Clear();
            m_Version++;
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
            m_Version++;
        }

        /// <summary>
        /// Inserts the elements of a collection at the specified index.
        /// </summary>
        public void InsertRange(int index, IEnumerable<T> collection)
        {
            m_List.InsertRange(index, collection);
            m_Version++;
        }

        /// <inheritdoc/>
        public bool Remove(T item)
        {
            var removed = m_List.Remove(item);
            if (removed)
            {
                m_Version++;
            }
            return removed;
        }

        /// <inheritdoc/>
        public void RemoveAt(int index)
        {
            m_List.RemoveAt(index);
            m_Version++;
        }

        /// <summary>
        /// Removes a range of elements from the list.
        /// </summary>
        public void RemoveRange(int index, int count)
        {
            m_List.RemoveRange(index, count);
            m_Version++;
        }

        /// <summary>
        /// Removes all elements that match the conditions defined by the specified predicate.
        /// </summary>
        public int RemoveAll(Predicate<T> match)
        {
            var count = m_List.RemoveAll(match);
            if (count > 0)
            {
                m_Version++;
            }
            return count;
        }

        /// <summary>
        /// Reverses the order of the elements in the list.
        /// </summary>
        public void Reverse()
        {
            m_List.Reverse();
            m_Version++;
        }

        /// <summary>
        /// Reverses the order of the elements in the specified range.
        /// </summary>
        public void Reverse(int index, int count)
        {
            m_List.Reverse(index, count);
            m_Version++;
        }

        /// <summary>
        /// Sorts the elements in the list using the default comparer.
        /// </summary>
        public void Sort()
        {
            m_List.Sort();
            m_Version++;
        }

        /// <summary>
        /// Sorts the elements in the list using the specified comparison.
        /// </summary>
        public void Sort(Comparison<T> comparison)
        {
            m_List.Sort(comparison);
            m_Version++;
        }

        /// <summary>
        /// Sorts the elements in the list using the specified comparer.
        /// </summary>
        public void Sort(IComparer<T> comparer)
        {
            m_List.Sort(comparer);
            m_Version++;
        }

        /// <summary>
        /// Sorts a range of elements using the specified comparer.
        /// </summary>
        public void Sort(int index, int count, IComparer<T> comparer)
        {
            m_List.Sort(index, count, comparer);
            m_Version++;
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
    }
}
