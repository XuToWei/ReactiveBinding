using System;

namespace ReactiveBinding
{
    /// <summary>
    /// Marks a field, property, or method as a reactive data source.
    /// The member name is automatically used as the identity for binding.
    /// </summary>
    /// <remarks>
    /// Supported targets:
    /// - Fields: The field value is monitored for changes
    /// - Properties: Must have a getter. The property value is monitored for changes
    /// - Methods: Must have no parameters and return a value. The return value is monitored for changes
    /// </remarks>
    /// <example>
    /// <code>
    /// [ReactiveSource]
    /// private int Health => playerData.Health;  // Identity is "Health"
    ///
    /// [ReactiveSource]
    /// private int GetMana() => playerData.Mana;  // Identity is "GetMana"
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Method)]
    public class ReactiveSourceAttribute : Attribute
    {
    }
}
