using System;

namespace ReactiveBinding
{
    /// <summary>
    /// Controls the frequency of ObserveChanges() method execution.
    /// Specifies how many calls to ObserveChanges() are needed before the check is actually performed.
    /// </summary>
    /// <remarks>
    /// - A value of 1 means every call to ObserveChanges() performs a check (default behavior)
    /// - A value of 10 means only every 10th call performs a check
    /// - The first call always performs a check regardless of the throttle value
    /// </remarks>
    /// <example>
    /// <code>
    /// [ReactiveThrottle(10)]  // Check every 10 calls
    /// public partial class PlayerUI : IReactiveObserver
    /// {
    ///     // ...
    /// }
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Class)]
    public class ReactiveThrottleAttribute : Attribute
    {
        /// <summary>
        /// The number of ObserveChanges() calls needed before a check is performed.
        /// Must be greater than or equal to 1.
        /// </summary>
        public int CallCount { get; }

        /// <summary>
        /// Creates a new ReactiveThrottle attribute with the specified call count.
        /// </summary>
        /// <param name="callCount">The number of calls needed before a check is performed. Must be >= 1.</param>
        public ReactiveThrottleAttribute(int callCount)
        {
            CallCount = callCount;
        }
    }
}
