namespace Zakira.Conduit.Sources.GitHub;

/// <summary>
///     Options controlling how the GitHub fetcher talks to the API.
/// </summary>
public sealed class GitHubFetcherOptions
{
    /// <summary>
    ///     Base URL for the GitHub REST API. Override for tests.
    ///     Defaults to <c>https://api.github.com</c>.
    /// </summary>
    public Uri BaseAddress { get; set; } = new("https://api.github.com");

    /// <summary>
    ///     OAuth/PAT token included as <c>Authorization: Bearer ...</c> on every
    ///     request. When <see langword="null"/>, requests are made anonymously.
    /// </summary>
    public string? Token { get; set; }

    /// <summary>
    ///     The <c>User-Agent</c> header value (required by GitHub).
    /// </summary>
    public string UserAgent { get; set; } = "Zakira.Conduit";

    /// <summary>
    ///     Soft cap on the size of a fetched archive, in bytes. Exists to
    ///     guard against accidentally downloading a huge monorepo. Default is 256 MiB.
    /// </summary>
    public long MaxArchiveSizeBytes { get; set; } = 256L * 1024 * 1024;
}
