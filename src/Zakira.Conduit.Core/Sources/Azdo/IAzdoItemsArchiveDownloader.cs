using Zakira.Conduit.Manifest;

namespace Zakira.Conduit.Sources.Azdo;

/// <summary>
///     Downloads the recursive zip of a sub-tree of an Azure DevOps repository
///     at a specific commit, via the Items REST endpoint.
/// </summary>
public interface IAzdoItemsArchiveDownloader
{
    /// <summary>
    ///     Streams the zip for <paramref name="source"/> at <paramref name="commitSha"/>
    ///     into <paramref name="destinationStream"/>. When <paramref name="scopePath"/>
    ///     is <see langword="null"/>/empty, the whole repository is downloaded.
    /// </summary>
    Task DownloadAsync(
        AzdoSkillSource source,
        string commitSha,
        string? scopePath,
        Stream destinationStream,
        CancellationToken cancellationToken = default);
}
