using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Zakira.Conduit.Manifest;

/// <summary>
///     A skill source hosted on Azure DevOps (cloud or Server). The repository
///     contents are fetched as a zip stream via the AzDO Git Items REST API.
///     <para>
///         Two equivalent shapes are accepted:
///         <list type="bullet">
///             <item><description>Explicit triplet: <c>organization</c>, <c>project</c>, <c>repo</c> (and optional <c>baseUrl</c> for AzDO Server).</description></item>
///             <item><description>Browser-paste form: <c>url</c> pointing at the repository's browse URL or git remote.</description></item>
///         </list>
///     </para>
/// </summary>
public sealed record AzdoSkillSource : ISkillSource
{
    /// <summary>
    ///     The JSON discriminator value for this source kind (<c>"azdo"</c>).
    /// </summary>
    public const string TypeDiscriminator = "azdo";

    // Process-wide cache of parsed URLs. Static so it stays out of record equality.
    private static readonly ConcurrentDictionary<string, AzdoUrlComponents> UrlCache = new(StringComparer.Ordinal);

    /// <summary>
    ///     Optional browser-paste / git remote URL. Mutually exclusive with
    ///     the explicit <see cref="Organization"/> / <see cref="Project"/> /
    ///     <see cref="Repo"/> triplet.
    /// </summary>
    [JsonPropertyName("url")]
    public string? Url { get; init; }

    /// <summary>
    ///     The Azure DevOps organization (cloud) or collection (Server) name.
    /// </summary>
    [JsonPropertyName("organization")]
    public string? Organization { get; init; }

    /// <summary>The Azure DevOps project name.</summary>
    [JsonPropertyName("project")]
    public string? Project { get; init; }

    /// <summary>The repository name.</summary>
    [JsonPropertyName("repo")]
    public string? Repo { get; init; }

    /// <summary>
    ///     Optional base URL override; required for self-hosted AzDO Server
    ///     when using the explicit triplet form. Defaults to
    ///     <c>https://dev.azure.com/</c> when the URL form is not supplied.
    /// </summary>
    [JsonPropertyName("baseUrl")]
    public string? BaseUrl { get; init; }

    /// <summary>Optional single sub-path inside the repository.</summary>
    [JsonPropertyName("path")]
    public string? Path { get; init; }

    /// <summary>Optional list of sub-paths to mirror.</summary>
    [JsonPropertyName("paths")]
    public IReadOnlyList<PathSpec>? Paths { get; init; }

    /// <summary>Branch name (tracking intent).</summary>
    [JsonPropertyName("branch")]
    public string? Branch { get; init; }

    /// <summary>Tag name.</summary>
    [JsonPropertyName("tag")]
    public string? Tag { get; init; }

    /// <summary>Commit SHA pin (wins over <see cref="Branch"/> / <see cref="Tag"/> for fetching).</summary>
    [JsonPropertyName("commit")]
    public string? Commit { get; init; }

    /// <summary>
    ///     Authentication chain. Either a single string (one mode) or an array
    ///     of mode names (tried in order). Supported modes: <c>env</c>,
    ///     <c>az</c>, <c>pat</c>, <c>anonymous</c>. Default chain when unset:
    ///     <c>[env, az]</c>.
    /// </summary>
    [JsonPropertyName("auth")]
    [JsonConverter(typeof(AzdoAuthChainJsonConverter))]
    public IReadOnlyList<string>? Auth { get; init; }

    /// <summary>
    ///     For <c>pat</c> auth mode: the environment variable name to read the
    ///     PAT from. Defaults to <c>CONDUIT_AZDO_TOKEN</c>.
    /// </summary>
    [JsonPropertyName("patEnv")]
    public string? PatEnv { get; init; }

    /// <inheritdoc />
    [JsonIgnore]
    public string Kind => TypeDiscriminator;

