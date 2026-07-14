#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;

namespace ReactiveBinding
{
    /// <summary>
    /// A list that tracks modifications via a version number AND participates in flat-registry data synchronization
    /// (<see cref="IVersionSync"/>). Use this — not the version-only <see cref="VersionList{T}"/> — for a
    /// <c>[VersionField]</c> container inside an <see cref="IVersionSync"/> class. Scalar elements serialize inline
    /// via owner-injected delegates; object elements (<c>T</c> is an <see cref="IVersionSync"/> type) sync as
    /// referenced registry nodes. Structural changes are coalesced into an adaptive per-frame op log that falls
    /// back to a full record after excessive churn. This is a standalone type (not a subclass of
    /// <see cref="VersionList{T}"/>).
    /// </summary>
    /// <typeparam name="T">The type of elements in the list.</typeparam>
    public class VersionSyncList<T> : IList<T>, IReadOnlyList<T>, IVersionSync
    {
        private readonly List<T> m_List;

        /// <summary>Creates a new empty VersionSyncList.</summary>
        public VersionSyncList() { m_List = new List<T>(); }

        /// <summary>Creates a new VersionSyncList with the specified initial capacity.</summary>
        public VersionSyncList(int capacity) { m_List = new List<T>(capacity); }

        /// <summary>Creates a new VersionSyncList containing elements from the specified collection.</summary>
        public VersionSyncList(IEnumerable<T> collection)
        {
            m_List = new List<T>(collection);
            VersionOwnership.EnsureCanAttachAll(this, m_List);
            foreach (var item in m_List)
            {
                if (item is IVersion v) v.__Parent = this;
            }
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

        /// <inheritdoc/>
        public int Count => m_List.Count;

        /// <inheritdoc/>
        public bool IsReadOnly => false;

        /// <inheritdoc/>
        public T this[int index]
        {
            get => m_List[index];
            set
            {
                var oldItem = m_List[index];
                if (VersionOwnership.AreSame(oldItem, value)) return;
                if (value is IVersion child) VersionOwnership.EnsureCanAttach(this, child);
                m_List[index] = value;
                if (oldItem is IVersion ov) ov.__Parent = null;
                if (__SyncContext != null && oldItem is IVersionSync os) __Recurse(SyncOp.Unregister, os);
                if (value is IVersion nv) nv.__Parent = this;
                if (__SyncContext != null && value is IVersionSync ns) __Recurse(SyncOp.Attach, ns);
                __IncrementVersion(); __LogOp(__OP_SET, index, value);
            }
        }

        /// <inheritdoc/>
        public void Add(T item)
        {
            if (item is IVersion child) VersionOwnership.EnsureCanAttach(this, child);
            m_List.Add(item);
            if (item is IVersion v) v.__Parent = this;
            if (__SyncContext != null && item is IVersionSync s) __Recurse(SyncOp.Attach, s);
            __IncrementVersion(); __LogOp(__OP_ADD, 0, item);
        }

        /// <summary>Adds the elements of the specified collection to the end of the list.</summary>
        public void AddRange(IEnumerable<T> collection)
        {
            if (collection == null) throw new ArgumentNullException(nameof(collection));
            if (collection is ICollection<T> stable)
            {
                if (stable.Count == 0) return;
                VersionOwnership.EnsureCanAttachAll(this, stable);
                int addedCount = stable.Count;
                int first = m_List.Count;
                m_List.AddRange(stable);
                for (int i = first; i < m_List.Count; i++)
                {
                    var item = m_List[i];
                    if (item is IVersion v) v.__Parent = this;
                    if (__SyncContext != null && item is IVersionSync s) __Recurse(SyncOp.Attach, s);
                }
                __IncrementVersion(); __LogRange(__OP_ADDRANGE, 0, first, addedCount);
                return;
            }

            var items = new List<T>(collection);
            if (items.Count == 0) return;
            VersionOwnership.EnsureCanAttachAll(this, items);
            int start = m_List.Count;
            m_List.AddRange(items);
            for (int i = start; i < m_List.Count; i++)
            {
                var item = m_List[i];
                if (item is IVersion v) v.__Parent = this;
                if (__SyncContext != null && item is IVersionSync s) __Recurse(SyncOp.Attach, s);
            }
            __IncrementVersion(); __LogRange(__OP_ADDRANGE, 0, start, items.Count);
        }

        /// <inheritdoc/>
        public void Clear()
        {
            if (m_List.Count == 0) return;
            for (int i = 0; i < m_List.Count; i++)
            {
                var item = m_List[i];
                if (item is IVersion v) v.__Parent = null;
                if (__SyncContext != null && item is IVersionSync s) __Recurse(SyncOp.Unregister, s);
            }
            m_List.Clear();
            __IncrementVersion(); __LogOp(__OP_CLEAR, 0, default);
        }

        /// <inheritdoc/>
        public bool Contains(T item) => m_List.Contains(item);

        /// <inheritdoc/>
        public void CopyTo(T[] array, int arrayIndex) => m_List.CopyTo(array, arrayIndex);

        /// <inheritdoc/>
        public IEnumerator<T> GetEnumerator() => m_List.GetEnumerator();

        /// <inheritdoc/>
        IEnumerator IEnumerable.GetEnumerator() => m_List.GetEnumerator();

        /// <inheritdoc/>
        public int IndexOf(T item) => m_List.IndexOf(item);

        /// <inheritdoc/>
        public void Insert(int index, T item)
        {
            if (index < 0 || index > m_List.Count) throw new ArgumentOutOfRangeException(nameof(index));
            if (item is IVersion child) VersionOwnership.EnsureCanAttach(this, child);
            m_List.Insert(index, item);
            if (item is IVersion v) v.__Parent = this;
            if (__SyncContext != null && item is IVersionSync s) __Recurse(SyncOp.Attach, s);
            __IncrementVersion(); __LogOp(__OP_INSERT, index, item);
        }

        /// <summary>Inserts the elements of a collection at the specified index.</summary>
        public void InsertRange(int index, IEnumerable<T> collection)
        {
            if (index < 0 || index > m_List.Count) throw new ArgumentOutOfRangeException(nameof(index));
            if (collection == null) throw new ArgumentNullException(nameof(collection));
            if (collection is ICollection<T> stable)
            {
                if (stable.Count == 0) return;
                VersionOwnership.EnsureCanAttachAll(this, stable);
                int count = stable.Count;
                m_List.InsertRange(index, stable);
                for (int i = index; i < index + count; i++)
                {
                    var item = m_List[i];
                    if (item is IVersion v) v.__Parent = this;
                    if (__SyncContext != null && item is IVersionSync s) __Recurse(SyncOp.Attach, s);
                }
                __IncrementVersion(); __LogRange(__OP_INSERTRANGE, index, index, count);
                return;
            }

            var items = new List<T>(collection);
            if (items.Count == 0) return;
            VersionOwnership.EnsureCanAttachAll(this, items);
            m_List.InsertRange(index, items);
            for (int i = index; i < index + items.Count; i++)
            {
                var item = m_List[i];
                if (item is IVersion v) v.__Parent = this;
                if (__SyncContext != null && item is IVersionSync s) __Recurse(SyncOp.Attach, s);
            }
            __IncrementVersion(); __LogRange(__OP_INSERTRANGE, index, index, items.Count);
        }

        /// <inheritdoc/>
        public bool Remove(T item)
        {
            int idx = m_List.IndexOf(item);
            var removed = idx >= 0;
            if (removed)
            {
                var storedItem = m_List[idx];
                m_List.RemoveAt(idx);
                if (storedItem is IVersion v) v.__Parent = null;
                if (__SyncContext != null && storedItem is IVersionSync s) __Recurse(SyncOp.Unregister, s);
                __IncrementVersion(); __LogOp(__OP_REMOVEAT, idx, default);
            }
            return removed;
        }

        /// <inheritdoc/>
        public void RemoveAt(int index)
        {
            var item = m_List[index];
            m_List.RemoveAt(index);
            if (item is IVersion v) v.__Parent = null;
            if (__SyncContext != null && item is IVersionSync s) __Recurse(SyncOp.Unregister, s);
            __IncrementVersion(); __LogOp(__OP_REMOVEAT, index, default);
        }

        /// <summary>Removes a range of elements from the list.</summary>
        public void RemoveRange(int index, int count)
        {
            __ValidateRange(index, count);
            if (count == 0) return;
            int end = index + count;
            for (int i = index; i < end; i++)
            {
                var item = m_List[i];
                if (item is IVersion v) v.__Parent = null;
                if (__SyncContext != null && item is IVersionSync s) __Recurse(SyncOp.Unregister, s);
            }
            m_List.RemoveRange(index, count);
            __IncrementVersion(); __LogRemoveRange(index, count);
        }

        /// <summary>Removes all elements that match the conditions defined by the specified predicate.</summary>
        public int RemoveAll(Predicate<T> match)
        {
            if (match == null) throw new ArgumentNullException(nameof(match));
            int write = 0;
            int originalCount = m_List.Count;
            int removeStart = -1;
            int removeCount = 0;
            for (int read = 0; read < originalCount; read++)
            {
                var item = m_List[read];
                if (match(item))
                {
                    if (removeStart < 0) removeStart = write;
                    removeCount++;
                    if (item is IVersion v) v.__Parent = null;
                    if (__SyncContext != null && item is IVersionSync s) __Recurse(SyncOp.Unregister, s);
                    continue;
                }
                if (removeCount != 0)
                {
                    __LogRemoveRange(removeStart, removeCount);
                    removeStart = -1;
                    removeCount = 0;
                }
                if (write != read) m_List[write] = item;
                write++;
            }
            if (removeCount != 0) __LogRemoveRange(removeStart, removeCount);
            int count = originalCount - write;
            if (count == 0) return 0;
            m_List.RemoveRange(write, count);
            __IncrementVersion();
            return count;
        }

        private void __ValidateRange(int index, int count)
        {
            if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            if (m_List.Count - index < count) throw new ArgumentException("The range exceeds the list bounds.");
        }

        /// <summary>Reverses the order of the elements in the list.</summary>
        public void Reverse() { if (m_List.Count <= 1) return; m_List.Reverse(); __IncrementVersion(); __MarkFull(); }

        /// <summary>Reverses the order of the elements in the specified range.</summary>
        public void Reverse(int index, int count) { m_List.Reverse(index, count); if (count > 1) { __IncrementVersion(); __MarkFull(); } }

        /// <summary>Sorts the elements in the list using the default comparer.</summary>
        public void Sort() { if (m_List.Count <= 1) return; m_List.Sort(); __IncrementVersion(); __MarkFull(); }

        /// <summary>Sorts the elements in the list using the specified comparison.</summary>
        public void Sort(Comparison<T> comparison) { m_List.Sort(comparison); if (m_List.Count > 1) { __IncrementVersion(); __MarkFull(); } }

        /// <summary>Sorts the elements in the list using the specified comparer.</summary>
        public void Sort(IComparer<T> comparer) { m_List.Sort(comparer); if (m_List.Count > 1) { __IncrementVersion(); __MarkFull(); } }

        /// <summary>Sorts a range of elements using the specified comparer.</summary>
        public void Sort(int index, int count, IComparer<T> comparer) { m_List.Sort(index, count, comparer); if (count > 1) { __IncrementVersion(); __MarkFull(); } }

        /// <summary>Sorts only when the list is not already ordered; returns whether its order changed.</summary>
        public bool SortIfNeeded() => SortIfNeeded(0, m_List.Count, Comparer<T>.Default);

        /// <summary>Sorts only when the list is not already ordered by <paramref name="comparer"/>.</summary>
        public bool SortIfNeeded(IComparer<T> comparer) => SortIfNeeded(0, m_List.Count, comparer);

        /// <summary>Sorts only when the list is not already ordered by <paramref name="comparison"/>.</summary>
        public bool SortIfNeeded(Comparison<T> comparison)
        {
            if (comparison == null) throw new ArgumentNullException(nameof(comparison));
            if (m_List.Count <= 1) return false;
            for (int i = 1; i < m_List.Count; i++)
                if (comparison(m_List[i - 1], m_List[i]) > 0)
                {
                    m_List.Sort(comparison);
                    __IncrementVersion(); __MarkFull();
                    return true;
                }
            return false;
        }

        /// <summary>Sorts a range only when it is not already ordered; returns whether its order changed.</summary>
        public bool SortIfNeeded(int index, int count, IComparer<T> comparer)
        {
            __ValidateRange(index, count);
            if (count <= 1) return false;
            comparer ??= Comparer<T>.Default;
            int end = index + count;
            for (int i = index + 1; i < end; i++)
                if (comparer.Compare(m_List[i - 1], m_List[i]) > 0)
                {
                    m_List.Sort(index, count, comparer);
                    __IncrementVersion(); __MarkFull();
                    return true;
                }
            return false;
        }

        /// <summary>Sets the capacity to the actual number of elements in the list.</summary>
        public void TrimExcess() => m_List.TrimExcess();

        /// <summary>Gets or sets the total number of elements the internal data structure can hold.</summary>
        public int Capacity
        {
            get => m_List.Capacity;
            set => m_List.Capacity = value;
        }

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

        /// <summary>Scalar-element mode: owner injects element write/read delegates.</summary>
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

        /// <summary>Object-element mode: owner injects an element factory; elements sync as referenced nodes.</summary>
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
                    "VersionSyncList synchronization is not initialized. Assign it through a generated " +
                    "[VersionField] property or call InitSync before AttachTo/Capture/Apply.");
        }

