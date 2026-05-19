namespace Zakira.Conduit.Sources.GitHub;

/// <summary>
///     Downloads a GitHub repository archive (zipball) for a given ref.
///     Pulled out into its own abstraction so the fetcher can be unit-tested
///     without touching the network.
/// </summary>
public interface IGitHubArchiveDownloader
{
    /// <summary>
    ///     Streams the zipball for <paramref name="owner"/>/<paramref name="repo"/>
    ///     into <paramref name="destinationStream"/>. When <paramref name="gitRef"/>
    ///     is <see langword="null"/>, the repository's default branch is used.
    /// </summary>
    Task DownloadAsync(string owner, string repo, string? gitRef, Stream destinationStream, CancellationToken cancellationToken = default);
}