    /// <summary>
    ///     The parsed components. Caches per <see cref="Url"/> so repeated
    ///     property access is cheap. Throws <see cref="FormatException"/> on
    ///     malformed URLs (validation catches it earlier).
    /// </summary>
    [JsonIgnore]
    public AzdoUrlComponents ResolvedComponents
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Url))
            {
                return UrlCache.GetOrAdd(Url, AzdoUrlParser.Parse);
            }

            var baseUri = string.IsNullOrWhiteSpace(BaseUrl)
                ? new Uri("https://dev.azure.com/")
                : new Uri(BaseUrl.EndsWith('/') ? BaseUrl : BaseUrl + "/");

            return new AzdoUrlComponents(
                Organization ?? string.Empty,
                Project ?? string.Empty,
                Repo ?? string.Empty,
                baseUri);
        }
    }

    /// <summary>The effective list of sub-paths (see <see cref="GitHubSkillSource.EffectivePaths"/>).</summary>
    [JsonIgnore]
    public IReadOnlyList<PathSpec> EffectivePaths =>
        Paths is { Count: > 0 } p
            ? p
            : (string.IsNullOrWhiteSpace(Path) ? Array.Empty<PathSpec>() : new PathSpec[] { new(Path) });

    /// <summary>
    ///     The resolved auth chain. Defaults to <c>[env, az]</c> when unset.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<string> ResolvedAuthChain =>
        Auth is { Count: > 0 } a ? a : new[] { "env", "az" };

    /// <summary>
    ///     The git ref to fetch (commit wins over tag wins over branch).
    /// </summary>
    [JsonIgnore]
    public string? ResolvedRef => !string.IsNullOrWhiteSpace(Commit)
        ? Commit
        : !string.IsNullOrWhiteSpace(Tag)
            ? Tag
            : (string.IsNullOrWhiteSpace(Branch) ? null : Branch);

    /// <summary>
    ///     The ref kind (<c>commit</c>, <c>tag</c>, <c>branch</c>) corresponding
    ///     to <see cref="ResolvedRef"/>. <see langword="null"/> when no ref set.
    /// </summary>
    [JsonIgnore]
    public string? ResolvedRefKind => !string.IsNullOrWhiteSpace(Commit)
        ? "commit"
        : !string.IsNullOrWhiteSpace(Tag)
            ? "tag"
            : (string.IsNullOrWhiteSpace(Branch) ? null : "branch");

    /// <summary>A short human-friendly slug for logging (<c>org/project/repo</c>).</summary>
    [JsonIgnore]
    public string Slug
    {
        get
        {
            var c = ResolvedComponents;
            return $"{c.Organization}/{c.Project}/{c.Repo}";
        }
    }
}

/// <summary>
///     Accepts either a JSON string (single mode) or array of strings (chain).
///     Writes as a single string when the chain has exactly one element, else
///     as an array.
/// </summary>
public sealed class AzdoAuthChainJsonConverter : JsonConverter<IReadOnlyList<string>>
{
    public override IReadOnlyList<string>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            var s = reader.GetString();
            if (string.IsNullOrWhiteSpace(s))
            {
                throw new JsonException("'auth' string must not be empty.");
            }

            return new[] { s };
        }

        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException($"Expected string or array for 'auth'; got {reader.TokenType}.");
        }

        var list = new List<string>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray)
            {
                if (list.Count == 0)
                {
                    throw new JsonException("'auth' array must not be empty.");
                }

                return list;
            }

            if (reader.TokenType != JsonTokenType.String)
            {
                throw new JsonException($"Expected string inside 'auth' array; got {reader.TokenType}.");
            }

            var v = reader.GetString();
            if (string.IsNullOrWhiteSpace(v))
            {
                throw new JsonException("'auth' array entries must not be empty.");
            }

            list.Add(v);
        }

        throw new JsonException("Unexpected end of JSON while reading 'auth'.");
    }

    public override void Write(Utf8JsonWriter writer, IReadOnlyList<string> value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);

        if (value.Count == 1)
        {
            writer.WriteStringValue(value[0]);
            return;
        }

        writer.WriteStartArray();
        foreach (var s in value)
        {
            writer.WriteStringValue(s);
        }

        writer.WriteEndArray();
    }
}
