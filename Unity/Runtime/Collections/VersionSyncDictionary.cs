#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;

namespace ReactiveBinding
{
    /// <summary>
    /// A dictionary that tracks modifications via a version number AND participates in flat-registry data
    /// synchronization (<see cref="IVersionSync"/>). Use this — not the version-only
    /// <see cref="VersionDictionary{TKey, TValue}"/> — for a <c>[VersionField]</c> dictionary inside an
    /// <see cref="IVersionSync"/> class. Keys must be scalar; values may be scalar or sync objects. Structural
    /// changes are coalesced into an adaptive per-frame op log that falls back to a full record after excessive
    /// churn. Standalone type (not a subclass).
    /// </summary>
    /// <typeparam name="TKey">The type of keys in the dictionary.</typeparam>
    /// <typeparam name="TValue">The type of values in the dictionary.</typeparam>
    public class VersionSyncDictionary<TKey, TValue> : IDictionary<TKey, TValue>, IReadOnlyDictionary<TKey, TValue>, IVersionSync
        where TKey : notnull
    {
        private readonly Dictionary<TKey, TValue> m_Dict;

        /// <summary>Creates a new empty VersionSyncDictionary.</summary>
        public VersionSyncDictionary() { m_Dict = new Dictionary<TKey, TValue>(); }

        /// <summary>Creates a new VersionSyncDictionary with the specified initial capacity.</summary>
        public VersionSyncDictionary(int capacity) { m_Dict = new Dictionary<TKey, TValue>(capacity); }

        /// <summary>Creates a new VersionSyncDictionary with the default comparer.</summary>
        /// <exception cref="NotSupportedException">Thrown when a custom comparer is supplied.</exception>
        public VersionSyncDictionary(IEqualityComparer<TKey> comparer) { m_Dict = new Dictionary<TKey, TValue>(__RequireDefaultComparer(comparer)); }

        /// <summary>Creates a new VersionSyncDictionary with the specified capacity and comparer.</summary>
        public VersionSyncDictionary(int capacity, IEqualityComparer<TKey> comparer) { m_Dict = new Dictionary<TKey, TValue>(capacity, __RequireDefaultComparer(comparer)); }

        /// <summary>Creates a new VersionSyncDictionary containing elements from the specified dictionary.</summary>
        public VersionSyncDictionary(IDictionary<TKey, TValue> dictionary)
        {
            m_Dict = new Dictionary<TKey, TValue>(dictionary);
            VersionOwnership.EnsureCanAttachAll(this, m_Dict.Values);
            foreach (var value in m_Dict.Values) if (value is IVersion v) v.__Parent = this;
        }

        /// <summary>Creates a new VersionSyncDictionary containing elements from the specified dictionary with the specified comparer.</summary>
        public VersionSyncDictionary(IDictionary<TKey, TValue> dictionary, IEqualityComparer<TKey> comparer)
        {
            m_Dict = new Dictionary<TKey, TValue>(dictionary, __RequireDefaultComparer(comparer));
            VersionOwnership.EnsureCanAttachAll(this, m_Dict.Values);
            foreach (var value in m_Dict.Values) if (value is IVersion v) v.__Parent = this;
        }

        private static IEqualityComparer<TKey> __RequireDefaultComparer(IEqualityComparer<TKey> comparer)
        {
            if (comparer == null || ReferenceEquals(comparer, EqualityComparer<TKey>.Default)) return comparer;
            throw new NotSupportedException(
                "VersionSyncDictionary does not support custom key comparers because both sync peers must use " +
                "identical key equality. Use the default comparer or a non-sync VersionDictionary.");
        }

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

        private void __AttachValue(TValue value)
        {
            if (value is IVersion v) v.__Parent = this;
            if (__SyncContext != null && __objectVals && value is IVersionSync s) __Recurse(SyncOp.Attach, s);
        }

        private void __DetachValue(TValue value)
        {
            if (value is IVersion v) v.__Parent = null;
            if (__SyncContext != null && __objectVals && value is IVersionSync s) __Recurse(SyncOp.Unregister, s);
        }

        private static void __ClearValueParent(TValue value)
        {
            if (value is IVersion v) v.__Parent = null;
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
                var hadOldValue = m_Dict.TryGetValue(key, out var oldValue);
                if (hadOldValue && VersionOwnership.AreSame(oldValue, value)) return;
                if (value is IVersion child) VersionOwnership.EnsureCanAttach(this, child);
                m_Dict[key] = value;
                if (hadOldValue) __DetachValue(oldValue);
                __AttachValue(value);
                __IncrementVersion(); __LogOp(__OP_SET, key, value);
            }
        }

