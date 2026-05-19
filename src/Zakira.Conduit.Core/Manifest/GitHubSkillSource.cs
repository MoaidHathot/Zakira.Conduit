using System.Text.Json.Serialization;

namespace Zakira.Conduit.Manifest;

/// <summary>
///     A skill source hosted on GitHub. The repository is downloaded as a
///     snapshot archive (zipball) for the configured ref (commit or branch)
///     and, if <see cref="Path"/> is provided, only that sub-tree is mirrored
///     to the entry's targets.
/// </summary>
public sealed record GitHubSkillSource : ISkillSource
{
    /// <summary>
    ///     The JSON discriminator value for this source kind (<c>"github"</c>).
    /// </summary>
    public const string TypeDiscriminator = "github";

    /// <summary>
    ///     The GitHub repository owner (user or organization). Required.
    /// </summary>
    [JsonPropertyName("owner")]
    public required string Owner { get; init; }

    /// <summary>
    ///     The GitHub repository name. Required.
    /// </summary>
    [JsonPropertyName("repo")]
    public required string Repo { get; init; }

    /// <summary>
    ///     Optional path inside the repository to mirror. When omitted, the
    ///     entire repository content is mirrored. Forward slashes are expected.
    /// </summary>
    [JsonPropertyName("path")]
    public string? Path { get; init; }

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

    /// <summary>
    ///     Resolves the git ref (commit SHA, tag, or branch) that should be
    ///     fetched, or <see langword="null"/> to fall back to the default branch.
    /// </summary>
    [JsonIgnore]
    public string? ResolvedRef => string.IsNullOrWhiteSpace(Commit) ? (string.IsNullOrWhiteSpace(Branch) ? null : Branch) : Commit;

    /// <summary>
    ///     A short slug used for logging and temp-directory names.
    /// </summary>
    [JsonIgnore]
    public string Slug => $"{Owner}/{Repo}";
}
