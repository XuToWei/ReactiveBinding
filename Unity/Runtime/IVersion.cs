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
    }
}
