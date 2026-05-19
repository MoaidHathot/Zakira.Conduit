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
    /// </summary>
    /// <returns>The number of files written.</returns>
    Task<int> MirrorAsync(string sourceDirectory, string targetDirectory, CancellationToken cancellationToken = default);
}
