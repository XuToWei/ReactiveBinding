namespace ReactiveBinding
{
    /// <summary>
    /// Interface for version data containers.
    /// The Version property increments on each modification, enabling efficient change detection.
    /// </summary>
    public interface IVersion
    {
        /// <summary>
        /// Gets the current version number.
        /// This value increments each time the container is modified.
        /// </summary>
        int Version { get; }
    }
}