        private void __WriteElem(System.IO.BinaryWriter writer, T item)
        {
            if (__objectElems) writer.WriteVarInt32(item == null ? 0 : ((IVersionSync)(object)item).__SyncId);
            else __wElem(writer, item);
        }

        private T __ReadElem(System.IO.BinaryReader reader)
        {
            if (!__objectElems) return __rElem(reader);
            int __id = reader.ReadVarInt32();
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
                        child.__MarkAllDirty();   // child before its descendants
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
                    child.__Reset();   // leaving the graph -> full reset (id/context/dirty/version/parent, recurses)
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
            for (int i = 0; i < m_List.Count; i++)
                if (m_List[i] is IVersionSync s) __Recurse(op, s);
        }

        // ----- Incremental recording: per-frame op log, coalesced into one record by CaptureDelta. -----
        private const byte __OP_ADD = 1, __OP_INSERT = 2, __OP_SET = 3, __OP_REMOVEAT = 4, __OP_CLEAR = 5,
            __OP_ADDRANGE = 6, __OP_INSERTRANGE = 7, __OP_REMOVERANGE = 8;
        private bool __inDirty;     // has pending structural changes this frame (drives __IsDirty)
        private bool __fullDirty;   // flush the whole contents instead of the op log
        private struct __LoggedElement { public T Value; public int Id; }
        private struct __PendingOp
        {
            public byte Op;
            public int Index;
            public int Count;
            public int ElementStart;
            public __LoggedElement Element;
            public int EstimatedBytes;
        }
        private System.Collections.Generic.List<__PendingOp> __ops;
        private System.Collections.Generic.List<__LoggedElement> __opElements;
        private System.Collections.Generic.List<T> __applyRange;
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
            __inDirty = false; __fullDirty = false; __pendingBytes = 0;
            if (__ops != null) __ops.Clear();
            if (__opElements != null) __opElements.Clear();
        }

        /// <inheritdoc/>
        public void __Reset()
        {
            for (int i = 0; i < m_List.Count; i++)
                if (m_List[i] is IVersion v)
                {
                    v.__Reset();
                    v.__Parent = this;
                }
            if (__SyncContext != null) __SyncContext.__Objects.Remove(__SyncId);
            __SyncId = 0; __SyncContext = null;
            __inDirty = false; __fullDirty = false; __pendingBytes = 0;
            if (__ops != null) __ops.Clear();
            if (__opElements != null) __opElements.Clear();
            __Version = 0; __Parent = null;   // keeps contents; detaches for reuse
        }

        /// <inheritdoc/>
        public void Reset() => __Reset();

        private void __MarkFull()
        {
            __EnsureDirty();
            __fullDirty = true;
            if (__ops != null) __ops.Clear();
            if (__opElements != null) __opElements.Clear();
            __pendingBytes = 0;
        }

        private __LoggedElement __LogElement(T elem)
            => new __LoggedElement
            {
                Value = __objectElems ? default : elem,
                Id = __objectElems && elem != null ? ((IVersionSync)(object)elem).__SyncId : 0
            };

        private void __WriteLoggedElement(System.IO.BinaryWriter writer, __LoggedElement elem)
        {
            if (__objectElems) writer.WriteVarInt32(elem.Id);
            else __wElem(writer, elem.Value);
        }

        private int __EstimateElementBytes(__LoggedElement elem)
        {
            if (__objectElems) return SyncWire.GetVarInt32Size(elem.Id);
            object value = elem.Value;
            if (value is string text)
            {
                int bytes = System.Text.Encoding.UTF8.GetByteCount(text);
                return 1 + SyncWire.GetVarInt32Size(bytes) + bytes;
            }
            Type type = typeof(T);
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

        private void __LogOp(byte op, int idx, T elem)
        {
            __EnsureDirty();
            if (!__inDirty) return;   // not attached
            if (__fullDirty) return;
            if (__ops == null) __ops = new System.Collections.Generic.List<__PendingOp>();
            var logged = __LogElement(elem);
            int estimate = 1 + ((op == __OP_INSERT || op == __OP_SET || op == __OP_REMOVEAT) ? SyncWire.GetVarInt32Size(idx) : 0)
                + ((op == __OP_ADD || op == __OP_INSERT || op == __OP_SET) ? __EstimateElementBytes(logged) : 0);
            if (op == __OP_CLEAR)
            {
                __ops.Clear();
                if (__opElements != null) __opElements.Clear();
                __pendingBytes = 0;
            }
            else if (op == __OP_SET && __ops.Count != 0)
            {
                int lastIndex = __ops.Count - 1;
                var last = __ops[lastIndex];
                if (last.Op == __OP_SET && last.Index == idx)
                {
                    __pendingBytes -= last.EstimatedBytes;
                    __ops[lastIndex] = new __PendingOp { Op = op, Index = idx, Element = logged, EstimatedBytes = estimate };
                    __pendingBytes += estimate;
                    __MaybeMarkFull();
                    return;
                }
            }
            __ops.Add(new __PendingOp { Op = op, Index = idx, Element = logged, EstimatedBytes = estimate });
            __pendingBytes += estimate;
            __MaybeMarkFull();
        }

        private void __LogRange(byte op, int index, int sourceIndex, int count)
        {
            __EnsureDirty();
            if (!__inDirty || __fullDirty || count == 0) return;
            if (__ops == null) __ops = new System.Collections.Generic.List<__PendingOp>();
            if (__opElements == null) __opElements = new System.Collections.Generic.List<__LoggedElement>();
            int start = __opElements.Count;
            int estimate = 1 + SyncWire.GetVarInt32Size(count);
            if (op == __OP_INSERTRANGE) estimate += SyncWire.GetVarInt32Size(index);
            for (int i = 0; i < count; i++)
            {
                var logged = __LogElement(m_List[sourceIndex + i]);
                __opElements.Add(logged);
                estimate += __EstimateElementBytes(logged);
            }
            __ops.Add(new __PendingOp { Op = op, Index = index, Count = count, ElementStart = start, EstimatedBytes = estimate });
            __pendingBytes += estimate;
            __MaybeMarkFull();
        }

        private void __LogRemoveRange(int index, int count)
        {
            __EnsureDirty();
            if (!__inDirty || __fullDirty || count == 0) return;
            if (__ops == null) __ops = new System.Collections.Generic.List<__PendingOp>();
            int estimate = 1 + SyncWire.GetVarInt32Size(index) + SyncWire.GetVarInt32Size(count);
            __ops.Add(new __PendingOp { Op = __OP_REMOVERANGE, Index = index, Count = count, EstimatedBytes = estimate });
            __pendingBytes += estimate;
            __MaybeMarkFull();
        }

        private void __MaybeMarkFull()
        {
            if (__ops == null || __ops.Count == 0) return;
            long minimumFullBytes = SyncWire.GetVarInt32Size(__SyncId) + 1L + SyncWire.GetVarInt32Size(m_List.Count) + m_List.Count;
            long deltaBytes = SyncWire.GetVarInt32Size(__SyncId) + 1L + SyncWire.GetVarInt32Size(__ops.Count) + __pendingBytes;
            if (deltaBytes < minimumFullBytes) return;
            long fullBytes = SyncWire.GetVarInt32Size(__SyncId) + 1L + SyncWire.GetVarInt32Size(m_List.Count);
            for (int i = 0; i < m_List.Count; i++) fullBytes += __EstimateElementBytes(__LogElement(m_List[i]));
            if (deltaBytes >= fullBytes) __MarkFull();
        }

        /// <summary>Full record (keyframe): [id][1][count][elements].</summary>
        public void __CaptureFull(System.IO.BinaryWriter writer)
        {
            __EnsureSyncInitialized();
            writer.WriteVarInt32(__SyncId);
            writer.Write((byte)1);
            writer.WriteVarInt32(m_List.Count);
            for (int i = 0; i < m_List.Count; i++) __WriteElem(writer, m_List[i]);
        }

        /// <summary>Incremental record: [id][0][opCount][ops] — or a full [id][1][count][elements] when fully dirty.</summary>
        public void __CaptureDelta(System.IO.BinaryWriter writer)
        {
            __EnsureSyncInitialized();
            writer.WriteVarInt32(__SyncId);
            if (__fullDirty || __ops == null || __ops.Count == 0)
            {
                writer.Write((byte)1);
                writer.WriteVarInt32(m_List.Count);
                for (int i = 0; i < m_List.Count; i++) __WriteElem(writer, m_List[i]);
                return;
            }
            writer.Write((byte)0);
            writer.WriteVarInt32(__ops.Count);
            for (int i = 0; i < __ops.Count; i++)
            {
                var e = __ops[i];
                writer.Write(e.Op);
                switch (e.Op)
                {
                    case __OP_ADD: __WriteLoggedElement(writer, e.Element); break;
                    case __OP_INSERT: writer.WriteVarInt32(e.Index); __WriteLoggedElement(writer, e.Element); break;
                    case __OP_SET: writer.WriteVarInt32(e.Index); __WriteLoggedElement(writer, e.Element); break;
                    case __OP_REMOVEAT: writer.WriteVarInt32(e.Index); break;
                    case __OP_CLEAR: break;
                    case __OP_ADDRANGE:
                        writer.WriteVarInt32(e.Count);
                        for (int j = 0; j < e.Count; j++) __WriteLoggedElement(writer, __opElements[e.ElementStart + j]);
                        break;
                    case __OP_INSERTRANGE:
                        writer.WriteVarInt32(e.Index); writer.WriteVarInt32(e.Count);
                        for (int j = 0; j < e.Count; j++) __WriteLoggedElement(writer, __opElements[e.ElementStart + j]);
                        break;
                    case __OP_REMOVERANGE:
                        writer.WriteVarInt32(e.Index); writer.WriteVarInt32(e.Count); break;
                }
            }
        }

        /// <summary>Applies a node record: a full [1][count][elements] rebuild, or a [0][opCount][ops] replay.
        /// Advances the local version but does not mark outbound sync state dirty; sets element __Parent directly
        /// without triggering sync attach/unregister (object elements are resolved/created inline by __ReadElem).</summary>
        public void __Apply(System.IO.BinaryReader reader)
        {
            __EnsureSyncInitialized();
            if (reader.ReadByte() == 1)
            {
                int n = reader.ReadVarInt32();
                if (n < 0) throw new System.IO.InvalidDataException("The list element count cannot be negative.");
                foreach (var e in m_List) if (e is IVersion v) v.__Parent = null;
                m_List.Clear();
                for (int i = 0; i < n; i++) { var e = __ReadElem(reader); m_List.Add(e); if (e is IVersion v) v.__Parent = this; }
                __SyncContext.__TouchVersion(this);
                return;
            }
            int ops = reader.ReadVarInt32();
            if (ops < 0) throw new System.IO.InvalidDataException("The list operation count cannot be negative.");
            for (int k = 0; k < ops; k++)
            {
                byte op = reader.ReadByte();
                switch (op)
                {
                    case __OP_ADD: { var e = __ReadElem(reader); m_List.Add(e); if (e is IVersion v) v.__Parent = this; break; }
                    case __OP_INSERT: { int i = reader.ReadVarInt32(); var e = __ReadElem(reader); m_List.Insert(i, e); if (e is IVersion v) v.__Parent = this; break; }
                    case __OP_SET: { int i = reader.ReadVarInt32(); var e = __ReadElem(reader); if (m_List[i] is IVersion ov) ov.__Parent = null; m_List[i] = e; if (e is IVersion v) v.__Parent = this; break; }
                    case __OP_REMOVEAT: { int i = reader.ReadVarInt32(); if (m_List[i] is IVersion ov) ov.__Parent = null; m_List.RemoveAt(i); break; }
                    case __OP_CLEAR: { foreach (var e in m_List) if (e is IVersion v) v.__Parent = null; m_List.Clear(); break; }
                    case __OP_ADDRANGE:
                    {
                        int count = reader.ReadVarInt32();
                        if (count < 0) throw new System.IO.InvalidDataException("A list range count cannot be negative.");
                        for (int i = 0; i < count; i++) { var e = __ReadElem(reader); m_List.Add(e); if (e is IVersion v) v.__Parent = this; }
                        break;
                    }
                    case __OP_INSERTRANGE:
                    {
                        int index = reader.ReadVarInt32(); int count = reader.ReadVarInt32();
                        if (index < 0 || count < 0)
                            throw new System.IO.InvalidDataException("A list range index and count cannot be negative.");
                        if (__applyRange == null) __applyRange = new System.Collections.Generic.List<T>(count);
                        __applyRange.Clear();
                        for (int i = 0; i < count; i++) __applyRange.Add(__ReadElem(reader));
                        m_List.InsertRange(index, __applyRange);
                        for (int i = index; i < index + count; i++) if (m_List[i] is IVersion v) v.__Parent = this;
                        __applyRange.Clear();
                        break;
                    }
                    case __OP_REMOVERANGE:
                    {
                        int index = reader.ReadVarInt32(); int count = reader.ReadVarInt32();
                        if (index < 0 || count < 0)
                            throw new System.IO.InvalidDataException("A list range index and count cannot be negative.");
                        for (int i = index; i < index + count; i++) if (m_List[i] is IVersion v) v.__Parent = null;
                        m_List.RemoveRange(index, count);
                        break;
                    }
                }
            }
            __SyncContext.__TouchVersion(this);
        }
    }
}
