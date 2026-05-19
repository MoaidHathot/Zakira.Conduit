namespace Zakira.Conduit.Manifest;

/// <summary>
///     Loads a <see cref="ConduitManifest"/> from disk.
/// </summary>
public interface IManifestLoader
{
    /// <summary>
    ///     Loads and validates the manifest at <paramref name="path"/>.
    /// </summary>
    /// <exception cref="ManifestException">If the file is missing, malformed or invalid.</exception>
    Task<ConduitManifest> LoadAsync(string path, CancellationToken cancellationToken = default);
}
