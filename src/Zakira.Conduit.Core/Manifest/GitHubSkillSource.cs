using System.Text.Json.Serialization;

namespace Zakira.Conduit.Manifest;

/// <summary>
///     A skill source hosted on GitHub. The repository is downloaded as a
///     snapshot archive (zipball) for the configured ref (commit or branch)
///     and, if <see cref="Paths"/> is provided, only those sub-trees are
///     mirrored to the entry's targets.
/// </summary>
public sealed record GitHubSkillSource : ISkillSource
{
    /// <summary>
    ///     The JSON discriminator value for this source kind (<c>"github"</c>).
    /// </summary>
    public const string TypeDiscriminator = "github";

    /// <summary>
    ///     The repository identifier. Accepts a slug (<c>owner/repo</c>),
    ///     a github.com URL, or an SSH-style reference. Parsed lazily into
    ///     <see cref="Owner"/> and <see cref="RepoName"/>. Required.
    /// </summary>
    [JsonPropertyName("repo")]
    public required string Repo { get; init; }

    /// <summary>
    ///     Optional single sub-path inside the repository. Syntactic sugar for
    ///     a one-element <see cref="Paths"/>. Mutually exclusive with
    ///     <see cref="Paths"/>.
    /// </summary>
    [JsonPropertyName("path")]
    public string? Path { get; init; }

    /// <summary>
    ///     Optional list of sub-paths inside the repository to mirror.
    ///     When <see langword="null"/> / empty (and <see cref="Path"/> is
    ///     also null) the entire repository is mirrored.
    ///     <para>
    ///         <list type="bullet">
    ///             <item><description>If exactly one path resolves, it is mirrored to <c>&lt;target&gt;/&lt;entry.name&gt;/</c>.</description></item>
    ///             <item><description>If two or more paths resolve, each is mirrored to <c>&lt;target&gt;/&lt;basename(path)&gt;/</c>; the entry name drops out of the destination.</description></item>
    ///         </list>
    ///     </para>
    /// </summary>
    [JsonPropertyName("paths")]
    public IReadOnlyList<string>? Paths { get; init; }

    /// <summary>
    ///     Optional commit SHA to pin to. Mutually exclusive with
    ///     <see cref="Branch"/>. Takes precedence when both are set.
    /// </summary>
    [JsonPropertyName("commit")]
    public string? Commit { get; init; }

    /// <summary>
    ///     Optional branch (or tag) name. Mutually exclusive with
    ///     <see cref="Commit"/>. When neither is set, the repository's
    ///     default branch is used.
    /// </summary>
    [JsonPropertyName("branch")]
    public string? Branch { get; init; }

    /// <inheritdoc />
    [JsonIgnore]
    public string Kind => TypeDiscriminator;

    /// <summary>The parsed owner / organisation portion of <see cref="Repo"/>.</summary>
    [JsonIgnore]
    public string Owner => GitHubRepoReference.Parse(Repo).Owner;

    /// <summary>The parsed repository name portion of <see cref="Repo"/>.</summary>
    [JsonIgnore]
    public string RepoName => GitHubRepoReference.Parse(Repo).Name;

    /// <summary>The canonical <c>owner/name</c> slug, useful for logging.</summary>
    [JsonIgnore]
    public string Slug
    {
        get
        {
            var (o, n) = GitHubRepoReference.Parse(Repo);
            return $"{o}/{n}";
        }
    }

    /// <summary>
    ///     Effective sub-paths to mirror. Returns <see cref="Paths"/> when set,
    ///     otherwise a one-element list containing <see cref="Path"/>, otherwise
    ///     an empty list (meaning the whole repository).
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<string> EffectivePaths =>
        Paths is { Count: > 0 } p
            ? p
            : (string.IsNullOrWhiteSpace(Path) ? Array.Empty<string>() : new[] { Path });

    /// <summary>
    ///     Resolves the git ref (commit SHA, tag, or branch) that should be
    ///     fetched, or <see langword="null"/> to fall back to the default branch.
    /// </summary>
    [JsonIgnore]
    public string? ResolvedRef => string.IsNullOrWhiteSpace(Commit) ? (string.IsNullOrWhiteSpace(Branch) ? null : Branch) : Commit;
}
