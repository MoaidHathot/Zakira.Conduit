namespace Zakira.Conduit.Sources.GitHub;

/// <summary>
///     The outcome of a zipball download. Either the body was written and
///     <see cref="NotModified"/> is <see langword="false"/>, or the server
///     replied with 304 (matching <c>If-None-Match</c>) and nothing was
///     written.
/// </summary>
/// <param name="NotModified">
///     <see langword="true"/> when the server returned HTTP 304 in response
///     to a conditional request. The destination stream was not written.
/// </param>
/// <param name="Etag">
///     The <c>ETag</c> header from the server, when present. Stable across
///     calls for the same content; pass it back as <c>ifNoneMatchEtag</c> on
///     the next call to short-circuit unchanged downloads.
/// </param>
/// <param name="ResolvedRef">
///     The short commit SHA encoded into the zipball's top-level folder name,
///     when the downloader could extract it. <see langword="null"/> for 304s.
/// </param>
public sealed record GitHubDownloadResult(bool NotModified, string? Etag, string? ResolvedRef);

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
    ///     When <paramref name="ifNoneMatchEtag"/> is set, the downloader sends
    ///     <c>If-None-Match</c> and short-circuits on HTTP 304.
    /// </summary>
    Task<GitHubDownloadResult> DownloadAsync(
        string owner,
        string repo,
        string? gitRef,
        Stream destinationStream,
        string? ifNoneMatchEtag = null,
        CancellationToken cancellationToken = default);
}
