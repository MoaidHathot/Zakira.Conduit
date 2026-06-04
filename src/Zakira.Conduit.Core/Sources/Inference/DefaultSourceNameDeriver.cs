using Zakira.Conduit.Manifest;

namespace Zakira.Conduit.Sources.Inference;

/// <summary>
///     Derives a default destination name from a concrete <see cref="ISource"/>
///     when the manifest author did not supply <c>entry.name</c> and did not
///     supply an explicit alias (in-string <c> -&gt; Name</c> or
///     <c>{ "source": ..., "as": "Name" }</c> wrapper).
/// </summary>
/// <remarks>
///     The rules are deliberately tied to the source's own identity (the
///     repository or directory the user pointed at), not to a sub-path inside
///     it: the sub-path tells Conduit <i>what</i> to copy, while the source
///     identity gives the natural folder name to copy it into. For example,
///     <c>github.com/foo/bar/tree/main/skills</c> yields <c>bar</c>, not
///     <c>skills</c>.
/// </remarks>
public static class DefaultSourceNameDeriver
{
    /// <summary>
    ///     Returns the default destination name for <paramref name="source"/>,
    ///     or <see langword="null"/> when no sensible default exists (in which
    ///     case the validator surfaces a friendly error telling the author to
    ///     supply <c>name</c> or an explicit alias).
    /// </summary>
    public static string? Derive(ISource source) => source switch
    {
        GitHubSource gh => NullIfBlank(gh.RepoName),
        AzdoSource azdo => NullIfBlank(azdo.ResolvedComponents.Repo),
        LocalDirectorySource local => DeriveLocal(local),
        AliasedSource aliased => aliased.As,
        _ => null,
    };

    private static string? DeriveLocal(LocalDirectorySource local)
    {
        var paths = local.EffectivePaths;
        if (paths.Count == 0)
        {
            return null;
        }

        // Single-path: the dir basename is the natural identity.
        // Multi-path: no single dir represents the source, so we fall back to
        // null. The user will get a validator error telling them to name it.
        if (paths.Count == 1)
        {
            return NullIfBlank(BasenameOf(paths[0].Path));
        }

        return null;
    }

    private static string BasenameOf(string path)
    {
        var normalized = path.Replace('\\', '/').TrimEnd('/');
        var slash = normalized.LastIndexOf('/');
        return slash < 0 ? normalized : normalized[(slash + 1)..];
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
