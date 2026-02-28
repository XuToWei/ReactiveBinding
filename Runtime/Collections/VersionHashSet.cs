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
                IncrementVersion();
            }
            return added;
        }

        /// <inheritdoc/>
        void ICollection<T>.Add(T item)
        {
            if (m_Set.Add(item))
            {
                AssignParent(item);
                IncrementVersion();
            }
        }

        /// <inheritdoc/>
        public void Clear()
        {
            foreach (var item in m_Set)
            {
                ClearParent(item);
            }
            m_Set.Clear();
            IncrementVersion();
        }

        /// <inheritdoc/>
        public bool Contains(T item) => m_Set.Contains(item);

        /// <inheritdoc/>
        public void CopyTo(T[] array, int arrayIndex) => m_Set.CopyTo(array, arrayIndex);

        /// <inheritdoc/>
        public void ExceptWith(IEnumerable<T> other)
        {
            var otherSet = other is HashSet<T> hs ? hs : new HashSet<T>(other);
            foreach (var item in m_Set)
            {
                if (otherSet.Contains(item))
                {
                    ClearParent(item);
                }
            }
            var countBefore = m_Set.Count;
            m_Set.ExceptWith(other);
            if (m_Set.Count != countBefore)
            {
                IncrementVersion();
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
            foreach (var item in m_Set)
            {
                if (!otherSet.Contains(item))
                {
                    ClearParent(item);
                }
            }
            var countBefore = m_Set.Count;
            m_Set.IntersectWith(other);
            if (m_Set.Count != countBefore)
            {
                IncrementVersion();
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
                IncrementVersion();
            }
            return removed;
        }

        /// <summary>
        /// Removes all elements that match the conditions defined by the specified predicate.
        /// </summary>
        public int RemoveWhere(Predicate<T> match)
        {
            foreach (var item in m_Set)
            {
                if (match(item))
                {
                    ClearParent(item);
                }
            }
            var count = m_Set.RemoveWhere(match);
            if (count > 0)
            {
                IncrementVersion();
            }
            return count;
        }

        /// <inheritdoc/>
        public bool SetEquals(IEnumerable<T> other) => m_Set.SetEquals(other);

        /// <inheritdoc/>
        public void SymmetricExceptWith(IEnumerable<T> other)
        {
            var otherSet = other is HashSet<T> hs ? hs : new HashSet<T>(other);
            foreach (var item in m_Set)
            {
                if (otherSet.Contains(item))
                {
                    ClearParent(item);
                }
            }
            m_Set.SymmetricExceptWith(other);
            foreach (var item in m_Set)
            {
                AssignParent(item);
            }
            IncrementVersion();
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
                }
            }
            if (m_Set.Count != countBefore)
            {
                IncrementVersion();
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
