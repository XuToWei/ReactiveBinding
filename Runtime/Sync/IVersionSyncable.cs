namespace ReactiveBinding
{
    /// <summary>
    /// Provides full / incremental (delta) byte-array synchronization for an object tree.
    /// Implemented automatically by the generator for any class that has at least one
    /// <see cref="VersionSyncAttribute"/> field. All members operate on the whole subtree
    /// rooted at this object (the parent automatically recurses into its children).
    /// </summary>
    /// <remarks>
    /// Model (snapshot + merged delta, compacted on Commit):
    /// <list type="bullet">
    /// <item><see cref="Commit"/> serializes the current state as the internal baseline and clears
    /// the accumulated increments (resets dirty flags / watermarks / container op-logs).</item>
    /// <item><see cref="GetFull"/> returns the baseline captured at the last <see cref="Commit"/>.</item>
    /// <item><see cref="GetDelta"/> returns the merged changes since the last <see cref="Commit"/>
    /// (latest value per field). It is non-destructive and may be called repeatedly.</item>
    /// </list>
    /// A consumer reconstructs the latest state with <c>Apply(GetFull())</c> followed by
    /// <c>ApplyDelta(GetDelta())</c>. If neither <see cref="GetFull"/> nor <see cref="GetDelta"/>
    /// has been preceded by a <see cref="Commit"/>, an implicit commit happens on first call
    /// (baseline = current full, delta = empty).
    /// </remarks>
    public interface IVersionSyncable
    {
        /// <summary>
        /// Records the current full state as the baseline and clears accumulated increments.
        /// </summary>
        void Commit();

        /// <summary>
        /// Gets the full baseline captured at the last <see cref="Commit"/> (commits implicitly if needed).
        /// </summary>
        byte[] GetFull();

        /// <summary>
        /// Gets the merged increment since the last <see cref="Commit"/>. Non-destructive.
        /// </summary>
        byte[] GetDelta();

        /// <summary>
        /// Applies a full snapshot produced by <see cref="GetFull"/>.
        /// </summary>
        void Apply(byte[] full);

        /// <summary>
        /// Applies an increment produced by <see cref="GetDelta"/> onto a state already at the same baseline.
        /// </summary>
        void ApplyDelta(byte[] delta);

        /// <summary>
        /// Clears the accumulated increment and re-baselines this subtree (recurses into children).
        /// Called by <see cref="Commit"/>; rarely needed directly.
        /// </summary>
        void ResetSync();
    }
}
