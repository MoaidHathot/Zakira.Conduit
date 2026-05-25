using Zakira.Conduit.Manifest;

namespace Zakira.Conduit.Sources.Inference;

/// <summary>
///     Recognises any URI accepted by <see cref="GitHubRepoReference"/>
///     <em>except</em> bare slugs. The slug form (<c>owner/repo</c>) is
///     intentionally rejected here, since slug-only values are ambiguous
///     in a fully URI-driven manifest; users who want the slug ergonomics
///     should keep using <c>"type": "github"</c> + <c>"repo"</c>.
/// </summary>
public sealed class GitHubSkillSourceInferrer : ISkillSourceInferrer
{
    /// <inheritdoc />
    public string Kind => GitHubSkillSource.TypeDiscriminator;

    /// <inheritdoc />
    public bool CanHandle(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri)) return false;
        if (LocalDirectorySkillSourceInferrer.LooksLikeLocalPath(uri)) return false;

        var v = uri.Trim();
        return v.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase)
            || v.StartsWith("http://github.com/", StringComparison.OrdinalIgnoreCase)
            || v.StartsWith("git@github.com:", StringComparison.OrdinalIgnoreCase)
            || v.StartsWith("ssh://git@github.com/", StringComparison.OrdinalIgnoreCase)
            || v.StartsWith("github.com/", StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public ISkillSource Infer(UriBasedSkillSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        RejectAzdoOnlyFields(source);

        if (!GitHubRepoReference.TryParseExtended(source.Uri, out var owner, out var name, out var urlSubPath, out var urlBranch, out var error))
        {
            throw new SkillSourceInferenceException($"uri '{source.Uri}' could not be parsed as a GitHub repository: {error}");
        }

        // Merge URL-derived sub-path / branch with explicit overrides on the
        // UriBasedSkillSource. Explicit overrides win when set; otherwise the
        // URL-derived values are used. Setting both an explicit 'path' and
        // having a URL sub-path is an error (the user is contradicting themself).
        var hasExplicitPath = !string.IsNullOrWhiteSpace(source.Path) || source.Paths is { Count: > 0 };
        if (urlSubPath is not null && hasExplicitPath)
        {
            throw new SkillSourceInferenceException(
                $"uri '{source.Uri}' carries a sub-path ('{urlSubPath}') but the source also sets an explicit 'path' or 'paths'. Use one or the other.");
        }

        var hasExplicitBranch = !string.IsNullOrWhiteSpace(source.Branch);
        if (urlBranch is not null && hasExplicitBranch && !string.Equals(urlBranch, source.Branch, StringComparison.Ordinal))
        {
            throw new SkillSourceInferenceException(
                $"uri '{source.Uri}' carries branch '{urlBranch}' but the source also sets branch='{source.Branch}'. Use one or the other.");
        }

        return new GitHubSkillSource
        {
            // Use the canonical owner/repo slug so downstream URL parsing
            // (ResolvedComponents) doesn't see the extra path segments.
            Repo = $"{owner}/{name}",
            Path = source.Path ?? urlSubPath,
            Paths = source.Paths,
            Branch = source.Branch ?? urlBranch,
            Commit = source.Commit,
        };
    }

    private static void RejectAzdoOnlyFields(UriBasedSkillSource source)
    {
        var bad = new List<string>(3);
        if (!string.IsNullOrWhiteSpace(source.Tag)) bad.Add("tag");
        if (!string.IsNullOrWhiteSpace(source.BaseUrl)) bad.Add("baseUrl");
        if (source.Auth is { Count: > 0 }) bad.Add("auth");
        if (!string.IsNullOrWhiteSpace(source.PatEnv)) bad.Add("patEnv");

        if (bad.Count > 0)
        {
            throw new SkillSourceInferenceException(
                $"Field(s) [{string.Join(", ", bad)}] do not apply to an inferred github source.");
        }
    }
}
