#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;

namespace ReactiveBinding
{
    /// <summary>
    /// A set that tracks modifications via a version number AND participates in flat-registry data synchronization
    /// (<see cref="IVersionSync"/>). Use this — not the version-only <see cref="VersionHashSet{T}"/> — for a
    /// <c>[VersionField]</c> set inside an <see cref="IVersionSync"/> class. Elements may be scalar or sync objects;
    /// equality follows <see cref="EqualityComparer{T}.Default"/> and custom comparers are not supported. Fields used
    /// by Equals/GetHashCode must remain stable while an element is in the set. Sync objects should normally retain
    /// reference equality because a consumer inserts a referenced node before applying that node's field record.
    /// Structural changes are coalesced into an adaptive per-frame op log. Standalone type (not a subclass).
    /// </summary>
    /// <typeparam name="T">The type of elements in the set.</typeparam>
    public class VersionSyncHashSet<T> : ISet<T>, IReadOnlyCollection<T>, IVersionSync
    {
        private readonly HashSet<T> m_Set;

        /// <summary>Creates a new empty VersionSyncHashSet.</summary>
        public VersionSyncHashSet() { m_Set = new HashSet<T>(EqualityComparer<T>.Default); }

        /// <summary>Creates a new VersionSyncHashSet containing elements from the specified collection.</summary>
        public VersionSyncHashSet(IEnumerable<T> collection)
        {
            m_Set = new HashSet<T>(collection, EqualityComparer<T>.Default);
            VersionOwnership.EnsureCanAttachAll(this, m_Set);
            foreach (var item in m_Set) if (item is IVersion v) v.__Parent = this;
        }

#if UNITY_2021_2_OR_NEWER || NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
        /// <summary>Creates a new VersionSyncHashSet with the specified initial capacity.</summary>
        public VersionSyncHashSet(int capacity) { m_Set = new HashSet<T>(capacity, EqualityComparer<T>.Default); }
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

        private void __AttachElement(T item)
        {
            if (item is IVersion v) v.__Parent = this;
            if (__SyncContext != null && __objectElems && item is IVersionSync s) __Recurse(SyncOp.Attach, s);
        }

        private void __DetachElement(T item)
        {
            if (item is IVersion v) v.__Parent = null;
            if (__SyncContext != null && __objectElems && item is IVersionSync s) __Recurse(SyncOp.Unregister, s);
        }

        private int __ElementId(T item)
            => __objectElems && item != null ? ((IVersionSync)(object)item).__SyncId : 0;

        private static void __ClearElementParent(T item)
        {
            if (item is IVersion v) v.__Parent = null;
        }

