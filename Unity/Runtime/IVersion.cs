#nullable disable
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace ReactiveBinding
{
    /// <summary>
    /// Global version counter shared by all IVersion implementations.
    /// Thread-safe using Interlocked operations.
    /// </summary>
    public static class VersionCounter
    {
        private static int s_GlobalVersion;

        /// <summary>
        /// Gets the next global version number (thread-safe).
        /// </summary>
        public static int Next() => Interlocked.Increment(ref s_GlobalVersion);

        /// <summary>
        /// Reads the current global version without incrementing it.
        /// Used as a synchronization watermark for subtree pruning.
        /// </summary>
        public static int Current => Volatile.Read(ref s_GlobalVersion);
    }

    /// <summary>
    /// Enforces the ownership-tree invariant used by version propagation and synchronization: one
    /// <see cref="IVersion"/> instance may appear in exactly one field or container slot.
    /// </summary>
    public static class VersionOwnership
    {
        private sealed class ReferenceComparer : IEqualityComparer<IVersion>
        {
            public static readonly ReferenceComparer Instance = new ReferenceComparer();
            public bool Equals(IVersion x, IVersion y) => ReferenceEquals(x, y);
            public int GetHashCode(IVersion obj) => RuntimeHelpers.GetHashCode(obj);
        }

        [ThreadStatic] private static HashSet<IVersion> s_AttachSeenScratch;
        [ThreadStatic] private static bool s_AttachSeenScratchInUse;

        private static HashSet<IVersion> RentAttachSeenScratch()
        {
            if (!s_AttachSeenScratchInUse)
            {
                var scratch = s_AttachSeenScratch
                    ?? (s_AttachSeenScratch = new HashSet<IVersion>(ReferenceComparer.Instance));
                s_AttachSeenScratchInUse = true;
                return scratch;
            }

            // A custom/re-entrant enumerable can call EnsureCanAttachAll while the outer validation is active.
            // Keep that rare nested call isolated instead of clearing or corrupting the outer call's scratch set.
            return new HashSet<IVersion>(ReferenceComparer.Instance);
        }

        private static void ReturnAttachSeenScratch(HashSet<IVersion> scratch)
        {
            var releasePooledScratch = scratch.Count > 4096;
            scratch.Clear();
            if (!ReferenceEquals(scratch, s_AttachSeenScratch)) return;

            if (releasePooledScratch) s_AttachSeenScratch = null;
            s_AttachSeenScratchInUse = false;
        }

        /// <summary>Returns the next ownership ancestor for runtime synchronization traversal.</summary>
        internal static IVersion GetParent(IVersion node) => node.__Parent;

        /// <summary>Throws when <paramref name="child"/> cannot be attached below <paramref name="parent"/>.</summary>
        public static void EnsureCanAttach(IVersion parent, IVersion child)
        {
            if (parent == null) throw new ArgumentNullException(nameof(parent));
            if (child == null) return;

            if (ReferenceEquals(parent, child))
                throw new InvalidOperationException(
                    "An IVersion instance cannot own itself. The version object graph must be an acyclic ownership tree.");

            if (child.__Parent != null)
                throw new InvalidOperationException(
                    $"Cannot attach IVersion instance '{child.GetType().FullName}' to '{parent.GetType().FullName}': " +
                    $"it already belongs to '{child.__Parent.GetType().FullName}'. An IVersion instance can appear " +
                    "in only one field or container slot.");

            if (child is IVersionSync syncChild && syncChild.__SyncContext != null)
                throw new InvalidOperationException(
                    $"Cannot attach IVersionSync instance '{child.GetType().FullName}': it is already attached to a " +
                    "SyncContext. Reset it before moving it into another ownership tree.");

            // Attaching an ancestor below one of its descendants would create a cycle. All mutation paths call
            // this method, so a linear walk is sufficient once the invariant has been established.
            for (var ancestor = parent.__Parent; ancestor != null; ancestor = ancestor.__Parent)
            {
                if (ReferenceEquals(ancestor, child))
                    throw new InvalidOperationException(
                        "Cannot attach an ancestor below its descendant. The version object graph must be an acyclic ownership tree.");
            }
        }

        /// <summary>
        /// Validates a materialized batch before a collection mutates, including duplicate references inside
        /// the batch, so a failed ownership check leaves the collection unchanged.
        /// </summary>
        public static void EnsureCanAttachAll<T>(IVersion parent, IEnumerable<T> children)
        {
            if (children == null) throw new ArgumentNullException(nameof(children));
            IVersion firstChild = null;
            HashSet<IVersion> seen = null;
            try
            {
                foreach (var item in children)
                {
                    if (!(item is IVersion child)) continue;
                    EnsureCanAttach(parent, child);
                    if (firstChild == null)
                    {
                        firstChild = child;
                        continue;
                    }
                    if (seen == null)
                    {
                        seen = RentAttachSeenScratch();
                        seen.Add(firstChild);
                    }
                    if (!seen.Add(child))
                    {
                        throw new InvalidOperationException(
                            $"Cannot attach IVersion instance '{child.GetType().FullName}' more than once. " +
                            "An IVersion instance can appear in only one field or container slot.");
                    }
                }
            }
            finally
            {
                if (seen != null) ReturnAttachSeenScratch(seen);
            }
        }

        /// <summary>
        /// Uses reference identity for version nodes (their parent and sync identity are identity-based), and
        /// normal equality for scalar/value elements.
        /// </summary>
        public static bool AreSame<T>(T left, T right)
        {
            if (left is IVersion || right is IVersion)
                return ReferenceEquals(left, right);
            return EqualityComparer<T>.Default.Equals(left, right);
        }
    }

    /// <summary>
    /// Interface for version-tracked data.
    /// Each element tracks its own version independently.
    /// When an element changes, it increments its own version and notifies all parents up the chain.
    /// </summary>
    public interface IVersion
    {
        /// <summary>
        /// Gets the current version number. User code should read this property instead of the internal
        /// <see cref="__Version"/> protocol member.
        /// </summary>
        int Version { get; }

        /// <summary>
        /// Internal protocol storage for the current version. Reserved for generated code and the
        /// ReactiveBinding runtime; user code must use <see cref="Version"/>.
        /// </summary>
        int __Version { get; set; }

        /// <summary>
        /// Internal protocol link to the parent in the version chain.
        /// When this element changes, it will notify the parent via __IncrementVersion(),
        /// which propagates up through all ancestors. User code must not access this member.
        /// </summary>
        IVersion __Parent { get; set; }

        /// <summary>
        /// Internal protocol operation that increments this element's version and notifies __Parent.
        /// Uses VersionCounter.Next() to get a globally unique version number.
        /// Propagates up through the entire parent chain. User code must not call this member.
        /// </summary>
        void __IncrementVersion();

        /// <summary>
        /// Internal implementation target for <see cref="Reset"/>. Reserved for generated code and the
        /// ReactiveBinding runtime; user code must call <see cref="Reset"/> instead.
        /// </summary>
        void __Reset();

        /// <summary>
        /// Resets this subtree for reuse: zeroes each version, detaches the subtree root from its previous parent,
        /// and rebuilds ownership links between retained child fields or elements. Field values and container
        /// contents are retained. For an <see cref="IVersionSync"/> node this also clears its synchronization id,
        /// context, and dirty state so the tree can be attached again. Call only after the previous parent/context
        /// is no longer in use.
        /// </summary>
        void Reset();
    }
}
