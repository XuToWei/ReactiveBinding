#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;

namespace ReactiveBinding
{
    /// <summary>
    /// A dictionary implementation that tracks modifications via a version number.
    /// The __Version property increments on each Add, Remove, Set, or Clear operation.
    /// If values implement IVersion, they will share this container as __Parent,
    /// allowing value property changes to automatically increment the container's version.
    /// </summary>
    /// <typeparam name="TKey">The type of keys in the dictionary.</typeparam>
    /// <typeparam name="TValue">The type of values in the dictionary.</typeparam>
    public class VersionDictionary<TKey, TValue> : IDictionary<TKey, TValue>, IReadOnlyDictionary<TKey, TValue>, IVersion, IVersionSync
        where TKey : notnull
    {
        private readonly Dictionary<TKey, TValue> m_Dict;
        private int m_Version;

        /// <summary>
        /// Creates a new empty VersionDictionary.
        /// </summary>
        public VersionDictionary()
        {
            m_Dict = new Dictionary<TKey, TValue>();
        }

        /// <summary>
        /// Creates a new VersionDictionary with the specified initial capacity.
        /// </summary>
        public VersionDictionary(int capacity)
        {
            m_Dict = new Dictionary<TKey, TValue>(capacity);
        }

        /// <summary>
        /// Creates a new VersionDictionary with the specified comparer.
        /// </summary>
        public VersionDictionary(IEqualityComparer<TKey> comparer)
        {
            m_Dict = new Dictionary<TKey, TValue>(comparer);
        }

        /// <summary>
        /// Creates a new VersionDictionary with the specified capacity and comparer.
        /// </summary>
        public VersionDictionary(int capacity, IEqualityComparer<TKey> comparer)
        {
            m_Dict = new Dictionary<TKey, TValue>(capacity, comparer);
        }

        /// <summary>
        /// Creates a new VersionDictionary containing elements from the specified dictionary.
        /// </summary>
        public VersionDictionary(IDictionary<TKey, TValue> dictionary)
        {
            m_Dict = new Dictionary<TKey, TValue>(dictionary);
            foreach (var value in m_Dict.Values)
            {
                AssignParent(value);
            }
        }

        /// <summary>
        /// Creates a new VersionDictionary containing elements from the specified dictionary with the specified comparer.
        /// </summary>
        public VersionDictionary(IDictionary<TKey, TValue> dictionary, IEqualityComparer<TKey> comparer)
        {
            m_Dict = new Dictionary<TKey, TValue>(dictionary, comparer);
            foreach (var value in m_Dict.Values)
            {
                AssignParent(value);
            }
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

        private void AssignParent(TValue item)
        {
            if (item is IVersion v) v.__Parent = this;
        }

        private void ClearParent(TValue item)
        {
            if (item is IVersion v) v.__Parent = null;
        }

        protected virtual void OnItemAdded(TKey key, TValue value) { }

        protected virtual void OnItemRemoved(TKey key, TValue value) { }

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
                    ClearParent(oldValue);
                    OnItemRemoved(key, oldValue);
                }
                m_Dict[key] = value;
                AssignParent(value);
                OnItemAdded(key, value);
                __IncrementVersion();
                RecordSet(key, value);
            }
        }

        /// <inheritdoc/>
        public void Add(TKey key, TValue value)
        {
            m_Dict.Add(key, value);
            AssignParent(value);
            OnItemAdded(key, value);
            __IncrementVersion();
            RecordSet(key, value);
        }

        /// <inheritdoc/>
        public void Add(KeyValuePair<TKey, TValue> item)
        {
            ((ICollection<KeyValuePair<TKey, TValue>>)m_Dict).Add(item);
            AssignParent(item.Value);
            OnItemAdded(item.Key, item.Value);
            __IncrementVersion();
            RecordSet(item.Key, item.Value);
        }

        /// <inheritdoc/>
        public void Clear()
        {
            foreach (var kvp in m_Dict)
            {
                ClearParent(kvp.Value);
                OnItemRemoved(kvp.Key, kvp.Value);
            }
            m_Dict.Clear();
            __IncrementVersion();
            RecordClear();
        }

        /// <inheritdoc/>
        public bool Contains(KeyValuePair<TKey, TValue> item)
        {
            return ((ICollection<KeyValuePair<TKey, TValue>>)m_Dict).Contains(item);
        }

        /// <inheritdoc/>
        public bool ContainsKey(TKey key) => m_Dict.ContainsKey(key);

        /// <summary>
        /// Determines whether the dictionary contains a specific value.
        /// </summary>
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
                ClearParent(value);
                m_Dict.Remove(key);
                OnItemRemoved(key, value);
                __IncrementVersion();
                RecordRemove(key);
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
                ClearParent(item.Value);
                OnItemRemoved(item.Key, item.Value);
                __IncrementVersion();
                RecordRemove(item.Key);
            }
            return removed;
        }

        /// <inheritdoc/>
        public bool TryGetValue(TKey key, out TValue value)
        {
            return m_Dict.TryGetValue(key, out value!);
        }

        /// <summary>
        /// Attempts to add the specified key and value to the dictionary.
        /// </summary>
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
                AssignParent(value);
                OnItemAdded(key, value);
                __IncrementVersion();
                RecordSet(key, value);
            }
            return added;
        }

        /// <summary>
        /// Gets the comparer used to determine equality of keys.
        /// </summary>
        public IEqualityComparer<TKey> Comparer => m_Dict.Comparer;

        // ===== Synchronization (flat-registry, direct-write model) =====
        /// <inheritdoc/>
        public int __SyncId { get; set; }
        /// <inheritdoc/>
        public SyncContext __SyncContext { get; set; }

        private System.Action<System.IO.BinaryWriter, TKey> __wKey;
        private System.Func<System.IO.BinaryReader, TKey> __rKey;
        private System.Action<System.IO.BinaryWriter, TValue> __wVal;
        private System.Func<System.IO.BinaryReader, TValue> __rVal;

        /// <summary>Owner injects key/value write/read delegates when the dictionary is attached.</summary>
        public void __InitSync(
            System.Action<System.IO.BinaryWriter, TKey> wKey, System.Func<System.IO.BinaryReader, TKey> rKey,
            System.Action<System.IO.BinaryWriter, TValue> wVal, System.Func<System.IO.BinaryReader, TValue> rVal)
        {
            __wKey = wKey;
            __rKey = rKey;
            __wVal = wVal;
            __rVal = rVal;
        }

        /// <inheritdoc/>
        public void AttachTo(SyncContext ctx)   // scalar key/value: no children to recurse
        {
            __SyncContext = ctx;
            if (__SyncId == 0) { __SyncId = ctx.__NextId++; ctx.__Objects[__SyncId] = this; }
        }

        // Each op writes its record straight into the recorder.
        // Op record: [0][id][mode=0][opcode][data]. Opcodes: 1 set, 2 remove, 3 clear.
        private void RecordSet(TKey key, TValue value)
        {
            if (__SyncContext == null) return;
            var w = __SyncContext.__Writer;
            w.Write((byte)0); w.Write(__SyncId); w.Write((byte)0); w.Write((byte)1); __wKey(w, key); __wVal(w, value);
        }
        private void RecordRemove(TKey key)
        {
            if (__SyncContext == null) return;
            var w = __SyncContext.__Writer;
            w.Write((byte)0); w.Write(__SyncId); w.Write((byte)0); w.Write((byte)2); __wKey(w, key);
        }
        private void RecordClear()
        {
            if (__SyncContext == null) return;
            var w = __SyncContext.__Writer;
            w.Write((byte)0); w.Write(__SyncId); w.Write((byte)0); w.Write((byte)3);
        }

        /// <inheritdoc/>
        public void __SyncChildren(SyncOp op) { }   // scalar key/value only

        /// <summary>Full snapshot of this node: one record [0][id][mode=1][count][key,value...].</summary>
        public void __Commit()
        {
            var writer = __SyncContext.__Writer;
            writer.Write((byte)0); writer.Write(__SyncId);
            writer.Write((byte)1);
            writer.Write(m_Dict.Count);
            foreach (var kvp in m_Dict) { __wKey(writer, kvp.Key); __wVal(writer, kvp.Value); }
        }

        /// <summary>Applies a single node record (full or one op), silently.</summary>
        public void __Apply(System.IO.BinaryReader reader)
        {
            byte mode = reader.ReadByte();
            if (mode == 1)
            {
                m_Dict.Clear();
                int n = reader.ReadInt32();
                for (int i = 0; i < n; i++) { var k = __rKey(reader); var v = __rVal(reader); m_Dict[k] = v; }
                return;
            }
            byte code = reader.ReadByte();
            if (code == 1)
            {
                var key = __rKey(reader); var val = __rVal(reader);
                m_Dict[key] = val;
            }
            else if (code == 2)
            {
                var key = __rKey(reader);
                m_Dict.Remove(key);
            }
            else if (code == 3)
            {
                m_Dict.Clear();
            }
        }
    }
}
