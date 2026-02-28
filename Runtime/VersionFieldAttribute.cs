using System;

namespace ReactiveBinding
{
    /// <summary>
    /// Marks a private field with m_ prefix for automatic property generation.
    /// The generator will also implement IVersion interface members automatically.
    /// </summary>
    /// <remarks>
    /// Requirements:
    /// - Field must start with "m_" prefix (e.g., m_Health)
    /// - Field must be private
    /// - Class must implement IVersion interface
    /// - Class must be partial
    ///
    /// Auto-generated members:
    /// - Property for each [VersionField] field (m_Health -> Health)
    /// - IVersion.Version property (returns VersionOwner's version or own version)
    /// - IVersion.VersionOwner property (set by parent container)
    /// - IVersion.IncrementVersion() method (increments owner's or own version)
    ///
    /// The generated class can work:
    /// - Standalone: tracks its own version via __version field
    /// - As container element: uses parent container's version via VersionOwner
    /// </remarks>
    /// <example>
    /// <code>
    /// // Just declare the interface and fields - everything else is generated
    /// public partial class PlayerData : IVersion
    /// {
    ///     [VersionField]
    ///     private int m_Health;
    ///
    ///     [VersionField]
    ///     private string m_Name;
    /// }
    ///
    /// // Usage standalone
    /// var player = new PlayerData();
    /// player.Health = 100;  // player.Version increments
    ///
    /// // Usage in container
    /// var list = new VersionList&lt;PlayerData&gt;();
    /// list.Add(player);     // player.VersionOwner = list
    /// player.Health = 50;   // list.Version increments
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Field)]
    public class VersionFieldAttribute : Attribute
    {
    }
}
