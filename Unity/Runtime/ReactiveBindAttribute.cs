using System;

namespace ReactiveBinding
{
    /// <summary>
    /// Marks a method as a callback to be invoked when bound reactive sources change.
    /// Use nameof() to specify the identity of the reactive sources to bind.
    /// </summary>
    /// <remarks>
    /// Callback method signatures:
    /// - 0 parameters: Called when any bound source changes
    /// - N parameters: (newValue1, newValue2, ...) - Only new values
    /// - 2N parameters: (oldValue1, newValue1, oldValue2, newValue2, ...) - Old and new values
    /// </remarks>
    /// <example>
    /// <code>
    /// // Single source binding with old and new values
    /// [ReactiveBind(nameof(Health))]
    /// private void OnHealthChanged(int oldValue, int newValue) { }
    ///
    /// // Multi-source binding with no parameters
    /// [ReactiveBind(nameof(Health), nameof(Mana))]
    /// private void OnStatsChanged() { }
    ///
    /// // Multi-source binding with only new values
    /// [ReactiveBind(nameof(Health), nameof(Mana))]
    /// private void OnStatsChangedNew(int newHealth, int newMana) { }
    ///
    /// // Multi-source binding with old and new values
    /// [ReactiveBind(nameof(Health), nameof(Mana))]
    /// private void OnStatsChangedFull(int oldHealth, int newHealth, int oldMana, int newMana) { }
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Method)]
    public class ReactiveBindAttribute : Attribute
    {
        /// <summary>
        /// The identities of the reactive sources to bind.
        /// </summary>
        public string[] ReactiveIds { get; }

        /// <summary>
        /// Creates a new ReactiveBind attribute with the specified source identities.
        /// </summary>
        /// <param name="reactiveIds">The identities of the reactive sources to bind. Use nameof() to specify.</param>
        public ReactiveBindAttribute(params string[] reactiveIds)
        {
            ReactiveIds = reactiveIds;
        }
    }
}
