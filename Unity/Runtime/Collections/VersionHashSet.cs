#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;

namespace ReactiveBinding
{
    /// <summary>
    /// A set that tracks modifications via a version number. The <see cref="__Version"/> property increments on
    /// each Add, Remove, or Clear operation. If elements implement <see cref="IVersion"/> they share this container
    /// as <see cref="__Parent"/>, so element changes bubble up the version chain. Equality follows
    /// <see cref="EqualityComparer{T}.Default"/> and custom comparers are not supported. As with
    /// <see cref="HashSet{T}"/>, fields used by an element's Equals/GetHashCode implementation must remain stable
    /// while the element is in the set. This is the version-tracking-only container; for flat-registry data
    /// synchronization use the separate <see cref="VersionSyncHashSet{T}"/>.
    /// </summary>
    /// <typeparam name="T">The type of elements in the set.</typeparam>
    public class VersionHashSet<T> : ISet<T>, IReadOnlyCollection<T>, IVersion
    {
        private readonly HashSet<T> m_Set;

        /// <summary>Creates a new empty VersionHashSet.</summary>
        public VersionHashSet() { m_Set = new HashSet<T>(EqualityComparer<T>.Default); }

        /// <summary>Creates a new VersionHashSet containing elements from the specified collection.</summary>
        public VersionHashSet(IEnumerable<T> collection)
        {
            m_Set = new HashSet<T>(collection, EqualityComparer<T>.Default);
            VersionOwnership.EnsureCanAttachAll(this, m_Set);
            foreach (var item in m_Set) if (item is IVersion v) v.__Parent = this;
        }

#if UNITY_2021_2_OR_NEWER || NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
        /// <summary>Creates a new VersionHashSet with the specified initial capacity.</summary>
        public VersionHashSet(int capacity) { m_Set = new HashSet<T>(capacity, EqualityComparer<T>.Default); }
#endif

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
            foreach (var item in m_Set)
                if (item is IVersion v)
                {
                    v.__Reset();
                    v.__Parent = this;
                }
            __Version = 0; __Parent = null;   // keeps contents; detaches for reuse
        }

        /// <inheritdoc/>
        public void Reset() => __Reset();

        private bool __TryRemoveStoredValue(T equalValue, out T storedValue)
        {
#if UNITY_2021_2_OR_NEWER || NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
            if (!m_Set.TryGetValue(equalValue, out storedValue)) return false;
#else
            bool found = false;
            storedValue = default;
            foreach (var candidate in m_Set)
            {
                if (!m_Set.Comparer.Equals(candidate, equalValue)) continue;
                storedValue = candidate;
                found = true;
                break;
            }
            if (!found) return false;
#endif
            return m_Set.Remove(storedValue);
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
            foreach (var item in m_Set)
                if (item is IVersion v) v.__Parent = null;
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
            if (ReferenceEquals(other, this)) { Clear(); return; }
            int removed = 0;
            foreach (var item in other)
            {
                if (!__TryRemoveStoredValue(item, out var stored)) continue;
                if (stored is IVersion v) v.__Parent = null;
                removed++;
            }
            if (removed == 0) return;
            __IncrementVersion();
        }

        /// <inheritdoc/>
        public HashSet<T>.Enumerator GetEnumerator() => m_Set.GetEnumerator();

        /// <inheritdoc/>
        IEnumerator<T> IEnumerable<T>.GetEnumerator() => m_Set.GetEnumerator();

        /// <inheritdoc/>
        IEnumerator IEnumerable.GetEnumerator() => m_Set.GetEnumerator();

        /// <inheritdoc/>
        public void IntersectWith(IEnumerable<T> other)
        {
            if (ReferenceEquals(other, this)) return;
            HashSet<T> otherSet;
            if (other is HashSet<T> hashSet && m_Set.Comparer.Equals(hashSet.Comparer)) otherSet = hashSet;
            else if (other is VersionHashSet<T> versionSet && m_Set.Comparer.Equals(versionSet.m_Set.Comparer))
                otherSet = versionSet.m_Set;
            else otherSet = new HashSet<T>(other, m_Set.Comparer);
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
            if (!__TryRemoveStoredValue(item, out var storedItem)) return false;
            if (storedItem is IVersion v) v.__Parent = null;
            __IncrementVersion();
            return true;
        }

        /// <summary>Removes all elements that match the conditions defined by the specified predicate.</summary>
        public int RemoveWhere(Predicate<T> match)
        {
            if (match == null) throw new ArgumentNullException(nameof(match));
            int removed = m_Set.RemoveWhere(item =>
            {
                if (!match(item)) return false;
                if (item is IVersion v) v.__Parent = null;
                return true;
            });
            if (removed == 0) return 0;
            __IncrementVersion();
            return removed;
        }

        /// <inheritdoc/>
        public bool SetEquals(IEnumerable<T> other) => m_Set.SetEquals(other);

        /// <inheritdoc/>
        public void SymmetricExceptWith(IEnumerable<T> other)
        {
            if (ReferenceEquals(other, this)) { Clear(); return; }
            HashSet<T> otherSet;
            if (other is HashSet<T> hashSet && m_Set.Comparer.Equals(hashSet.Comparer)) otherSet = hashSet;
            else if (other is VersionHashSet<T> versionSet && m_Set.Comparer.Equals(versionSet.m_Set.Comparer))
                otherSet = versionSet.m_Set;
            else otherSet = new HashSet<T>(other, m_Set.Comparer);
            bool changed = false;
            foreach (var item in otherSet)
            {
                if (__TryRemoveStoredValue(item, out var stored))
                {
                    if (stored is IVersion oldVersion) oldVersion.__Parent = null;
                }
                else
                {
                    if (item is IVersion child) VersionOwnership.EnsureCanAttach(this, child);
                    m_Set.Add(item);
                    if (item is IVersion newVersion) newVersion.__Parent = this;
                }
                changed = true;
            }
            if (changed) __IncrementVersion();
        }

        /// <inheritdoc/>
        public void UnionWith(IEnumerable<T> other)
        {
            bool changed = false;
            foreach (var item in other)
            {
                if (m_Set.Contains(item)) continue;
                if (item is IVersion child) VersionOwnership.EnsureCanAttach(this, child);
                m_Set.Add(item);
                if (item is IVersion v) v.__Parent = this;
                changed = true;
            }
            if (changed) __IncrementVersion();
        }

        /// <summary>Sets the capacity to the actual number of elements in the set.</summary>
        public void TrimExcess()
        {
#if UNITY_2021_2_OR_NEWER || NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
            m_Set.TrimExcess();
#endif
        }

        /// <summary>Gets the default comparer used to determine equality of values.</summary>
        public IEqualityComparer<T> Comparer => m_Set.Comparer;
    }
}
