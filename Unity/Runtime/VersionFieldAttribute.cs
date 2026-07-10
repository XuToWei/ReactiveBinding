using System;

namespace ReactiveBinding
{
    /// <summary>
    /// Marks a private field with a <c>__</c> prefix for automatic property generation.
    /// The generator will also implement IVersion interface members automatically.
    /// </summary>
    /// <remarks>
    /// Requirements:
    /// - Field must start with a "__" prefix (e.g., __Health)
    /// - Field must be private
    /// - Class must implement IVersion interface
    /// - Class must be partial
    ///
    /// Auto-generated members:
    /// - Property for each [VersionField] field (__Health -> Health)
    /// - IVersion.__Version property (the node's local version)
    /// - IVersion.__Parent property (managed by generated setters and version containers)
    /// - IVersion.__IncrementVersion() method (increments locally, then propagates to the parent)
    ///
    /// The generated class can work:
    /// - Standalone: tracks its own version via the __Version property
    /// - As container element: propagates changes through __Parent while retaining its local version
    /// </remarks>
    /// <example>
    /// <code>
    /// // Just declare the interface and fields - everything else is generated
    /// public partial class PlayerData : IVersion
    /// {
    ///     [VersionField]
    ///     private int __Health;
    ///
    ///     [VersionField]
    ///     private string __Name;
    /// }
    ///
    /// // Usage standalone
    /// var player = new PlayerData();
    /// player.Health = 100;  // player.__Version increments
    ///
    /// // Usage in container
    /// var list = new VersionList&lt;PlayerData&gt;();
    /// list.Add(player);     // player.__Parent = list
    /// player.Health = 50;   // list.__Version increments
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Field)]
    public class VersionFieldAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Field, AllowMultiple = true)]
    public class VersionFieldPropertyAttribute : Attribute
    {
        public Type PropertyType { get; } = null!;
        public string PropertyText { get; } = null!;

        public VersionFieldPropertyAttribute(Type type)
        {
            PropertyType = type;
        }

        public VersionFieldPropertyAttribute(string text)
        {
            PropertyText = text;
        }
    }
}
