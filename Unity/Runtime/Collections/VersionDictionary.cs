using System.Collections;
using System.Collections.Generic;

namespace ReactiveBinding
{
    /// <summary>
    /// A dictionary that tracks modifications via a version number. The <see cref="__Version"/> property increments
    /// on each Add, Remove, Set, or Clear operation. If values implement <see cref="IVersion"/> they share this
    /// container as <see cref="__Parent"/>, so value changes bubble up the version chain. This is the
    /// version-tracking-only container; for flat-registry data synchronization use the separate
    /// <see cref="VersionSyncDictionary{TKey, TValue}"/>.
    /// </summary>
    /// <typeparam name="TKey">The type of keys in the dictionary.</typeparam>
    /// <typeparam name="TValue">The type of values in the dictionary.</typeparam>
    public class VersionDictionary<TKey, TValue> : IDictionary<TKey, TValue>, IReadOnlyDictionary<TKey, TValue>, IVersion
        where TKey : notnull
    {
        private readonly Dictionary<TKey, TValue> m_Dict;
        private int m_Version;

        /// <summary>Creates a new empty VersionDictionary.</summary>
        public VersionDictionary() { m_Dict = new Dictionary<TKey, TValue>(); }

        /// <summary>Creates a new VersionDictionary with the specified initial capacity.</summary>
        public VersionDictionary(int capacity) { m_Dict = new Dictionary<TKey, TValue>(capacity); }

        /// <summary>Creates a new VersionDictionary with the specified comparer.</summary>
        public VersionDictionary(IEqualityComparer<TKey> comparer) { m_Dict = new Dictionary<TKey, TValue>(comparer); }

        /// <summary>Creates a new VersionDictionary with the specified capacity and comparer.</summary>
        public VersionDictionary(int capacity, IEqualityComparer<TKey> comparer) { m_Dict = new Dictionary<TKey, TValue>(capacity, comparer); }

        /// <summary>Creates a new VersionDictionary containing elements from the specified dictionary.</summary>
        public VersionDictionary(IDictionary<TKey, TValue> dictionary)
        {
            m_Dict = new Dictionary<TKey, TValue>(dictionary);
            foreach (var value in m_Dict.Values) if (value is IVersion v) v.__Parent = this;
        }

        /// <summary>Creates a new VersionDictionary containing elements from the specified dictionary with the specified comparer.</summary>
        public VersionDictionary(IDictionary<TKey, TValue> dictionary, IEqualityComparer<TKey> comparer)
        {
            m_Dict = new Dictionary<TKey, TValue>(dictionary, comparer);
            foreach (var value in m_Dict.Values) if (value is IVersion v) v.__Parent = this;
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
            foreach (var value in m_Dict.Values)
                if (value is IVersion v) v.__Reset();   // recurse version values
            m_Version = 0; __Parent = null;   // keeps contents; detaches for reuse
        }

        /// <inheritdoc/>
        public int Count => m_Dict.Count;

        /// <inheritdoc/>
        public bool IsReadOnly => false;

        /// <inheritdoc/>
        public ICollection<TKey> Keys => m_Dict.Keys;

        /// <inheritdoc/>
        public ICollection<TValue> Values => m_Dict.Values;

        /// <inheritdoc/>
        IEnumerable<TKey> IReadOnlyDictionary<TKey, TValue>.Keys => m_Dict.Keys;

        /// <inheritdoc/>
        IEnumerable<TValue> IReadOnlyDictionary<TKey, TValue>.Values => m_Dict.Values;

        /// <inheritdoc/>
        public TValue this[TKey key]
        {
            get => m_Dict[key];
            set
            {
                if (m_Dict.TryGetValue(key, out var oldValue))
                {
                    if (EqualityComparer<TValue>.Default.Equals(oldValue, value)) return;
                    if (oldValue is IVersion ov) ov.__Parent = null;
                }
                m_Dict[key] = value;
                if (value is IVersion v) v.__Parent = this;
                __IncrementVersion();
            }
        }

        /// <inheritdoc/>
        public void Add(TKey key, TValue value)
        {
            m_Dict.Add(key, value);
            if (value is IVersion v) v.__Parent = this;
            __IncrementVersion();
        }

        /// <inheritdoc/>
        public void Add(KeyValuePair<TKey, TValue> item)
        {
            ((ICollection<KeyValuePair<TKey, TValue>>)m_Dict).Add(item);
            if (item.Value is IVersion v) v.__Parent = this;
            __IncrementVersion();
        }

        /// <inheritdoc/>
        public void Clear()
        {
            foreach (var kvp in m_Dict) if (kvp.Value is IVersion v) v.__Parent = null;
            m_Dict.Clear();
            __IncrementVersion();
        }

        /// <inheritdoc/>
        public bool Contains(KeyValuePair<TKey, TValue> item)
        {
            return ((ICollection<KeyValuePair<TKey, TValue>>)m_Dict).Contains(item);
        }

        /// <inheritdoc/>
        public bool ContainsKey(TKey key) => m_Dict.ContainsKey(key);

        /// <summary>Determines whether the dictionary contains a specific value.</summary>
        public bool ContainsValue(TValue value) => m_Dict.ContainsValue(value);

        /// <inheritdoc/>
        public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
        {
            ((ICollection<KeyValuePair<TKey, TValue>>)m_Dict).CopyTo(array, arrayIndex);
        }

        /// <inheritdoc/>
        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => m_Dict.GetEnumerator();

        /// <inheritdoc/>
        IEnumerator IEnumerable.GetEnumerator() => m_Dict.GetEnumerator();

        /// <inheritdoc/>
        public bool Remove(TKey key)
        {
            if (m_Dict.TryGetValue(key, out var value))
            {
                if (value is IVersion v) v.__Parent = null;
                m_Dict.Remove(key);
                __IncrementVersion();
                return true;
            }
            return false;
        }

        /// <inheritdoc/>
        public bool Remove(KeyValuePair<TKey, TValue> item)
        {
            var removed = ((ICollection<KeyValuePair<TKey, TValue>>)m_Dict).Remove(item);
            if (removed)
            {
                if (item.Value is IVersion v) v.__Parent = null;
                __IncrementVersion();
            }
            return removed;
        }

        /// <inheritdoc/>
        public bool TryGetValue(TKey key, out TValue value)
        {
            return m_Dict.TryGetValue(key, out value!);
        }

        /// <summary>Attempts to add the specified key and value to the dictionary.</summary>
        /// <returns>true if the key/value pair was added successfully; false if the key already exists.</returns>
        public bool TryAdd(TKey key, TValue value)
        {
#if UNITY_2021_2_OR_NEWER || NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
            var added = m_Dict.TryAdd(key, value);
#else
            if (m_Dict.ContainsKey(key))
            {
                return false;
            }
            m_Dict.Add(key, value);
            var added = true;
#endif
            if (added)
            {
                if (value is IVersion v) v.__Parent = this;
                __IncrementVersion();
            }
            return added;
        }

        /// <summary>Gets the comparer used to determine equality of keys.</summary>
        public IEqualityComparer<TKey> Comparer => m_Dict.Comparer;
    }
}