        /// <inheritdoc/>
        public void Add(TKey key, TValue value)
        {
            if (value is IVersion child) VersionOwnership.EnsureCanAttach(this, child);
            m_Dict.Add(key, value);
            __AttachValue(value);
            __IncrementVersion(); __LogOp(__OP_SET, key, value);
        }

        /// <inheritdoc/>
        public void Add(KeyValuePair<TKey, TValue> item)
        {
            if (item.Value is IVersion child) VersionOwnership.EnsureCanAttach(this, child);
            ((ICollection<KeyValuePair<TKey, TValue>>)m_Dict).Add(item);
            __AttachValue(item.Value);
            __IncrementVersion(); __LogOp(__OP_SET, item.Key, item.Value);
        }

        /// <inheritdoc/>
        public void Clear()
        {
            if (m_Dict.Count == 0) return;
            foreach (var value in m_Dict.Values) __DetachValue(value);
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
        public Dictionary<TKey, TValue>.Enumerator GetEnumerator() => m_Dict.GetEnumerator();

        /// <inheritdoc/>
        IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator() => m_Dict.GetEnumerator();

        /// <inheritdoc/>
        IEnumerator IEnumerable.GetEnumerator() => m_Dict.GetEnumerator();

        /// <inheritdoc/>
        public bool Remove(TKey key)
        {
            if (m_Dict.TryGetValue(key, out var value))
            {
                m_Dict.Remove(key);
                __DetachValue(value);
                __IncrementVersion(); __LogOp(__OP_REMOVE, key, default);
                return true;
            }
            return false;
        }

        /// <inheritdoc/>
        public bool Remove(KeyValuePair<TKey, TValue> item)
        {
            if (m_Dict.TryGetValue(item.Key, out var value)
                && EqualityComparer<TValue>.Default.Equals(value, item.Value)
                && ((ICollection<KeyValuePair<TKey, TValue>>)m_Dict).Remove(item))
            {
                __DetachValue(value);
                __IncrementVersion(); __LogOp(__OP_REMOVE, item.Key, default);
                return true;
            }
            return false;
        }

        /// <inheritdoc/>
        public bool TryGetValue(TKey key, out TValue value)
        {
            return m_Dict.TryGetValue(key, out value);
        }

        /// <summary>Attempts to add the specified key and value to the dictionary.</summary>
        /// <returns>true if the key/value pair was added successfully; false if the key already exists.</returns>
        public bool TryAdd(TKey key, TValue value)
        {
            if (m_Dict.ContainsKey(key)) return false;
            if (value is IVersion child) VersionOwnership.EnsureCanAttach(this, child);
            m_Dict.Add(key, value);
            __AttachValue(value);
            __IncrementVersion(); __LogOp(__OP_SET, key, value);
            return true;
        }

        /// <summary>Gets the comparer used to determine equality of keys.</summary>
        public IEqualityComparer<TKey> Comparer => m_Dict.Comparer;

        // ===== Synchronization (flat-registry) =====
        /// <inheritdoc/>
        public int __SyncId { get; set; }
        /// <inheritdoc/>
        public SyncContext __SyncContext { get; set; }

        private bool __objectVals;
        private System.Action<System.IO.BinaryWriter, TKey> __wKey;
        private System.Func<System.IO.BinaryReader, TKey> __rKey;
        private System.Action<System.IO.BinaryWriter, TValue> __wVal;
        private System.Func<System.IO.BinaryReader, TValue> __rVal;
        private System.Func<IVersionSync> __newVal;
        private bool __syncInitialized;

        /// <summary>Configures scalar key/value serialization.</summary>
        public void InitSync(
            System.Action<System.IO.BinaryWriter, TKey> wKey, System.Func<System.IO.BinaryReader, TKey> rKey,
            System.Action<System.IO.BinaryWriter, TValue> wVal, System.Func<System.IO.BinaryReader, TValue> rVal)
            => __InitSync(wKey, rKey, wVal, rVal);

        /// <summary>Owner injects key/value write/read delegates when the dictionary has scalar values.</summary>
        public void __InitSync(
            System.Action<System.IO.BinaryWriter, TKey> wKey, System.Func<System.IO.BinaryReader, TKey> rKey,
            System.Action<System.IO.BinaryWriter, TValue> wVal, System.Func<System.IO.BinaryReader, TValue> rVal)
        {
            __wKey = wKey;
            __rKey = rKey;
            __wVal = wVal;
            __rVal = rVal;
            __objectVals = false;
            __syncInitialized = true;
        }

        /// <summary>Configures scalar keys and sync-object values through a value factory.</summary>
        public void InitSync(
            System.Action<System.IO.BinaryWriter, TKey> wKey, System.Func<System.IO.BinaryReader, TKey> rKey,
            System.Func<IVersionSync> newVal)
            => __InitSync(wKey, rKey, newVal);

        /// <summary>Owner injects key delegates and a value factory when dictionary values are sync objects.</summary>
        public void __InitSync(
            System.Action<System.IO.BinaryWriter, TKey> wKey, System.Func<System.IO.BinaryReader, TKey> rKey,
            System.Func<IVersionSync> newVal)
        {
            __wKey = wKey;
            __rKey = rKey;
            __newVal = newVal;
            __objectVals = true;
            __syncInitialized = true;
        }

        private void __EnsureSyncInitialized()
        {
            if (!__syncInitialized)
                throw new InvalidOperationException(
                    "VersionSyncDictionary synchronization is not initialized. Assign it through a generated " +
                    "[VersionField] property or call InitSync before AttachTo/Capture/Apply.");
        }

        private void __WriteVal(System.IO.BinaryWriter writer, TValue value)
        {
            if (__objectVals) SyncWire.WriteVarInt32(writer, value == null ? 0 : ((IVersionSync)(object)value).__SyncId);
            else __wVal(writer, value);
        }

        private TValue __ReadVal(System.IO.BinaryReader reader)
        {
            if (!__objectVals) return __rVal(reader);
            int __id = SyncWire.ReadVarInt32(reader);
            if (__id < 0 || __id == int.MaxValue)
                throw new System.IO.InvalidDataException("Sync object ids must be between 0 and Int32.MaxValue - 1.");
            if (__id == 0) return default;
            if (__SyncContext.__Objects.TryGetValue(__id, out var __n)) return (TValue)__n;
            var __v = __newVal(); __v.__SyncId = __id; __v.__SyncContext = __SyncContext; __SyncContext.__Objects[__id] = __v;
            return (TValue)__v;
        }

        // Subtree recursion driver: Attach assigns ids / registers (and marks the new node all-dirty so it
        // flushes in full); Unregister fully resets the leaving node.
        private void __Recurse(SyncOp op, IVersionSync child)
        {
            switch (op)
            {
                case SyncOp.Attach:
                    if (child.__SyncId == 0)
                    {
                        child.__SyncId = __SyncContext.__AllocateId();
                        child.__SyncContext = __SyncContext;
                        __SyncContext.__Objects[child.__SyncId] = child;
                        child.__MarkAllDirty();
                        child.__SyncChildren(SyncOp.Attach);
                    }
                    else if (!ReferenceEquals(child.__SyncContext, __SyncContext)
                        || !__SyncContext.__Objects.TryGetValue(child.__SyncId, out var registered)
                        || !ReferenceEquals(registered, child))
                    {
                        throw new InvalidOperationException("The IVersionSync node is already attached to another SyncContext.");
                    }
                    break;
                case SyncOp.Unregister:
                    if (child.__SyncId != 0) __SyncContext.__RecordTombstone(child.__SyncId);
                    child.__Reset();
                    break;
            }
        }

        /// <inheritdoc/>
        public void AttachTo(SyncContext ctx)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            __EnsureSyncInitialized();
            if (__Parent != null)
                throw new InvalidOperationException("A child node cannot be attached as a SyncContext root.");
            if (__SyncContext != null)
            {
                if (ReferenceEquals(__SyncContext, ctx)
                    && __SyncId != 0
                    && ctx.__Objects.TryGetValue(__SyncId, out var registered)
                    && ReferenceEquals(registered, this)) return;
                throw new InvalidOperationException("This IVersionSync node is already attached to another SyncContext.");
            }
            __SyncContext = ctx;
            __Recurse(SyncOp.Attach, this);
        }

