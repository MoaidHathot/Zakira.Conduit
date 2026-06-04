using System.Collections.Concurrent;
using System.Text.Json.Serialization;

namespace Zakira.Conduit.Manifest;

/// <summary>
///     A skill source hosted on GitHub. The repository is downloaded as a
///     snapshot archive (zipball) for the configured ref (commit or branch)
///     and, if <see cref="Paths"/> is provided, only those sub-trees are
///     mirrored to the entry's targets.
/// </summary>
public sealed record GitHubSource : ISource
{
    /// <summary>
    ///     The JSON discriminator value for this source kind (<c>"github"</c>).
    /// </summary>
    public const string TypeDiscriminator = "github";

    // Process-wide cache of parsed repo references. Static so it stays out of
    // record equality (which would otherwise compare instance fields too).
    // Unique-string cardinality is naturally bounded by the number of repos a
    // user references; no eviction needed.
    private static readonly ConcurrentDictionary<string, (string Owner, string Name)> RepoCache = new(StringComparer.Ordinal);

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
    ///     Optional list of sub-paths inside the repository to mirror. Each
    ///     element may be a string (the common case) or an object with
    ///     <c>path</c> + optional <c>as</c> alias.
    ///     When <see langword="null"/> / empty (and <see cref="Path"/> is
    ///     also null) the entire repository is mirrored.
    ///     <para>
    ///         <list type="bullet">
    ///             <item><description>If exactly one path resolves, it is mirrored to <c>&lt;target&gt;/&lt;entry.name&gt;/</c>.</description></item>
    ///             <item><description>If two or more paths resolve, each is mirrored to <c>&lt;target&gt;/&lt;basename(path)&gt;/</c> (or its alias when supplied); the entry name drops out of the destination.</description></item>
    ///         </list>
    ///     </para>
    /// </summary>
    [JsonPropertyName("paths")]
    public IReadOnlyList<PathSpec>? Paths { get; init; }

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

    /// <summary>
    ///     Optional list of file-relative glob patterns (post-fetch filter)
    ///     describing what is mirrored from the fetched archive. Patterns use
    ///     <see cref="Microsoft.Extensions.FileSystemGlobbing"/> syntax
    ///     (<c>*</c>, <c>**</c>, <c>?</c>, bracket classes). Empty / null
    ///     means "include everything".
    /// </summary>
    [JsonPropertyName("include")]
    public IReadOnlyList<string>? Include { get; init; }

    /// <summary>
    ///     Optional list of file-relative glob patterns to exclude from the
    ///     mirrored output. Applied after <see cref="Include"/>.
    /// </summary>
    [JsonPropertyName("exclude")]
    public IReadOnlyList<string>? Exclude { get; init; }

    /// <summary>
    ///     Authentication chain. Either a single string (one mode) or an array
    ///     of mode names tried in order. Supported modes: <c>env</c>
    ///     (CONDUIT_GITHUB_TOKEN -&gt; GITHUB_TOKEN -&gt; GH_TOKEN), <c>gh</c>
    ///     (the <c>gh auth token</c> CLI), <c>pat</c> (a specific env var
    ///     named by <see cref="PatEnv"/>), and <c>anonymous</c>. When unset
    ///     the default chain is <c>[env, gh, anonymous]</c>.
    /// </summary>
    [JsonPropertyName("auth")]
    [JsonConverter(typeof(AzdoAuthChainJsonConverter))]
    public IReadOnlyList<string>? Auth { get; init; }

    /// <summary>
    ///     For <c>pat</c> auth mode: the environment variable name to read
    ///     the PAT from. Defaults to <c>CONDUIT_GITHUB_TOKEN</c>.
    /// </summary>
    [JsonPropertyName("patEnv")]
    public string? PatEnv { get; init; }

    /// <inheritdoc />
    [JsonIgnore]
    public string Kind => TypeDiscriminator;

    /// <summary>The parsed owner / organisation portion of <see cref="Repo"/>.</summary>
    [JsonIgnore]
    public string Owner => ParsedRepo.Owner;

    /// <summary>The parsed repository name portion of <see cref="Repo"/>.</summary>
    [JsonIgnore]
    public string RepoName => ParsedRepo.Name;

    /// <summary>The canonical <c>owner/name</c> slug, useful for logging.</summary>
    [JsonIgnore]
    public string Slug
    {
        get
        {
            var (o, n) = ParsedRepo;
            return $"{o}/{n}";
        }
    }

    /// <summary>
    ///     Parses <see cref="Repo"/> once and caches the result process-wide,
    ///     so the three convenience properties above are cheap to read repeatedly.
    /// </summary>
    private (string Owner, string Name) ParsedRepo => RepoCache.GetOrAdd(Repo, GitHubRepoReference.Parse);

    /// <summary>
    ///     Effective sub-paths to mirror. Returns <see cref="Paths"/> when set,
    ///     otherwise a one-element list containing <see cref="Path"/>, otherwise
    ///     an empty list (meaning the whole repository).
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<PathSpec> EffectivePaths =>
        Paths is { Count: > 0 } p
            ? p
            : (string.IsNullOrWhiteSpace(Path) ? Array.Empty<PathSpec>() : new PathSpec[] { new(Path) });

    /// <summary>
    ///     Resolves the git ref (commit SHA, tag, or branch) that should be
    ///     fetched, or <see langword="null"/> to fall back to the default branch.
    /// </summary>
    [JsonIgnore]
    public string? ResolvedRef => string.IsNullOrWhiteSpace(Commit) ? (string.IsNullOrWhiteSpace(Branch) ? null : Branch) : Commit;

    /// <summary>
    ///     The resolved auth chain. Defaults to <c>[env, gh, anonymous]</c>
    ///     when <see cref="Auth"/> is unset.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<string> ResolvedAuthChain =>
        Auth is { Count: > 0 } a ? a : new[] { "env", "gh", "anonymous" };
}
