namespace Zakira.Conduit.Mirroring;

/// <summary>
///     Mirrors the contents of a source directory into a target directory.
/// </summary>
public interface IDirectoryMirror
{
    /// <summary>
    ///     Replaces <paramref name="targetDirectory"/> with the contents of
    ///     <paramref name="sourceDirectory"/>. Intermediate directories are
    ///     created. The operation is performed via a sibling staging directory
    ///     and a swap so partial failures cannot leave the target half-updated.
    ///     <para>
    ///         When <paramref name="filter"/> is supplied, only files whose
    ///         source-root-relative path is accepted by the filter are
    ///         mirrored. Excluded files never appear in the staging directory,
    ///         and dirs left empty after filtering are not created.
    ///     </para>
    /// </summary>
    /// <returns>The number of files written.</returns>
    Task<int> MirrorAsync(string sourceDirectory, string targetDirectory, MirrorFilter? filter = null, CancellationToken cancellationToken = default);
}