        /// <inheritdoc/>
        public void __SyncChildren(SyncOp op)
        {
            if (!__objectVals) return;
            foreach (var kvp in m_Dict)
                if (kvp.Value is IVersionSync s) __Recurse(op, s);
        }

        // ----- Incremental recording: per-frame op log, coalesced into one record by CaptureDelta. -----
        private const byte __OP_SET = 1, __OP_REMOVE = 2, __OP_CLEAR = 3;
        private bool __inDirty;
        private bool __fullDirty;
        private struct __PendingOp
        {
            public byte Op;
            public TKey Key;
            public TValue Value;
            public int ValueId;
            public int EstimatedBytes;
        }
        private System.Collections.Generic.List<__PendingOp> __ops;
        private System.Collections.Generic.Dictionary<TKey, int> __opIndex;
        private long __pendingBytes;

        private void __EnsureDirty()
        {
            if (__SyncContext != null && !__inDirty)
            {
                __inDirty = true;
                __SyncContext.__EnlistDirty(__SyncId);
            }
        }

        /// <inheritdoc/>
        public bool __IsDirty => __inDirty;

        /// <inheritdoc/>
        public void __MarkAllDirty()
        {
            __EnsureDirty();
            __fullDirty = true;
            if (__ops != null) __ops.Clear();
            if (__opIndex != null) __opIndex.Clear();
            __pendingBytes = 0;
        }

