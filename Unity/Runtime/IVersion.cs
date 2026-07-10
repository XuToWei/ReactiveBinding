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
            public bool Equals(IVersion? x, IVersion? y) => ReferenceEquals(x, y);
            public int GetHashCode(IVersion obj) => RuntimeHelpers.GetHashCode(obj);
        }

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
            var seen = new HashSet<IVersion>(ReferenceComparer.Instance);
            foreach (var item in children)
            {
                if (!(item is IVersion child)) continue;
                EnsureCanAttach(parent, child);
                if (!seen.Add(child))
                {
                    throw new InvalidOperationException(
                        $"Cannot attach IVersion instance '{child.GetType().FullName}' more than once. " +
                        "An IVersion instance can appear in only one field or container slot.");
                }
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
        /// Gets the current version number of this element.
        /// Each element has its own independent version.
        /// </summary>
        int __Version { get; }

        /// <summary>
        /// Gets or sets the parent in the version chain.
        /// When this element changes, it will notify the parent via __IncrementVersion(),
        /// which propagates up through all ancestors.
        /// </summary>
        IVersion __Parent { get; set; }

        /// <summary>
        /// Increments this element's version and notifies __Parent.
        /// Uses VersionCounter.Next() to get a globally unique version number.
        /// Propagates up through the entire parent chain.
        /// </summary>
        void __IncrementVersion();

        /// <summary>
        /// Resets this subtree for reuse: zeroes each version, detaches the subtree root from its previous parent,
        /// and rebuilds the ownership links between retained child fields / elements. Field values and container
        /// contents are kept. For an
        /// <see cref="IVersionSync"/> node this additionally detaches it from its <c>SyncContext</c> (clears its
        /// sync id / dirty state) so the same tree can be re-attached to a fresh context. Call only when the
        /// previous owner (parent / context) is no longer in use.
        /// </summary>
        void __Reset();
    }
}
