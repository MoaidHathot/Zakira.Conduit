namespace Zakira.Conduit.Manifest;

/// <summary>
///     Parses an Azure DevOps repository URL into its <c>organization</c>,
///     <c>project</c> and <c>repo</c> components. Accepts the common shapes
///     copy-pasted from a browser or git remote:
///     <list type="bullet">
///         <item><description><c>https://dev.azure.com/{org}/{project}/_git/{repo}</c></description></item>
///         <item><description><c>https://{org}.visualstudio.com/{project}/_git/{repo}</c></description></item>
///         <item><description><c>https://{org}@dev.azure.com/{org}/{project}/_git/{repo}</c> (git remote form)</description></item>
///         <item><description><c>git@ssh.dev.azure.com:v3/{org}/{project}/{repo}</c></description></item>
///         <item><description>self-hosted AzDO Server: <c>https://{host}/{collection}/{project}/_git/{repo}</c> (collection treated as organization, baseUrl derived from <c>https://{host}/</c>)</description></item>
///     </list>
/// </summary>
public static class AzdoUrlParser
{
    /// <summary>
    ///     Parses <paramref name="value"/> into its components. Throws
    ///     <see cref="FormatException"/> when the input cannot be recognised.
    /// </summary>
    public static AzdoUrlComponents Parse(string value)
    {
        if (TryParse(value, out var components, out var error))
        {
            return components!;
        }

        throw new FormatException(error);
    }

    /// <summary>
    ///     <see langword="true"/> when <paramref name="value"/> parsed cleanly.
    /// </summary>
    public static bool TryParse(string? value, out AzdoUrlComponents? components, out string error)
    {
        components = null;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(value))
        {
            error = "Azure DevOps URL is empty.";
            return false;
        }

        var s = value.Trim();

        // SSH form: git@ssh.dev.azure.com:v3/{org}/{project}/{repo}
        const string sshPrefix = "git@ssh.dev.azure.com:v3/";
        if (s.StartsWith(sshPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var rest = s[sshPrefix.Length..].TrimEnd('/');
            if (rest.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            {
                rest = rest[..^4];
            }

            var parts = rest.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3)
            {
                error = $"'{value}' is not a recognized Azure DevOps SSH URL (expected git@ssh.dev.azure.com:v3/org/project/repo).";
                return false;
            }

            components = new AzdoUrlComponents(parts[0], parts[1], parts[2], BaseUrl: new Uri("https://dev.azure.com/"));
            return true;
        }

        if (!Uri.TryCreate(s, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            error = $"'{value}' is not a recognized Azure DevOps URL.";
            return false;
        }

        var host = uri.Host;
        var segments = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(seg => Uri.UnescapeDataString(seg))
            .ToArray();

        if (segments.Length == 0)
        {
            error = $"'{value}' is missing a project / repository path.";
            return false;
        }

        // Strip trailing ".git" on the last segment (git remote form).
        if (segments[^1].EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            segments[^1] = segments[^1][..^4];
        }

        // dev.azure.com/{org}/{project}/_git/{repo}
        if (host.Equals("dev.azure.com", StringComparison.OrdinalIgnoreCase))
        {
            if (segments.Length < 4 || !segments[^2].Equals("_git", StringComparison.OrdinalIgnoreCase))
            {
                error = $"'{value}' is not a recognized dev.azure.com repository URL (expected https://dev.azure.com/org/project/_git/repo).";
                return false;
            }

            var org = segments[0];
            var repo = segments[^1];
            // project is everything between org and "_git" (allows project names with slashes? AzDO doesn't, but be safe).
            var project = string.Join('/', segments, 1, segments.Length - 3);
            components = new AzdoUrlComponents(org, project, repo, BaseUrl: new Uri("https://dev.azure.com/"));
            return true;
        }

        // {org}.visualstudio.com/{project}/_git/{repo}
        if (host.EndsWith(".visualstudio.com", StringComparison.OrdinalIgnoreCase))
        {
            var org = host[..^".visualstudio.com".Length];
            if (segments.Length < 3 || !segments[^2].Equals("_git", StringComparison.OrdinalIgnoreCase))
            {
                error = $"'{value}' is not a recognized visualstudio.com repository URL (expected https://org.visualstudio.com/project/_git/repo).";
                return false;
            }

            var repo = segments[^1];
            var project = string.Join('/', segments, 0, segments.Length - 2);
            components = new AzdoUrlComponents(org, project, repo, BaseUrl: new Uri($"https://{host}/"));
            return true;
        }

        // Self-hosted Azure DevOps Server: {host}/{collection}/{project}/_git/{repo}
        // We map "collection" -> organization and the host -> baseUrl.
        if (segments.Length >= 4 && segments[^2].Equals("_git", StringComparison.OrdinalIgnoreCase))
        {
            var org = segments[0];
            var repo = segments[^1];
            var project = string.Join('/', segments, 1, segments.Length - 3);
            components = new AzdoUrlComponents(org, project, repo, BaseUrl: new Uri($"{uri.Scheme}://{uri.Authority}/"));
            return true;
        }

        error = $"'{value}' is not a recognized Azure DevOps repository URL (expected '.../org/project/_git/repo').";
        return false;
    }

    /// <summary>
    ///     Like <see cref="TryParse(string?, out AzdoUrlComponents?, out string)"/> but
    ///     additionally extracts:
    ///     <list type="bullet">
    ///         <item><description><paramref name="subPath"/>: path segments after <c>_git/&lt;repo&gt;</c>, or the <c>path=</c> query parameter.</description></item>
    ///         <item><description><paramref name="branch"/>: from a <c>version=GB&lt;branch&gt;</c> query parameter (AzDO browse URLs use this).</description></item>
    ///         <item><description><paramref name="tag"/>: from <c>version=GT&lt;tag&gt;</c>.</description></item>
    ///         <item><description><paramref name="commit"/>: from <c>version=GC&lt;sha&gt;</c>.</description></item>
    ///     </list>
    /// </summary>
    public static bool TryParseExtended(
        string? value,
        out AzdoUrlComponents? components,
        out string? subPath,
        out string? branch,
        out string? tag,
        out string? commit,
        out string error)
    {
        components = null;
        subPath = null;
        branch = null;
        tag = null;
        commit = null;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(value))
        {
            error = "Azure DevOps URL is empty.";
            return false;
        }

        var s = value.Trim();

        // Fast path: TryParse already covers the "no extra path / no query" case.
        if (TryParse(s, out components, out _))
        {
            ApplyQueryParameters(s, ref subPath, ref branch, ref tag, ref commit);
            return true;
        }

        // Slow path: extra path segments after _git/<repo>. Walk the URL, find
        // the _git marker, take the next segment as the repo, and treat the rest
        // as the sub-path.
        if (!Uri.TryCreate(s, UriKind.Absolute, out var uri))
        {
            error = $"'{value}' is not a recognized Azure DevOps URL.";
            return false;
        }

        var rawSegments = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.UnescapeDataString)
            .ToList();