        /// <inheritdoc/>
        public void __ClearDirty()
        {
            __inDirty = false; __fullDirty = false; __pendingBytes = 0;
            if (__ops != null) __ops.Clear();
            if (__opIndex != null) __opIndex.Clear();
        }

        /// <inheritdoc/>
        public void __Reset()
        {
            foreach (var value in m_Dict.Values)
                if (value is IVersion v)
                {
                    v.__Reset();
                    v.__Parent = this;
                }
            if (__SyncContext != null) __SyncContext.__Objects.Remove(__SyncId);
            __SyncId = 0; __SyncContext = null;
            __inDirty = false; __fullDirty = false; __pendingBytes = 0;
            if (__ops != null) __ops.Clear();
            if (__opIndex != null) __opIndex.Clear();
            __Version = 0; __Parent = null;   // keeps contents; detaches for reuse
        }

        /// <inheritdoc/>
        public void Reset() => __Reset();

        private void __LogOp(byte op, TKey key, TValue val)
        {
            __EnsureDirty();
            if (!__inDirty) return;   // not attached
            if (__fullDirty) return;
            if (__ops == null) __ops = new System.Collections.Generic.List<__PendingOp>();
            if (__opIndex == null) __opIndex = new System.Collections.Generic.Dictionary<TKey, int>(m_Dict.Comparer);
            int valueId = __objectVals && op == __OP_SET && val != null ? ((IVersionSync)(object)val).__SyncId : 0;
            int estimate = 1 + (op == __OP_CLEAR ? 0 : __EstimateScalarBytes(key))
                + (op == __OP_SET ? (__objectVals ? SyncWire.GetVarInt32Size(valueId) : __EstimateScalarBytes(val)) : 0);
            if (op == __OP_CLEAR)
            {
                __ops.Clear();
                __opIndex.Clear();
                __pendingBytes = 0;
            }
            else if (__opIndex.TryGetValue(key, out int priorIndex))
            {
                var prior = __ops[priorIndex];
                __pendingBytes -= prior.EstimatedBytes;
                __ops[priorIndex] = new __PendingOp { Op = op, Key = key, Value = __objectVals ? default : val, ValueId = valueId, EstimatedBytes = estimate };
                __pendingBytes += estimate;
                __MaybeMarkFull();
                return;
            }
            int index = __ops.Count;
            __ops.Add(new __PendingOp { Op = op, Key = key, Value = __objectVals ? default : val, ValueId = valueId, EstimatedBytes = estimate });
            if (op != __OP_CLEAR) __opIndex[key] = index;
            __pendingBytes += estimate;
            __MaybeMarkFull();
        }

        private void __MaybeMarkFull()
        {
            if (__ops == null || __ops.Count == 0) return;
            long deltaBytes = SyncWire.GetVarInt32Size(__SyncId) + 1L + SyncWire.GetVarInt32Size(__ops.Count) + __pendingBytes;
            long minimumFullBytes = SyncWire.GetVarInt32Size(__SyncId) + 1L + SyncWire.GetVarInt32Size(m_Dict.Count) + m_Dict.Count * 2L;
            if (deltaBytes < minimumFullBytes) return;
            long fullBytes = SyncWire.GetVarInt32Size(__SyncId) + 1L + SyncWire.GetVarInt32Size(m_Dict.Count);
            foreach (var pair in m_Dict)
                fullBytes += __EstimateScalarBytes(pair.Key) + (__objectVals
                    ? SyncWire.GetVarInt32Size(pair.Value == null ? 0 : ((IVersionSync)(object)pair.Value).__SyncId)
                    : __EstimateScalarBytes(pair.Value));
            if (deltaBytes >= fullBytes) __MarkAllDirty();
        }

