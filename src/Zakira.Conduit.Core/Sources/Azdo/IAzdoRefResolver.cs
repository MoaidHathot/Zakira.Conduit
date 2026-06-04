using Zakira.Conduit.Manifest;

namespace Zakira.Conduit.Sources.Azdo;

/// <summary>
///     Resolves an Azure DevOps branch / tag / commit ref to its full commit
///     SHA. Used by both the fetcher (cache check) and the
///     <c>pin</c> / <c>update</c> CLI commands.
/// </summary>
public interface IAzdoRefResolver
{
    /// <summary>
    ///     Returns the full commit SHA at the tip of the given ref. When
    ///     <paramref name="refKind"/> is <c>"commit"</c> the value is returned
    ///     verbatim after a sanity-check.
    /// </summary>
    /// <param name="source">The source providing org/project/repo + auth.</param>
    /// <param name="refValue">The branch name, tag name, or commit SHA.</param>
    /// <param name="refKind">One of <c>"branch"</c>, <c>"tag"</c>, <c>"commit"</c>.</param>
    Task<string> ResolveAsync(AzdoSource source, string refValue, string refKind, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Returns the repository's default branch (the short name, e.g.
    ///     <c>"main"</c>, not the <c>refs/heads/&#8230;</c> form). Used by
    ///     <c>conduit unpin</c> when no explicit <c>--to</c> branch is given.
    /// </summary>
    Task<string> GetDefaultBranchAsync(AzdoSource source, CancellationToken cancellationToken = default);
}