        var gitIndex = rawSegments.FindIndex(seg => seg.Equals("_git", StringComparison.OrdinalIgnoreCase));
        if (gitIndex < 0 || gitIndex == rawSegments.Count - 1)
        {
            error = $"'{value}' is not a recognized Azure DevOps repository URL (expected '.../_git/<repo>[/sub/path]').";
            return false;
        }

        // Repo is the segment immediately after _git; anything further is a sub-path.
        var trimmed = rawSegments.Take(gitIndex + 2).ToList();
        if (trimmed[^1].EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            trimmed[^1] = trimmed[^1][..^4];
        }

        if (rawSegments.Count > gitIndex + 2)
        {
            subPath = string.Join('/', rawSegments.Skip(gitIndex + 2));
        }

        // Rebuild a "clean" URL with no extra path, no query, no .git, and re-run TryParse.
        var clean = $"{uri.Scheme}://{uri.Authority}/{string.Join('/', trimmed)}";
        if (!TryParse(clean, out components, out var innerError))
        {
            error = innerError;
            return false;
        }

        ApplyQueryParameters(s, ref subPath, ref branch, ref tag, ref commit);
        return true;
    }

    private static void ApplyQueryParameters(string url, ref string? subPath, ref string? branch, ref string? tag, ref string? commit)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Query))
        {
            return;
        }

        // Manually parse the query (System.Web isn't available cleanly cross-platform in older targets;
        // for .NET 10 it would be, but staying lightweight).
        var query = uri.Query.TrimStart('?');
        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=', StringComparison.Ordinal);
            if (eq < 0) continue;
            var key = Uri.UnescapeDataString(part[..eq]);
            var val = Uri.UnescapeDataString(part[(eq + 1)..]);
            if (string.IsNullOrEmpty(val)) continue;

            if (key.Equals("path", StringComparison.OrdinalIgnoreCase))
            {
                subPath ??= val.TrimStart('/');
            }
            else if (key.Equals("version", StringComparison.OrdinalIgnoreCase) && val.Length >= 3)
            {
                // AzDO version descriptor prefixes: GB=branch, GT=tag, GC=commit.
                var prefix = val[..2];
                var rest = val[2..];
                if (prefix.Equals("GB", StringComparison.OrdinalIgnoreCase)) branch ??= rest;
                else if (prefix.Equals("GT", StringComparison.OrdinalIgnoreCase)) tag ??= rest;
                else if (prefix.Equals("GC", StringComparison.OrdinalIgnoreCase)) commit ??= rest;
            }
        }
    }
}

/// <summary>
///     The parsed pieces of an Azure DevOps repository URL.
/// </summary>
/// <param name="Organization">The org (cloud) or collection (Server) name.</param>
/// <param name="Project">The project name.</param>
/// <param name="Repo">The repository name.</param>
/// <param name="BaseUrl">
///     The base URL to use for REST calls. For dev.azure.com this is
///     <c>https://dev.azure.com/</c>. For visualstudio.com or self-hosted
///     servers it is the scheme + host of the parsed URL.
/// </param>
public sealed record AzdoUrlComponents(string Organization, string Project, string Repo, Uri BaseUrl);
