using System.Text.Json.Serialization;

namespace Zakira.Conduit.Manifest;

/// <summary>
///     A source declared by a single <c>uri</c> field whose concrete kind is
///     inferred at load time (one of <see cref="GitHubSkillSource"/>,
///     <see cref="AzdoSkillSource"/>, <see cref="LocalDirectorySkillSource"/>,
///     etc.) by an <see cref="Sources.Inference.ISkillSourceInferrer"/>.
///     <para>
///         All the per-kind optional fields a user might want to set live here
///         as nullable properties; the inferrer copies the relevant ones into
///         the concrete record. Fields irrelevant to the inferred kind are
///         rejected by validation.
///     </para>
/// </summary>
/// <remarks>
///     This record never survives past manifest load: the
///     <c>SkillSourceInferenceCoordinator</c> rewrites every entry's source
///     into its concrete kind before the manifest reaches the validator,
///     synchronizer, pin/update, etc. Downstream code never sees this type.
/// </remarks>
public sealed record UriBasedSkillSource : ISkillSource
{
    /// <summary>The JSON discriminator value (<c>"uri"</c>).</summary>
    public const string TypeDiscriminator = "uri";

    /// <summary>
    ///     The source URI. A full URL (<c>https://...</c>, <c>git@host:...</c>)
    ///     or a local-path-shaped value (<c>./foo</c>, <c>~/foo</c>,
    ///     <c>$VAR/foo</c>, an absolute path, etc.). Required.
    /// </summary>
    [JsonPropertyName("uri")]
    public required string Uri { get; init; }

    /// <summary>Optional single sub-path inside the source.</summary>
    [JsonPropertyName("path")]
    public string? Path { get; init; }

    /// <summary>Optional list of sub-paths inside the source.</summary>
    [JsonPropertyName("paths")]
    public IReadOnlyList<PathSpec>? Paths { get; init; }

    /// <summary>Optional branch name (tracking intent for git-shaped sources).</summary>
    [JsonPropertyName("branch")]
    public string? Branch { get; init; }

    /// <summary>Optional tag name (for sources that support tags).</summary>
    [JsonPropertyName("tag")]
    public string? Tag { get; init; }

    /// <summary>Optional commit SHA pin.</summary>
    [JsonPropertyName("commit")]
    public string? Commit { get; init; }

    /// <summary>Optional REST base-URL override (used by AzDO Server, future Gitea/Gitlab self-hosted, etc.).</summary>
    [JsonPropertyName("baseUrl")]
    public string? BaseUrl { get; init; }

    /// <summary>Optional auth chain (currently consumed by the azdo inferrer).</summary>
    [JsonPropertyName("auth")]
    [JsonConverter(typeof(AzdoAuthChainJsonConverter))]
    public IReadOnlyList<string>? Auth { get; init; }

    /// <summary>Optional explicit-PAT env-var name (azdo).</summary>
    [JsonPropertyName("patEnv")]
    public string? PatEnv { get; init; }

    /// <inheritdoc />
    [JsonIgnore]
    public string Kind => TypeDiscriminator;
}
