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
            VersionOwnership.EnsureCanAttachAll(this, m_Set);
            foreach (var item in m_Set) if (item is IVersion v) v.__Parent = this;
        }

        /// <summary>Creates a new VersionHashSet containing elements from the specified collection with the specified comparer.</summary>
        public VersionHashSet(IEnumerable<T> collection, IEqualityComparer<T> comparer)
        {
            m_Set = new HashSet<T>(collection, comparer);
            VersionOwnership.EnsureCanAttachAll(this, m_Set);
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
                if (item is IVersion v)
                {
                    v.__Reset();
                    v.__Parent = this;
                }
            m_Version = 0; __Parent = null;   // keeps contents; detaches for reuse
        }

        private bool __TryGetStoredValue(T equalValue, out T storedValue)
        {
            foreach (var candidate in m_Set)
            {
                if (!m_Set.Comparer.Equals(candidate, equalValue)) continue;
                storedValue = candidate;
                return true;
            }
            storedValue = default;
            return false;
        }

        /// <inheritdoc/>
        public int Count => m_Set.Count;

        /// <inheritdoc/>
        public bool IsReadOnly => false;

        /// <inheritdoc/>
        public bool Add(T item)
        {
            if (m_Set.Contains(item)) return false;
            if (item is IVersion child) VersionOwnership.EnsureCanAttach(this, child);
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
            if (m_Set.Count == 0) return;
            var removed = new List<T>(m_Set);
            m_Set.Clear();
            foreach (var item in removed) if (item is IVersion v) v.__Parent = null;
            __IncrementVersion();
        }

        /// <inheritdoc/>
        public bool Contains(T item) => m_Set.Contains(item);

        /// <inheritdoc/>
        public void CopyTo(T[] array, int arrayIndex) => m_Set.CopyTo(array, arrayIndex);

        /// <inheritdoc/>
        public void ExceptWith(IEnumerable<T> other)
        {
            var otherSet = new HashSet<T>(other, m_Set.Comparer);
            var removed = new List<T>();
            foreach (var stored in m_Set)
            {
                if (otherSet.Contains(stored)) removed.Add(stored);
            }
            if (removed.Count == 0) return;
            foreach (var stored in removed)
            {
                m_Set.Remove(stored);
                if (stored is IVersion v) v.__Parent = null;
            }
            __IncrementVersion();
        }

        /// <inheritdoc/>
        public IEnumerator<T> GetEnumerator() => m_Set.GetEnumerator();

        /// <inheritdoc/>
        IEnumerator IEnumerable.GetEnumerator() => m_Set.GetEnumerator();

        /// <inheritdoc/>
        public void IntersectWith(IEnumerable<T> other)
        {
            var otherSet = new HashSet<T>(other, m_Set.Comparer);
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
            if (!__TryGetStoredValue(item, out var storedItem)) return false;
            m_Set.Remove(storedItem);
            if (storedItem is IVersion v) v.__Parent = null;
            __IncrementVersion();
            return true;
        }

        /// <summary>Removes all elements that match the conditions defined by the specified predicate.</summary>
        public int RemoveWhere(Predicate<T> match)
        {
            if (match == null) throw new ArgumentNullException(nameof(match));
            var removed = new List<T>();
            foreach (var item in m_Set)
            {
                if (match(item)) removed.Add(item);
            }
            if (removed.Count == 0) return 0;
            foreach (var item in removed)
            {
                m_Set.Remove(item);
                if (item is IVersion v) v.__Parent = null;
            }
            __IncrementVersion();
            return removed.Count;
        }

        /// <inheritdoc/>
        public bool SetEquals(IEnumerable<T> other) => m_Set.SetEquals(other);

        /// <inheritdoc/>
        public void SymmetricExceptWith(IEnumerable<T> other)
        {
            var otherSet = new HashSet<T>(other, m_Set.Comparer);
            var removed = new List<T>();
            var added = new List<T>();
            foreach (var item in otherSet)
            {
                if (__TryGetStoredValue(item, out var stored)) removed.Add(stored);
                else added.Add(item);
            }
            VersionOwnership.EnsureCanAttachAll(this, added);
            if (removed.Count == 0 && added.Count == 0) return;
            foreach (var item in removed)
            {
                m_Set.Remove(item);
                if (item is IVersion v) v.__Parent = null;
            }
            foreach (var item in added)
            {
                m_Set.Add(item);
                if (item is IVersion v) v.__Parent = this;
            }
            __IncrementVersion();
        }

        /// <inheritdoc/>
        public void UnionWith(IEnumerable<T> other)
        {
            var incoming = new HashSet<T>(other, m_Set.Comparer);
            var added = new List<T>();
            foreach (var item in incoming)
            {
                if (!m_Set.Contains(item)) added.Add(item);
            }
            if (added.Count == 0) return;
            VersionOwnership.EnsureCanAttachAll(this, added);
            foreach (var item in added)
            {
                m_Set.Add(item);
                if (item is IVersion v) v.__Parent = this;
            }
            __IncrementVersion();
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
