#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;

namespace ReactiveBinding
{
    /// <summary>
    /// A set that tracks modifications via a version number. The <see cref="__Version"/> property increments on
    /// each Add, Remove, or Clear operation. If elements implement <see cref="IVersion"/> they share this container
    /// as <see cref="__Parent"/>, so element changes bubble up the version chain. This is the version-tracking-only
    /// container; for flat-registry data synchronization use the separate <see cref="VersionSyncHashSet{T}"/>.
    /// </summary>
    /// <typeparam name="T">The type of elements in the set.</typeparam>
    public class VersionHashSet<T> : ISet<T>, IReadOnlyCollection<T>, IVersion
    {
        private readonly HashSet<T> m_Set;
        private int m_Version;

        /// <summary>Creates a new empty VersionHashSet.</summary>
        public VersionHashSet() { m_Set = new HashSet<T>(); }

        /// <summary>Creates a new VersionHashSet with the specified comparer.</summary>
        public VersionHashSet(IEqualityComparer<T> comparer) { m_Set = new HashSet<T>(comparer); }

        /// <summary>Creates a new VersionHashSet containing elements from the specified collection.</summary>
        public VersionHashSet(IEnumerable<T> collection)
        {
            m_Set = new HashSet<T>(collection);
            foreach (var item in m_Set) if (item is IVersion v) v.__Parent = this;
        }

        /// <summary>Creates a new VersionHashSet containing elements from the specified collection with the specified comparer.</summary>
        public VersionHashSet(IEnumerable<T> collection, IEqualityComparer<T> comparer)
        {
            m_Set = new HashSet<T>(collection, comparer);
            foreach (var item in m_Set) if (item is IVersion v) v.__Parent = this;
        }

#if UNITY_2021_2_OR_NEWER || NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
        /// <summary>Creates a new VersionHashSet with the specified initial capacity.</summary>
        public VersionHashSet(int capacity) { m_Set = new HashSet<T>(capacity); }

        /// <summary>Creates a new VersionHashSet with the specified initial capacity and comparer.</summary>
        public VersionHashSet(int capacity, IEqualityComparer<T> comparer) { m_Set = new HashSet<T>(capacity, comparer); }
#endif

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
            foreach (var item in m_Set)
                if (item is IVersion v) v.__Reset();   // recurse version elements
            m_Version = 0; __Parent = null;   // keeps contents; detaches for reuse
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
                if (item is IVersion v) v.__Parent = this;
                __IncrementVersion();
            }
            return added;
        }

        /// <inheritdoc/>
        void ICollection<T>.Add(T item) => Add(item);   // route the explicit-interface add through the tracked Add

        /// <inheritdoc/>
        public void Clear()
        {
            foreach (var item in m_Set) if (item is IVersion v) v.__Parent = null;
            m_Set.Clear();
            __IncrementVersion();
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
                if (m_Set.Remove(item)) if (item is IVersion v) v.__Parent = null;
            }
            if (m_Set.Count != countBefore) __IncrementVersion();
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
                if (item is IVersion v) v.__Parent = null;
                return true;
            });
            if (m_Set.Count != countBefore) __IncrementVersion();
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
                if (item is IVersion v) v.__Parent = null;
                __IncrementVersion();
            }
            return removed;
        }

        /// <summary>Removes all elements that match the conditions defined by the specified predicate.</summary>
        public int RemoveWhere(Predicate<T> match)
        {
            var count = m_Set.RemoveWhere(item =>
            {
                if (!match(item)) return false;
                if (item is IVersion v) v.__Parent = null;
                return true;
            });
            if (count > 0) __IncrementVersion();
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
                    if (item is IVersion v) v.__Parent = null;
                    changed = true;
                }
                else
                {
                    m_Set.Add(item);
                    if (item is IVersion v) v.__Parent = this;
                    changed = true;
                }
            }
            if (changed) __IncrementVersion();
        }

        /// <inheritdoc/>
        public void UnionWith(IEnumerable<T> other)
        {
            var countBefore = m_Set.Count;
            foreach (var item in other)
            {
                if (m_Set.Add(item)) if (item is IVersion v) v.__Parent = this;
            }
            if (m_Set.Count != countBefore) __IncrementVersion();
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
    }
}
