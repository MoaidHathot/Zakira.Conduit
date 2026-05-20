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
    /// </summary>
    /// <param name="gitRef">A branch name, tag, or commit SHA.</param>
    Task<string> ResolveAsync(string owner, string repo, string gitRef, CancellationToken cancellationToken = default);
}
