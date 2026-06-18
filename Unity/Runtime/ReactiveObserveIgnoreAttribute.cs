using System;

namespace ReactiveBinding
{
    /// <summary>
    /// Ignores the RB0003 error when ObserveChanges() is not called within the class.
    /// Use this attribute when ObserveChanges() is called externally (e.g., by a manager or framework).
    /// </summary>
    /// <example>
    /// <code>
    /// [ReactiveObserveIgnore]
    /// public partial class PlayerUI : IReactiveObserver
    /// {
    ///     // ObserveChanges() is called by an external manager, not within this class
    /// }
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Class)]
    public class ReactiveObserveIgnoreAttribute : Attribute
    {
    }
}
