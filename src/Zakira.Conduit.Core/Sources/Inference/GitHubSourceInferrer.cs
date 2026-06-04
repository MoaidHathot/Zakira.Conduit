using Zakira.Conduit.Manifest;

namespace Zakira.Conduit.Sources.Inference;

/// <summary>
///     Recognises any URI accepted by <see cref="GitHubRepoReference"/>
///     <em>except</em> bare slugs. The slug form (<c>owner/repo</c>) is
///     intentionally rejected here, since slug-only values are ambiguous
///     in a fully URI-driven manifest; users who want the slug ergonomics
///     should keep using <c>"type": "github"</c> + <c>"repo"</c>.
/// </summary>
public sealed class GitHubSourceInferrer : ISourceInferrer
{
    /// <inheritdoc />
    public string Kind => GitHubSource.TypeDiscriminator;

    /// <inheritdoc />
    public bool CanHandle(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri)) return false;
        if (LocalDirectorySourceInferrer.LooksLikeLocalPath(uri)) return false;

        var v = uri.Trim();
        return v.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase)
            || v.StartsWith("http://github.com/", StringComparison.OrdinalIgnoreCase)
            || v.StartsWith("git@github.com:", StringComparison.OrdinalIgnoreCase)
            || v.StartsWith("ssh://git@github.com/", StringComparison.OrdinalIgnoreCase)
            || v.StartsWith("github.com/", StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public ISource Infer(UriBasedSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        RejectAzdoOnlyFields(source);

        if (!GitHubRepoReference.TryParseExtended(source.Uri, out var owner, out var name, out var urlSubPath, out var urlBranch, out var urlCommit, out var error))
        {
            throw new SourceInferenceException($"uri '{source.Uri}' could not be parsed as a GitHub repository: {error}");
        }

        // Merge URL-derived sub-path / branch / commit with explicit overrides
        // on the UriBasedSource. Explicit overrides win when set; otherwise
        // the URL-derived values are used. Setting both an explicit 'path'
        // and having a URL sub-path is an error (the user is contradicting
        // themself). Same goes for branch and commit.
        var hasExplicitPath = !string.IsNullOrWhiteSpace(source.Path) || source.Paths is { Count: > 0 };
        if (urlSubPath is not null && hasExplicitPath)
        {
            throw new SourceInferenceException(
                $"uri '{source.Uri}' carries a sub-path ('{urlSubPath}') but the source also sets an explicit 'path' or 'paths'. Use one or the other.");
        }

        var hasExplicitBranch = !string.IsNullOrWhiteSpace(source.Branch);
        if (urlBranch is not null && hasExplicitBranch && !string.Equals(urlBranch, source.Branch, StringComparison.Ordinal))
        {
            throw new SourceInferenceException(
                $"uri '{source.Uri}' carries branch '{urlBranch}' but the source also sets branch='{source.Branch}'. Use one or the other.");
        }

        var hasExplicitCommit = !string.IsNullOrWhiteSpace(source.Commit);
        if (urlCommit is not null && hasExplicitCommit && !string.Equals(urlCommit, source.Commit, StringComparison.OrdinalIgnoreCase))
        {
            throw new SourceInferenceException(
                $"uri '{source.Uri}' pins commit '{urlCommit}' but the source also sets commit='{source.Commit}'. Use one or the other.");
        }

        return new GitHubSource
        {
            // Use the canonical owner/repo slug so downstream URL parsing
            // (ResolvedComponents) doesn't see the extra path segments.
            Repo = $"{owner}/{name}",
            Path = source.Path ?? urlSubPath,
            Paths = source.Paths,
            Branch = source.Branch ?? urlBranch,
            Commit = source.Commit ?? urlCommit,
            Include = source.Include,
            Exclude = source.Exclude,
            Auth = source.Auth,
            PatEnv = source.PatEnv,
        };
    }

    private static void RejectAzdoOnlyFields(UriBasedSource source)
    {
        var bad = new List<string>(2);
        if (!string.IsNullOrWhiteSpace(source.Tag)) bad.Add("tag");
        if (!string.IsNullOrWhiteSpace(source.BaseUrl)) bad.Add("baseUrl");

        if (bad.Count > 0)
        {
            throw new SourceInferenceException(
                $"Field(s) [{string.Join(", ", bad)}] do not apply to an inferred github source.");
        }
    }
}
