#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;

namespace ReactiveBinding
{
    /// <summary>
    /// A dictionary implementation that tracks modifications via a version number.
    /// The Version property increments on each Add, Remove, Set, or Clear operation.
    /// If values implement IVersion, they will share this container as Parent,
    /// allowing value property changes to automatically increment the container's version.
    /// </summary>
    /// <typeparam name="TKey">The type of keys in the dictionary.</typeparam>
    /// <typeparam name="TValue">The type of values in the dictionary.</typeparam>
    public class VersionDictionary<TKey, TValue> : IDictionary<TKey, TValue>, IReadOnlyDictionary<TKey, TValue>, IVersion
        where TKey : notnull
    {
        private readonly Dictionary<TKey, TValue> m_Dictionary;
        private int m_Version;

        /// <summary>
        /// Creates a new empty VersionDictionary.
        /// </summary>
        public VersionDictionary()
        {
            m_Dictionary = new Dictionary<TKey, TValue>();
        }

        /// <summary>
        /// Creates a new VersionDictionary with the specified initial capacity.
        /// </summary>
        public VersionDictionary(int capacity)
        {
            m_Dictionary = new Dictionary<TKey, TValue>(capacity);
        }

        /// <summary>
        /// Creates a new VersionDictionary with the specified comparer.
        /// </summary>
        public VersionDictionary(IEqualityComparer<TKey> comparer)
        {
            m_Dictionary = new Dictionary<TKey, TValue>(comparer);
        }

        /// <summary>
        /// Creates a new VersionDictionary with the specified capacity and comparer.
        /// </summary>
        public VersionDictionary(int capacity, IEqualityComparer<TKey> comparer)
        {
            m_Dictionary = new Dictionary<TKey, TValue>(capacity, comparer);
        }

        /// <summary>
        /// Creates a new VersionDictionary containing elements from the specified dictionary.
        /// </summary>
        public VersionDictionary(IDictionary<TKey, TValue> dictionary)
        {
            m_Dictionary = new Dictionary<TKey, TValue>(dictionary);
            foreach (var value in m_Dictionary.Values)
            {
                AssignParent(value);
            }
        }

        /// <summary>
        /// Creates a new VersionDictionary containing elements from the specified dictionary with the specified comparer.
        /// </summary>
        public VersionDictionary(IDictionary<TKey, TValue> dictionary, IEqualityComparer<TKey> comparer)
        {
            m_Dictionary = new Dictionary<TKey, TValue>(dictionary, comparer);
            foreach (var value in m_Dictionary.Values)
            {
                AssignParent(value);
            }
        }

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

        private void AssignParent(TValue item)
        {
            if (item is IVersion v) v.Parent = this;
        }

        private void ClearParent(TValue item)
        {
            if (item is IVersion v) v.Parent = null;
        }

        /// <inheritdoc/>
        public int Count => m_Dictionary.Count;

        /// <inheritdoc/>
        public bool IsReadOnly => false;

        /// <inheritdoc/>
        public ICollection<TKey> Keys => m_Dictionary.Keys;

        /// <inheritdoc/>
        public ICollection<TValue> Values => m_Dictionary.Values;

        /// <inheritdoc/>
        IEnumerable<TKey> IReadOnlyDictionary<TKey, TValue>.Keys => m_Dictionary.Keys;

        /// <inheritdoc/>
        IEnumerable<TValue> IReadOnlyDictionary<TKey, TValue>.Values => m_Dictionary.Values;

        /// <inheritdoc/>
        public TValue this[TKey key]
        {
            get => m_Dictionary[key];
            set
            {
                if (m_Dictionary.TryGetValue(key, out var oldValue))
                {
                    ClearParent(oldValue);
                }
                m_Dictionary[key] = value;
                AssignParent(value);
                IncrementVersion();
            }
        }

        /// <inheritdoc/>
        public void Add(TKey key, TValue value)
        {
            m_Dictionary.Add(key, value);
            AssignParent(value);
            IncrementVersion();
        }

        /// <inheritdoc/>
        public void Add(KeyValuePair<TKey, TValue> item)
        {
            ((ICollection<KeyValuePair<TKey, TValue>>)m_Dictionary).Add(item);
            AssignParent(item.Value);
            IncrementVersion();
        }

        /// <inheritdoc/>
        public void Clear()
        {
            foreach (var value in m_Dictionary.Values)
            {
                ClearParent(value);
            }
            m_Dictionary.Clear();
            IncrementVersion();
        }

        /// <inheritdoc/>
        public bool Contains(KeyValuePair<TKey, TValue> item)
        {
            return ((ICollection<KeyValuePair<TKey, TValue>>)m_Dictionary).Contains(item);
        }

        /// <inheritdoc/>
        public bool ContainsKey(TKey key) => m_Dictionary.ContainsKey(key);

        /// <summary>
        /// Determines whether the dictionary contains a specific value.
        /// </summary>
        public bool ContainsValue(TValue value) => m_Dictionary.ContainsValue(value);

        /// <inheritdoc/>
        public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
        {
            ((ICollection<KeyValuePair<TKey, TValue>>)m_Dictionary).CopyTo(array, arrayIndex);
        }

        /// <inheritdoc/>
        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => m_Dictionary.GetEnumerator();

        /// <inheritdoc/>
        IEnumerator IEnumerable.GetEnumerator() => m_Dictionary.GetEnumerator();

        /// <inheritdoc/>
        public bool Remove(TKey key)
        {
            if (m_Dictionary.TryGetValue(key, out var value))
            {
                ClearParent(value);
            }
            var removed = m_Dictionary.Remove(key);
            if (removed)
            {
                IncrementVersion();
            }
            return removed;
        }

        /// <inheritdoc/>
        public bool Remove(KeyValuePair<TKey, TValue> item)
        {
            var removed = ((ICollection<KeyValuePair<TKey, TValue>>)m_Dictionary).Remove(item);
            if (removed)
            {
                ClearParent(item.Value);
                IncrementVersion();
            }
            return removed;
        }

        /// <inheritdoc/>
        public bool TryGetValue(TKey key, out TValue value)
        {
            return m_Dictionary.TryGetValue(key, out value!);
        }

        /// <summary>
        /// Attempts to add the specified key and value to the dictionary.
        /// </summary>
        /// <returns>true if the key/value pair was added successfully; false if the key already exists.</returns>
        public bool TryAdd(TKey key, TValue value)
        {
#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
            var added = m_Dictionary.TryAdd(key, value);
#else
            if (m_Dictionary.ContainsKey(key))
            {
                return false;
            }
            m_Dictionary.Add(key, value);
            var added = true;
#endif
            if (added)
            {
                AssignParent(value);
                IncrementVersion();
            }
            return added;
        }

        /// <summary>
        /// Gets the comparer used to determine equality of keys.
        /// </summary>
        public IEqualityComparer<TKey> Comparer => m_Dictionary.Comparer;
    }
}
