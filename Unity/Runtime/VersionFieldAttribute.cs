#nullable disable
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
    /// - Public IVersion.Version and IVersion.Reset() APIs
    /// - Internal double-underscore version/ownership protocol used by generated/runtime code
    ///
    /// The generated class can work:
    /// - Standalone: tracks its own version through the Version property
    /// - As container element: propagates changes through its internal parent while retaining its local version
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
    /// player.Health = 100;  // player.Version increments
    ///
    /// // Usage in container
    /// var list = new VersionList&lt;PlayerData&gt;();
    /// list.Add(player);     // internal ownership is wired automatically
    /// player.Health = 50;   // list.Version increments
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Field)]
    public class VersionFieldAttribute : Attribute
    {
    }
}
