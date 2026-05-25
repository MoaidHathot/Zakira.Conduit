namespace Zakira.Conduit.Manifest;

/// <summary>
///     Parses the user-supplied <c>repo</c> field of a GitHub source into
///     an <c>owner</c> / <c>name</c> pair. Accepts:
///     <list type="bullet">
///         <item><description><c>owner/repo</c> (slug)</description></item>
///         <item><description><c>github.com/owner/repo</c></description></item>
///         <item><description><c>https://github.com/owner/repo[.git][/]</c></description></item>
///         <item><description><c>http://github.com/owner/repo[.git][/]</c></description></item>
///         <item><description><c>git@github.com:owner/repo[.git]</c> (SSH)</description></item>
///     </list>
/// </summary>
public static class GitHubRepoReference
{
    /// <summary>
    ///     Parses <paramref name="value"/>. Throws <see cref="FormatException"/>
    ///     when the input cannot be recognised as a GitHub repository reference.
    /// </summary>
    public static (string Owner, string Name) Parse(string value)
    {
        if (TryParse(value, out var owner, out var name, out var error))
        {
            return (owner, name);
        }

        throw new FormatException(error);
    }

    /// <summary>
    ///     <see langword="true"/> when <paramref name="value"/> parsed cleanly.
    /// </summary>
    public static bool TryParse(string? value, out string owner, out string name, out string error) =>
        TryParseExtended(value, out owner, out name, out _, out _, out error, allowExtraPath: false);

    /// <summary>
    ///     Like <see cref="TryParse(string?, out string, out string, out string)"/> but
    ///     additionally accepts and surfaces extra path segments after the
    ///     repository name, splitting them into <paramref name="subPath"/>
    ///     and (optionally) <paramref name="branch"/>:
    ///     <list type="bullet">
    ///         <item><description><c>owner/repo/some/sub/path</c>            -> subPath=<c>some/sub/path</c></description></item>
    ///         <item><description><c>owner/repo/tree/main/some/path</c>      -> branch=<c>main</c>, subPath=<c>some/path</c></description></item>
    ///         <item><description><c>owner/repo/blob/main/file.md</c>        -> branch=<c>main</c>, subPath=<c>file.md</c></description></item>
    ///     </list>
    ///     <para>
    ///         When <paramref name="allowExtraPath"/> is <see langword="false"/>
    ///         (the default of <see cref="TryParse(string?, out string, out string, out string)"/>)
    ///         the legacy strict behaviour applies and any extra segment is an error.
    ///     </para>
    /// </summary>
    public static bool TryParseExtended(
        string? value,
        out string owner,
        out string name,
        out string? subPath,
        out string? branch,
        out string error,
        bool allowExtraPath = true)
    {
        owner = string.Empty;
        name = string.Empty;
        subPath = null;
        branch = null;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(value))
        {
            error = "GitHub repository reference is empty.";
            return false;
        }

        var s = value.Trim();

        // Strip well-known prefixes.
        s = StripPrefix(s, "https://github.com/");
        s = StripPrefix(s, "http://github.com/");
        s = StripPrefix(s, "git@github.com:");
        s = StripPrefix(s, "github.com/");
        s = StripPrefix(s, "ssh://git@github.com/");

        // Strip trailing ".git" and "/".
        if (s.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            s = s[..^4];
        }

        s = s.TrimEnd('/');

        var slash = s.IndexOf('/', StringComparison.Ordinal);
        if (slash <= 0 || slash == s.Length - 1)
        {
            error = $"'{value}' is not a recognized GitHub repository. Expected 'owner/repo' or a github.com URL.";
            return false;
        }

        var parsedOwner = s[..slash];
        var rest = s[(slash + 1)..];

        // Split repo from any extra path.
        string parsedName;
        string? remainder;
        var nameSlash = rest.IndexOf('/', StringComparison.Ordinal);
        if (nameSlash < 0)
        {
            parsedName = rest;
            remainder = null;
        }
        else
        {
            parsedName = rest[..nameSlash];
            remainder = rest[(nameSlash + 1)..];
        }

        // Strip a trailing ".git" on the repo name itself (e.g. "owner/repo.git/sub").
        if (parsedName.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            parsedName = parsedName[..^4];
        }

        if (!IsValidNamePart(parsedOwner) || !IsValidNamePart(parsedName))
        {
            error = $"'{value}' contains characters that are not valid in a GitHub owner or repository name.";
            return false;
        }

        if (!string.IsNullOrEmpty(remainder))
        {
            if (!allowExtraPath)
            {
                error = $"'{value}' contains extra path segments after the repository name. Use the 'path' or 'paths' field for sub-trees and 'branch' / 'commit' for refs.";
                return false;
            }

            // Recognise GitHub's browse-URL shapes:
            //   tree/<ref>/<path...>
            //   blob/<ref>/<path...>  (single file)
            //   raw/<ref>/<path...>
            var segments = remainder.Split('/');
            if (segments.Length >= 2 && segments[0] is "tree" or "blob" or "raw")
            {
                branch = segments[1];
                subPath = segments.Length > 2 ? string.Join('/', segments, 2, segments.Length - 2) : null;
            }
            else
            {
                subPath = remainder;
            }

            if (subPath is { Length: 0 })
            {
                subPath = null;
            }
        }

        owner = parsedOwner;
        name = parsedName;
        return true;
    }

    private static string StripPrefix(string value, string prefix) =>
        value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? value[prefix.Length..] : value;

    private static bool IsValidNamePart(string s)
    {
        if (s.Length == 0)
        {
            return false;
        }

        foreach (var c in s)
        {
            if (!char.IsLetterOrDigit(c) && c is not ('-' or '_' or '.'))
            {
                return false;
            }
        }

        return true;
    }
}
