using System;
using System.Collections;
using System.Collections.Generic;

namespace ReactiveBinding
{
    /// <summary>
    /// A set implementation that tracks modifications via a version number.
    /// The Version property increments on each Add, Remove, or Clear operation.
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
        public VersionHashSet(IEqualityComparer<T>? comparer)
        {
            m_Set = new HashSet<T>(comparer);
        }

        /// <summary>
        /// Creates a new VersionHashSet containing elements from the specified collection.
        /// </summary>
        public VersionHashSet(IEnumerable<T> collection)
        {
            m_Set = new HashSet<T>(collection);
        }

        /// <summary>
        /// Creates a new VersionHashSet containing elements from the specified collection with the specified comparer.
        /// </summary>
        public VersionHashSet(IEnumerable<T> collection, IEqualityComparer<T>? comparer)
        {
            m_Set = new HashSet<T>(collection, comparer);
        }

#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
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
        public VersionHashSet(int capacity, IEqualityComparer<T>? comparer)
        {
            m_Set = new HashSet<T>(capacity, comparer);
        }
#endif

        /// <inheritdoc/>
        public int Version => m_Version;

        /// <inheritdoc/>
        public int Count => m_Set.Count;

        /// <inheritdoc/>
        public bool IsReadOnly => false;

        /// <summary>
        /// Manually increments the version number to trigger change detection.
        /// Use this when modifying items in the set without using set methods.
        /// </summary>
        public void IncrementVersion()
        {
            m_Version++;
        }

        /// <inheritdoc/>
        public bool Add(T item)
        {
            var added = m_Set.Add(item);
            if (added)
            {
                m_Version++;
            }
            return added;
        }

        /// <inheritdoc/>
        void ICollection<T>.Add(T item)
        {
            if (m_Set.Add(item))
            {
                m_Version++;
            }
        }

        /// <inheritdoc/>
        public void Clear()
        {
            m_Set.Clear();
            m_Version++;
        }

        /// <inheritdoc/>
        public bool Contains(T item) => m_Set.Contains(item);

        /// <inheritdoc/>
        public void CopyTo(T[] array, int arrayIndex) => m_Set.CopyTo(array, arrayIndex);

        /// <inheritdoc/>
        public void ExceptWith(IEnumerable<T> other)
        {
            var countBefore = m_Set.Count;
            m_Set.ExceptWith(other);
            if (m_Set.Count != countBefore)
            {
                m_Version++;
            }
        }

        /// <inheritdoc/>
        public IEnumerator<T> GetEnumerator() => m_Set.GetEnumerator();

        /// <inheritdoc/>
        IEnumerator IEnumerable.GetEnumerator() => m_Set.GetEnumerator();

        /// <inheritdoc/>
        public void IntersectWith(IEnumerable<T> other)
        {
            var countBefore = m_Set.Count;
            m_Set.IntersectWith(other);
            if (m_Set.Count != countBefore)
            {
                m_Version++;
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
                m_Version++;
            }
            return removed;
        }

        /// <summary>
        /// Removes all elements that match the conditions defined by the specified predicate.
        /// </summary>
        public int RemoveWhere(Predicate<T> match)
        {
            var count = m_Set.RemoveWhere(match);
            if (count > 0)
            {
                m_Version++;
            }
            return count;
        }

        /// <inheritdoc/>
        public bool SetEquals(IEnumerable<T> other) => m_Set.SetEquals(other);

        /// <inheritdoc/>
        public void SymmetricExceptWith(IEnumerable<T> other)
        {
            var countBefore = m_Set.Count;
            m_Set.SymmetricExceptWith(other);
            // SymmetricExceptWith may change content without changing count
            // We always increment version for simplicity
            m_Version++;
        }

        /// <inheritdoc/>
        public void UnionWith(IEnumerable<T> other)
        {
            var countBefore = m_Set.Count;
            m_Set.UnionWith(other);
            if (m_Set.Count != countBefore)
            {
                m_Version++;
            }
        }

        /// <summary>
        /// Sets the capacity to the actual number of elements in the set.
        /// </summary>
        public void TrimExcess()
        {
#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
            m_Set.TrimExcess();
#endif
        }

        /// <summary>
        /// Gets the comparer used to determine equality of values.
        /// </summary>
        public IEqualityComparer<T> Comparer => m_Set.Comparer;
    }
}