        private void __ApplyAdd(T item)
        {
            if (!m_Set.Add(item))
                throw new System.IO.InvalidDataException(
                    "VersionSyncHashSet received duplicate elements under EqualityComparer<T>.Default. " +
                    "Sync-object equality and hash codes must remain stable before field records are applied.");
            if (item is IVersion v) v.__Parent = this;
        }

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
                __AttachElement(item);
                __IncrementVersion(); __LogOp(__OP_ADD, item);
            }
            return added;
        }

        /// <inheritdoc/>
        void ICollection<T>.Add(T item) => Add(item);

        /// <inheritdoc/>
        public void Clear()
        {
            if (m_Set.Count == 0) return;
            foreach (var item in m_Set) __DetachElement(item);
            m_Set.Clear();
            __IncrementVersion();
            __LogOp(__OP_CLEAR, default);
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
                int elementId = __ElementId(stored);
                __DetachElement(stored);
                __LogOp(__OP_REMOVE, stored, elementId);
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
            else if (other is VersionSyncHashSet<T> versionSet && m_Set.Comparer.Equals(versionSet.m_Set.Comparer))
                otherSet = versionSet.m_Set;
            else otherSet = new HashSet<T>(other, m_Set.Comparer);
            int removed = m_Set.RemoveWhere(item =>
            {
                if (otherSet.Contains(item)) return false;
                int elementId = __ElementId(item);
                __DetachElement(item);
                __LogOp(__OP_REMOVE, item, elementId);
                return true;
            });
            if (removed != 0)
            {
                __IncrementVersion();
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
            if (!__TryRemoveStoredValue(item, out var storedItem)) return false;
            int elementId = __ElementId(storedItem);
            __DetachElement(storedItem);
            __IncrementVersion();
            __LogOp(__OP_REMOVE, storedItem, elementId);
            return true;
        }

        /// <summary>Removes all elements that match the conditions defined by the specified predicate.</summary>
        public int RemoveWhere(Predicate<T> match)
        {
            if (match == null) throw new ArgumentNullException(nameof(match));
            int removed = m_Set.RemoveWhere(item =>
            {
                if (!match(item)) return false;
                int elementId = __ElementId(item);
                __DetachElement(item);
                __LogOp(__OP_REMOVE, item, elementId);
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
            else if (other is VersionSyncHashSet<T> versionSet && m_Set.Comparer.Equals(versionSet.m_Set.Comparer))
                otherSet = versionSet.m_Set;
            else otherSet = new HashSet<T>(other, m_Set.Comparer);
            bool changed = false;
            foreach (var item in otherSet)
            {
                if (__TryRemoveStoredValue(item, out var stored))
                {
                    int elementId = __ElementId(stored);
                    __DetachElement(stored);
                    __LogOp(__OP_REMOVE, stored, elementId);
                }
                else
                {
                    if (item is IVersion child) VersionOwnership.EnsureCanAttach(this, child);
                    m_Set.Add(item);
                    __AttachElement(item);
                    __LogOp(__OP_ADD, item);
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
                __AttachElement(item);
                __LogOp(__OP_ADD, item);
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

        // ===== Synchronization (flat-registry) =====
        /// <inheritdoc/>
        public int __SyncId { get; set; }
        /// <inheritdoc/>
        public SyncContext __SyncContext { get; set; }

        private bool __objectElems;
        private System.Action<System.IO.BinaryWriter, T> __wElem;
        private System.Func<System.IO.BinaryReader, T> __rElem;
        private System.Func<IVersionSync> __newElem;
        private bool __syncInitialized;

        /// <summary>Configures scalar-element serialization.</summary>
        public void InitSync(System.Action<System.IO.BinaryWriter, T> wElem, System.Func<System.IO.BinaryReader, T> rElem)
            => __InitSync(wElem, rElem);

        /// <summary>Owner injects element write/read delegates when the set has scalar elements.</summary>
        public void __InitSync(System.Action<System.IO.BinaryWriter, T> wElem, System.Func<System.IO.BinaryReader, T> rElem)
        {
            __wElem = wElem;
            __rElem = rElem;
            __objectElems = false;
            __syncInitialized = true;
        }

        /// <summary>Configures sync-object elements through an element factory.</summary>
        public void InitSync(System.Func<IVersionSync> newElem)
            => __InitSync(newElem);

        /// <summary>Owner injects an element factory when set elements are sync objects.</summary>
        public void __InitSync(System.Func<IVersionSync> newElem)
        {
            __newElem = newElem;
            __objectElems = true;
            __syncInitialized = true;
        }

        private void __EnsureSyncInitialized()
        {
            if (!__syncInitialized)
                throw new InvalidOperationException(
                    "VersionSyncHashSet synchronization is not initialized. Assign it through a generated " +
                    "[VersionField] property or call InitSync before AttachTo/Capture/Apply.");
        }

        private void __WriteElem(System.IO.BinaryWriter writer, T item)
        {
            if (__objectElems) SyncWire.WriteVarInt32(writer, item == null ? 0 : ((IVersionSync)(object)item).__SyncId);
            else __wElem(writer, item);
        }

        private T __ReadElem(System.IO.BinaryReader reader)
        {
            if (!__objectElems) return __rElem(reader);
            int __id = SyncWire.ReadVarInt32(reader);
            if (__id < 0 || __id == int.MaxValue)
                throw new System.IO.InvalidDataException("Sync object ids must be between 0 and Int32.MaxValue - 1.");
            if (__id == 0) return default;
            if (__SyncContext.__Objects.TryGetValue(__id, out var __n)) return (T)__n;
            var __e = __newElem(); __e.__SyncId = __id; __e.__SyncContext = __SyncContext; __SyncContext.__Objects[__id] = __e;
            return (T)__e;
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
            if (!__objectElems) return;
            foreach (var item in m_Set)
                if (item is IVersionSync s) __Recurse(op, s);
        }

        // ----- Incremental recording: per-frame op log, coalesced into one record by CaptureDelta. -----
        private const byte __OP_ADD = 1, __OP_REMOVE = 2, __OP_CLEAR = 3;
        private bool __inDirty;
        private bool __fullDirty;
        private struct __PendingOp
        {
            public byte Op;
            public T Element;
            public int ElementId;
            public int EstimatedBytes;
        }
        private System.Collections.Generic.List<__PendingOp> __ops;
        private System.Collections.Generic.Dictionary<T, int> __opIndex;
        private int __nullOpIndex = -1;
        private int __activeOpCount;
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
        public void __MarkAllDirty() { __MarkFull(); }

        /// <inheritdoc/>
        public void __ClearDirty()
        {
            __inDirty = false; __fullDirty = false; __pendingBytes = 0; __activeOpCount = 0; __nullOpIndex = -1;
            if (__ops != null) __ops.Clear();
            if (__opIndex != null) __opIndex.Clear();
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
            if (__SyncContext != null) __SyncContext.__Objects.Remove(__SyncId);
            __SyncId = 0; __SyncContext = null;
            __inDirty = false; __fullDirty = false; __pendingBytes = 0; __activeOpCount = 0; __nullOpIndex = -1;
            if (__ops != null) __ops.Clear();
            if (__opIndex != null) __opIndex.Clear();
            __Version = 0; __Parent = null;   // keeps contents; detaches for reuse
        }

        /// <inheritdoc/>
        public void Reset() => __Reset();

        private void __MarkFull()
        {
            __EnsureDirty();
            __fullDirty = true;
            if (__ops != null) __ops.Clear();
            if (__opIndex != null) __opIndex.Clear();
            __activeOpCount = 0;
            __nullOpIndex = -1;
            __pendingBytes = 0;
        }

        private void __LogOp(byte op, T elem, int elementId = 0)
        {
            __EnsureDirty();
            if (!__inDirty) return;   // not attached
            if (__fullDirty) return;
            if (__ops == null) __ops = new System.Collections.Generic.List<__PendingOp>();
            if (!__objectElems && __opIndex == null)
                __opIndex = new System.Collections.Generic.Dictionary<T, int>(m_Set.Comparer);
            if (__objectElems && op == __OP_ADD && elementId == 0) elementId = __ElementId(elem);
            int estimate = 1 + (op == __OP_CLEAR ? 0
                : (__objectElems ? SyncWire.GetVarInt32Size(elementId) : __EstimateScalarBytes(elem)));
            if (op == __OP_CLEAR)
            {
                __ops.Clear();
                if (__opIndex != null) __opIndex.Clear();
                __activeOpCount = 0;
                __nullOpIndex = -1;
                __pendingBytes = 0;
            }
            else if (!__objectElems)
            {
                bool isNull = (object)elem == null;
                int priorIndex;
                bool hasPrior = isNull
                    ? (priorIndex = __nullOpIndex) >= 0
                    : __opIndex.TryGetValue(elem, out priorIndex);
                if (hasPrior)
                {
                    var prior = __ops[priorIndex];
                    if (prior.Op != op)
                    {
                        __pendingBytes -= prior.EstimatedBytes;
                        if (priorIndex == __ops.Count - 1) __ops.RemoveAt(priorIndex);
                        else { prior.Op = 0; __ops[priorIndex] = prior; }
                        __activeOpCount--;
                        if (isNull) __nullOpIndex = -1; else __opIndex.Remove(elem);
                        if (__activeOpCount == 0) __inDirty = false;
                    }
                    __MaybeMarkFull();
                    return;
                }
            }
            int newIndex = __ops.Count;
            __ops.Add(new __PendingOp
            {
                Op = op,
                Element = __objectElems ? default : elem,
                ElementId = elementId,
                EstimatedBytes = estimate
            });
            __activeOpCount++;
            if (!__objectElems && op != __OP_CLEAR)
            {
                if ((object)elem == null) __nullOpIndex = newIndex;
                else __opIndex[elem] = newIndex;
            }
            __pendingBytes += estimate;
            __MaybeMarkFull();
        }

        private void __MaybeMarkFull()
        {
            if (__ops == null || __activeOpCount == 0) return;
            long deltaBytes = SyncWire.GetVarInt32Size(__SyncId) + 1L + SyncWire.GetVarInt32Size(__activeOpCount) + __pendingBytes;
            long minimumFullBytes = SyncWire.GetVarInt32Size(__SyncId) + 1L + SyncWire.GetVarInt32Size(m_Set.Count) + m_Set.Count;
            if (deltaBytes < minimumFullBytes) return;
            long fullBytes = SyncWire.GetVarInt32Size(__SyncId) + 1L + SyncWire.GetVarInt32Size(m_Set.Count);
            foreach (var element in m_Set)
                fullBytes += __objectElems ? SyncWire.GetVarInt32Size(__ElementId(element)) : __EstimateScalarBytes(element);
            if (deltaBytes >= fullBytes) __MarkFull();
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

        /// <summary>Full record (keyframe): [id][1][count][elements].</summary>
        public void __CaptureFull(System.IO.BinaryWriter writer)
        {
            __EnsureSyncInitialized();
            SyncWire.WriteVarInt32(writer, __SyncId);
            writer.Write((byte)1);
            SyncWire.WriteVarInt32(writer, m_Set.Count);
            foreach (var e in m_Set) __WriteElem(writer, e);
        }

        /// <summary>Incremental record: [id][0][opCount][ops] — or a full [id][1][count][elements] when fully dirty.</summary>
        public void __CaptureDelta(System.IO.BinaryWriter writer)
        {
            __EnsureSyncInitialized();
            SyncWire.WriteVarInt32(writer, __SyncId);
            if (__fullDirty || __ops == null || __ops.Count == 0)
            {
                writer.Write((byte)1);
                SyncWire.WriteVarInt32(writer, m_Set.Count);
                foreach (var e in m_Set) __WriteElem(writer, e);
                return;
            }
            writer.Write((byte)0);
            SyncWire.WriteVarInt32(writer, __activeOpCount);
            for (int i = 0; i < __ops.Count; i++)
            {
                var e = __ops[i];
                if (e.Op == 0) continue;
                writer.Write(e.Op);
                switch (e.Op)
                {
                    case __OP_ADD:
                    case __OP_REMOVE:
                        if (__objectElems) SyncWire.WriteVarInt32(writer, e.ElementId); else __wElem(writer, e.Element);
                        break;
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
                if (n < 0) throw new System.IO.InvalidDataException("The set element count cannot be negative.");
                foreach (var e in m_Set) __ClearElementParent(e);
                m_Set.Clear();
                for (int i = 0; i < n; i++) __ApplyAdd(__ReadElem(reader));
                __SyncContext.__TouchVersion(this);
                return;
            }
            int ops = SyncWire.ReadVarInt32(reader);
            if (ops < 0) throw new System.IO.InvalidDataException("The set operation count cannot be negative.");
            for (int k = 0; k < ops; k++)
            {
                byte op = reader.ReadByte();
                switch (op)
                {
                    case __OP_ADD: { __ApplyAdd(__ReadElem(reader)); break; }
                    case __OP_REMOVE: { var e = __ReadElem(reader); if (__TryRemoveStoredValue(e, out var stored)) __ClearElementParent(stored); break; }
                    case __OP_CLEAR: { foreach (var e in m_Set) __ClearElementParent(e); m_Set.Clear(); break; }
                }
            }
            __SyncContext.__TouchVersion(this);
        }
    }
}
