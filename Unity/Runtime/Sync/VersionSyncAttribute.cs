using System;

namespace ReactiveBinding
{
    /// <summary>
    /// Marks a <see cref="VersionFieldAttribute"/> field for inclusion in data synchronization.
    /// A class that contains at least one <c>[VersionSync]</c> field automatically implements
    /// <see cref="IVersionSyncable"/> (the generator emits full/delta byte serialization for it).
    /// Fields without this attribute are tracked for versioning but excluded from sync.
    /// </summary>
    /// <remarks>
    /// Requirements:
    /// - Must be applied together with <see cref="VersionFieldAttribute"/> (sync hooks the generated
    ///   property setter to record per-field dirty state).
    /// - Synced field types: bool/byte/int/long/float/double/string/enum, a nested concrete type that
    ///   itself has <c>[VersionSync]</c> fields, or a Version container.
    /// - Up to 64 synced fields per class (bitmask limit).
    /// </remarks>
    /// <example>
    /// <code>
    /// public partial class PlayerData : IVersion
    /// {
    ///     [VersionField][VersionSync] private int m_Health;     // synced
    ///     [VersionField]              private int m_TempCache;  // not synced
    /// }
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Field)]
    public class VersionSyncAttribute : Attribute
    {
    }
}
