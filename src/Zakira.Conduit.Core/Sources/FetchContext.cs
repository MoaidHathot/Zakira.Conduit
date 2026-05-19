namespace Zakira.Conduit.Sources;

/// <summary>
///     Context handed to every <see cref="ISkillSourceFetcher"/> when materializing
///     a source. Carries the originating manifest's path so fetchers can resolve
///     relative inputs (e.g. <c>./skills</c>) against the manifest's directory.
/// </summary>
public sealed record FetchContext
{
    /// <summary>The absolute path of the manifest the entry was loaded from.</summary>
    public string ManifestPath { get; }

    /// <summary>The directory portion of <see cref="ManifestPath"/>.</summary>
    public string ManifestDirectory { get; }

    public FetchContext(string manifestPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);

        ManifestPath = Path.GetFullPath(manifestPath);
        ManifestDirectory = Path.GetDirectoryName(ManifestPath)
                            ?? throw new ArgumentException("Manifest path has no parent directory.", nameof(manifestPath));
    }
}
