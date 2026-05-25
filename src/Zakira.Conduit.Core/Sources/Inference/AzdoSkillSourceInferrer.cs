using Zakira.Conduit.Manifest;

namespace Zakira.Conduit.Sources.Inference;

/// <summary>
///     Recognises Azure DevOps repository URLs. Anything
///     <see cref="AzdoUrlParser.TryParse"/> accepts is in scope: cloud
///     (<c>dev.azure.com</c>, <c>*.visualstudio.com</c>), SSH form, and
///     self-hosted AzDO Server.
/// </summary>
public sealed class AzdoSkillSourceInferrer : ISkillSourceInferrer
{
    /// <inheritdoc />
    public string Kind => AzdoSkillSource.TypeDiscriminator;

    /// <inheritdoc />
    public bool CanHandle(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri)) return false;
        if (LocalDirectorySkillSourceInferrer.LooksLikeLocalPath(uri)) return false;

        var v = uri.Trim();
        // Cheap shape check first; AzdoUrlParser handles the full grammar.
        if (v.Contains("dev.azure.com", StringComparison.OrdinalIgnoreCase)) return true;
        if (v.Contains(".visualstudio.com", StringComparison.OrdinalIgnoreCase)) return true;
        if (v.StartsWith("git@ssh.dev.azure.com:", StringComparison.OrdinalIgnoreCase)) return true;

        // Self-hosted AzDO Server: require an explicit hint via the URL shape
        // (path contains /_git/). Otherwise we'd accept every random URL.
        if (v.Contains("/_git/", StringComparison.OrdinalIgnoreCase) &&
            (v.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || v.StartsWith("http://", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return false;
    }

    /// <inheritdoc />
    public ISkillSource Infer(UriBasedSkillSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (!AzdoUrlParser.TryParseExtended(source.Uri, out var parsed, out var urlSubPath, out var urlBranch, out var urlTag, out var urlCommit, out var error))
        {
            throw new SkillSourceInferenceException($"uri '{source.Uri}' could not be parsed as an Azure DevOps repository: {error}");
        }

        var hasExplicitPath = !string.IsNullOrWhiteSpace(source.Path) || source.Paths is { Count: > 0 };
        if (urlSubPath is not null && hasExplicitPath)
        {
            throw new SkillSourceInferenceException(
                $"uri '{source.Uri}' carries a sub-path ('{urlSubPath}') but the source also sets an explicit 'path' or 'paths'. Use one or the other.");
        }

        // For ref-bearing query params, explicit overrides win; URL value is the fallback.
        var branch = !string.IsNullOrWhiteSpace(source.Branch) ? source.Branch : urlBranch;
        var tag = !string.IsNullOrWhiteSpace(source.Tag) ? source.Tag : urlTag;
        var commit = !string.IsNullOrWhiteSpace(source.Commit) ? source.Commit : urlCommit;

        // Hand AzdoSkillSource the explicit triplet so its own URL parser
        // doesn't need to round-trip a sub-path-bearing or query-bearing URL.
        var components = parsed!;
        return new AzdoSkillSource
        {
            Organization = components.Organization,
            Project = components.Project,
            Repo = components.Repo,
            BaseUrl = source.BaseUrl ?? components.BaseUrl.AbsoluteUri,
            Path = source.Path ?? urlSubPath,
            Paths = source.Paths,
            Branch = branch,
            Tag = tag,
            Commit = commit,
            Auth = source.Auth,
            PatEnv = source.PatEnv,
        };
    }
}
