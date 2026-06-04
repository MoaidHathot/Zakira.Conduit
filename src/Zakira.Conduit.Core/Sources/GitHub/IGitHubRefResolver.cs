using System.Net.Http.Headers;

namespace Zakira.Conduit.Sources.GitHub;

/// <summary>
///     Resolves a GitHub branch/tag/ref to its current commit SHA. Used by the
///     <c>pin</c> and <c>update</c> CLI commands to lock a manifest entry to
///     the immutable head of its tracked branch.
/// </summary>
public interface IGitHubRefResolver
{
    /// <summary>
    ///     Returns the full commit SHA at the tip of <paramref name="gitRef"/>.
    ///     <paramref name="authHeader"/>, when supplied, is attached as the
    ///     outbound request's <c>Authorization</c> header; otherwise the
    ///     resolver falls back to the token configured in
    ///     <see cref="GitHubFetcherOptions"/>.
    /// </summary>
    /// <param name="gitRef">A branch name, tag, or commit SHA.</param>
    Task<string> ResolveAsync(string owner, string repo, string gitRef, AuthenticationHeaderValue? authHeader = null, CancellationToken cancellationToken = default);
}
