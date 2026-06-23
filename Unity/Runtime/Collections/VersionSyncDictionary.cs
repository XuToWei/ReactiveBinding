using System.Collections;
using System.Collections.Generic;

namespace ReactiveBinding
{
    /// <summary>
    /// A dictionary that tracks modifications via a version number AND participates in flat-registry data
    /// synchronization (<see cref="IVersionSync"/>). Use this — not the version-only
    /// <see cref="VersionDictionary{TKey, TValue}"/> — for a <c>[VersionField]</c> dictionary inside an
    /// <see cref="IVersionSync"/> class. Keys and values must be scalar (object values are unsupported, VS2001);
    /// structural changes are coalesced into a per-frame op log. Standalone type (not a subclass).
    /// </summary>
    /// <typeparam name="TKey">The type of keys in the dictionary.</typeparam>
    /// <typeparam name="TValue">The type of values in the dictionary.</typeparam>
    public class VersionSyncDictionary<TKey, TValue> : IDictionary<TKey, TValue>, IReadOnlyDictionary<TKey, TValue>, IVersionSync
        where TKey : notnull
    {
        private readonly Dictionary<TKey, TValue> m_Dict;
        private int m_Version;

        /// <summary>Creates a new empty VersionSyncDictionary.</summary>
        public VersionSyncDictionary() { m_Dict = new Dictionary<TKey, TValue>(); }

        /// <summary>Creates a new VersionSyncDictionary with the specified initial capacity.</summary>
        public VersionSyncDictionary(int capacity) { m_Dict = new Dictionary<TKey, TValue>(capacity); }

        /// <summary>Creates a new VersionSyncDictionary with the specified comparer.</summary>
        public VersionSyncDictionary(IEqualityComparer<TKey> comparer) { m_Dict = new Dictionary<TKey, TValue>(comparer); }

        /// <summary>Creates a new VersionSyncDictionary with the specified capacity and comparer.</summary>
        public VersionSyncDictionary(int capacity, IEqualityComparer<TKey> comparer) { m_Dict = new Dictionary<TKey, TValue>(capacity, comparer); }

        /// <summary>Creates a new VersionSyncDictionary containing elements from the specified dictionary.</summary>
        public VersionSyncDictionary(IDictionary<TKey, TValue> dictionary)
        {
            m_Dict = new Dictionary<TKey, TValue>(dictionary);
            foreach (var value in m_Dict.Values) if (value is IVersion v) v.__Parent = this;
        }

        /// <summary>Creates a new VersionSyncDictionary containing elements from the specified dictionary with the specified comparer.</summary>
        public VersionSyncDictionary(IDictionary<TKey, TValue> dictionary, IEqualityComparer<TKey> comparer)
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
                __IncrementVersion(); __LogOp(__OP_SET, key, value);
            }
        }

        /// <inheritdoc/>
        public void Add(TKey key, TValue value)
        {
            m_Dict.Add(key, value);
            if (value is IVersion v) v.__Parent = this;
            __IncrementVersion(); __LogOp(__OP_SET, key, value);
        }

        /// <inheritdoc/>
        public void Add(KeyValuePair<TKey, TValue> item)
        {
            ((ICollection<KeyValuePair<TKey, TValue>>)m_Dict).Add(item);
            if (item.Value is IVersion v) v.__Parent = this;
            __IncrementVersion(); __LogOp(__OP_SET, item.Key, item.Value);
        }

        /// <inheritdoc/>
        public void Clear()
        {
            foreach (var kvp in m_Dict) if (kvp.Value is IVersion v) v.__Parent = null;
            m_Dict.Clear();
            __IncrementVersion(); __LogOp(__OP_CLEAR, default, default);
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
                __IncrementVersion(); __LogOp(__OP_REMOVE, key, default);
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
                __IncrementVersion(); __LogOp(__OP_REMOVE, item.Key, default);
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
                __IncrementVersion(); __LogOp(__OP_SET, key, value);
            }
            return added;
        }

        /// <summary>Gets the comparer used to determine equality of keys.</summary>
        public IEqualityComparer<TKey> Comparer => m_Dict.Comparer;

        // ===== Synchronization (flat-registry) =====
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

        /// <inheritdoc/>
        public void __SyncChildren(SyncOp op) { }   // scalar key/value only

        // ----- Incremental recording: per-frame op log, coalesced into one record by CaptureDelta. -----
        private const byte __OP_SET = 1, __OP_REMOVE = 2, __OP_CLEAR = 3;
        private bool __inDirty;
        private bool __fullDirty;
        private System.Collections.Generic.List<(byte op, TKey key, TValue val)> __ops;

        private void __EnsureDirty()
        {
            if (__SyncContext != null) __inDirty = true;
        }

        /// <inheritdoc/>
        public bool __IsDirty => __inDirty;

        /// <inheritdoc/>
        public void __MarkAllDirty() { __EnsureDirty(); __fullDirty = true; }

        /// <inheritdoc/>
        public void __ClearDirty() { __inDirty = false; __fullDirty = false; if (__ops != null) __ops.Clear(); }

        /// <inheritdoc/>
        public void __Reset()
        {
            // Scalar keys/values (object values are unsupported, VS2001) — nothing to recurse.
            if (__SyncContext != null) __SyncContext.__Objects.Remove(__SyncId);
            __SyncId = 0; __SyncContext = null;
            __inDirty = false; __fullDirty = false; if (__ops != null) __ops.Clear();
            m_Version = 0; __Parent = null;   // keeps contents; detaches for reuse
        }

        private void __LogOp(byte op, TKey key, TValue val)
        {
            __EnsureDirty();
            if (!__inDirty) return;   // not attached
            if (__ops == null) __ops = new System.Collections.Generic.List<(byte, TKey, TValue)>();
            __ops.Add((op, key, val));
        }

        /// <summary>Full record (keyframe): [id][1][count][key,value...].</summary>
        public void __CaptureFull(System.IO.BinaryWriter writer)
        {
            writer.Write(__SyncId);
            writer.Write((byte)1);
            writer.Write(m_Dict.Count);
            foreach (var kvp in m_Dict) { __wKey(writer, kvp.Key); __wVal(writer, kvp.Value); }
        }

        /// <summary>Incremental record: [id][0][opCount][ops] — or a full [id][1][count][...] when fully dirty.</summary>
        public void __CaptureDelta(System.IO.BinaryWriter writer)
        {
            writer.Write(__SyncId);
            if (__fullDirty || __ops == null || __ops.Count == 0)
            {
                writer.Write((byte)1);
                writer.Write(m_Dict.Count);
                foreach (var kvp in m_Dict) { __wKey(writer, kvp.Key); __wVal(writer, kvp.Value); }
                return;
            }
            writer.Write((byte)0);
            writer.Write(__ops.Count);
            for (int i = 0; i < __ops.Count; i++)
            {
                var e = __ops[i];
                writer.Write(e.op);
                switch (e.op)
                {
                    case __OP_SET: __wKey(writer, e.key); __wVal(writer, e.val); break;
                    case __OP_REMOVE: __wKey(writer, e.key); break;
                    case __OP_CLEAR: break;
                }
            }
        }

        /// <summary>Applies a node record: a full [1][count][...] rebuild, or a [0][opCount][ops] replay. Silently.</summary>
        public void __Apply(System.IO.BinaryReader reader)
        {
            if (reader.ReadByte() == 1)
            {
                m_Dict.Clear();
                int n = reader.ReadInt32();
                for (int i = 0; i < n; i++) { var k = __rKey(reader); var v = __rVal(reader); m_Dict[k] = v; }
                return;
            }
            int ops = reader.ReadInt32();
            for (int k = 0; k < ops; k++)
            {
                byte op = reader.ReadByte();
                switch (op)
                {
                    case __OP_SET: { var key = __rKey(reader); var val = __rVal(reader); m_Dict[key] = val; break; }
                    case __OP_REMOVE: { var key = __rKey(reader); m_Dict.Remove(key); break; }
                    case __OP_CLEAR: m_Dict.Clear(); break;
                }
            }
        }
    }
}