        private static int __EstimateScalarBytes<TItem>(TItem value)
        {
            object boxed = value;
            if (boxed is string text)
            {
                int bytes = System.Text.Encoding.UTF8.GetByteCount(text);
                return 1 + SyncWire.GetVarInt32Size(bytes) + bytes;
            }
            Type type = typeof(TItem);
            if (type.IsEnum) type = Enum.GetUnderlyingType(type);
            switch (Type.GetTypeCode(type))
            {
                case TypeCode.Boolean:
                case TypeCode.Byte:
                case TypeCode.SByte: return 1;
                case TypeCode.Char:
                case TypeCode.Int16:
                case TypeCode.UInt16: return 2;
                case TypeCode.Int32:
                case TypeCode.UInt32:
                case TypeCode.Single: return 4;
                case TypeCode.Int64:
                case TypeCode.UInt64:
                case TypeCode.Double: return 8;
                case TypeCode.Decimal: return 16;
                case TypeCode.String: return 1;
                default: return 16;
            }
        }

        /// <summary>Full record (keyframe): [id][1][count][key,value...].</summary>
        public void __CaptureFull(System.IO.BinaryWriter writer)
        {
            __EnsureSyncInitialized();
            SyncWire.WriteVarInt32(writer, __SyncId);
            writer.Write((byte)1);
            SyncWire.WriteVarInt32(writer, m_Dict.Count);
            foreach (var kvp in m_Dict) { __wKey(writer, kvp.Key); __WriteVal(writer, kvp.Value); }
        }

        /// <summary>Incremental record: [id][0][opCount][ops] — or a full [id][1][count][...] when fully dirty.</summary>
        public void __CaptureDelta(System.IO.BinaryWriter writer)
        {
            __EnsureSyncInitialized();
            SyncWire.WriteVarInt32(writer, __SyncId);
            if (__fullDirty || __ops == null || __ops.Count == 0)
            {
                writer.Write((byte)1);
                SyncWire.WriteVarInt32(writer, m_Dict.Count);
                foreach (var kvp in m_Dict) { __wKey(writer, kvp.Key); __WriteVal(writer, kvp.Value); }
                return;
            }
            writer.Write((byte)0);
            SyncWire.WriteVarInt32(writer, __ops.Count);
            for (int i = 0; i < __ops.Count; i++)
            {
                var e = __ops[i];
                writer.Write(e.Op);
                switch (e.Op)
                {
                    case __OP_SET:
                        __wKey(writer, e.Key);
                        if (__objectVals) SyncWire.WriteVarInt32(writer, e.ValueId); else __wVal(writer, e.Value);
                        break;
                    case __OP_REMOVE: __wKey(writer, e.Key); break;
                    case __OP_CLEAR: break;
                }
            }
        }

        /// <summary>Applies a node record and advances the local version without marking outbound sync state dirty.</summary>
        public void __Apply(System.IO.BinaryReader reader)
        {
            __EnsureSyncInitialized();
            if (reader.ReadByte() == 1)
            {
                int n = SyncWire.ReadVarInt32(reader);
                if (n < 0) throw new System.IO.InvalidDataException("The dictionary entry count cannot be negative.");
                foreach (var kvp in m_Dict) __ClearValueParent(kvp.Value);
                m_Dict.Clear();
                for (int i = 0; i < n; i++) { var k = __rKey(reader); var v = __ReadVal(reader); m_Dict[k] = v; if (v is IVersion iv) iv.__Parent = this; }
                __SyncContext.__TouchVersion(this);
                return;
            }
            int ops = SyncWire.ReadVarInt32(reader);
            if (ops < 0) throw new System.IO.InvalidDataException("The dictionary operation count cannot be negative.");
            for (int k = 0; k < ops; k++)
            {
                byte op = reader.ReadByte();
                switch (op)
                {
                    case __OP_SET:
                    {
                        var key = __rKey(reader);
                        var val = __ReadVal(reader);
                        if (m_Dict.TryGetValue(key, out var old)) __ClearValueParent(old);
                        m_Dict[key] = val;
                        if (val is IVersion iv) iv.__Parent = this;
                        break;
                    }
                    case __OP_REMOVE:
                    {
                        var key = __rKey(reader);
                        if (m_Dict.TryGetValue(key, out var old)) __ClearValueParent(old);
                        m_Dict.Remove(key);
                        break;
                    }
                    case __OP_CLEAR:
                        foreach (var kvp in m_Dict) __ClearValueParent(kvp.Value);
                        m_Dict.Clear();
                        break;
                }
            }
            __SyncContext.__TouchVersion(this);
        }
    }
}
