using System.Text.Json;
using System.Text.Json.Serialization;
using Zakira.Conduit.Strategies;

namespace Zakira.Conduit.Manifest;

/// <summary>
///     A single entry in a conduit manifest: one remote source, mirrored to
///     one or more local target directories.
/// </summary>
public sealed record ConduitEntry
{
    /// <summary>
    ///     A unique, human-friendly identifier for this entry. Used both for
    ///     logging and as the sub-directory name inside each target where the
    ///     mirrored content is placed (i.e. each target receives a
    ///     <c>&lt;target&gt;/&lt;name&gt;/</c> directory).
    ///     <para>
    ///         Optional in the manifest. When omitted, the
    ///         <see cref="Sources.Inference.SourceInferenceCoordinator"/>
    ///         derives a default name from the source (GitHub/AzDO repo name,
    ///         local directory basename, etc.). May also be supplied indirectly
    ///         via the in-string arrow shorthand
    ///         (<c>"https://github.com/owner/repo -&gt; MyName"</c>) or the
    ///         object wrapper form (<c>{ "source": ..., "as": "MyName" }</c>);
    ///         in both cases the supplied alias becomes the entry name.
    ///     </para>
    ///     <para>
    ///         After the coordinator runs every entry is guaranteed to have a
    ///         non-empty <see cref="Name"/>; if no name can be derived the
    ///         <see cref="ManifestValidator"/> errors with a clear message.
    ///     </para>
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>
    ///     The remote source this entry mirrors. The concrete type is
    ///     determined by the polymorphic JSON discriminator.
    ///     <para>
    ///         For ergonomics the JSON value may also be a bare string
    ///         (e.g. <c>"https://github.com/owner/repo"</c> or <c>"./skills"</c>),
    ///         in which case it is deserialised as a
    ///         <see cref="UriBasedSource"/> and resolved to its concrete
    ///         kind by the inference coordinator at manifest load time.
    ///     </para>
    /// </summary>
    [JsonPropertyName("source")]
    [JsonConverter(typeof(SourceShorthandJsonConverter))]
    public required ISource Source { get; init; }

    /// <summary>
    ///     One or more local target directories. The exact destination layout
    ///     inside each target is decided by the entry's <see cref="Strategy"/>:
    ///     the default <c>wrap</c> creates a sub-directory named after the
    ///     entry, while other strategies (<c>flat</c>, <c>expand</c>,
    ///     <c>skills</c>) emit different layouts. Paths may include <c>~</c>
    ///     and environment variables such as <c>$XDG_CONFIG_HOME</c>.
    ///     <para>
    ///         Each entry in the array can be either a plain string (the
    ///         common case) or an object with <c>path</c> and an optional
    ///         <c>as</c> alias. The aliased form is honoured only by
    ///         strategies that produce a per-target wrapping sub-directory
    ///         (currently: <c>wrap</c>).
    ///     </para>
    /// </summary>
    [JsonPropertyName("targets")]
    public required IReadOnlyList<PathSpec> Targets { get; init; }

    /// <summary>
    ///     Optional, free-form description for documentation purposes.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>
    ///     If <see langword="true"/>, the entry is skipped during sync.
    /// </summary>
    [JsonPropertyName("disabled")]
    public bool Disabled { get; init; }

    /// <summary>
    ///     Which planning strategy controls the per-target destination layout
    ///     for this entry. Accepts (case-insensitive) <c>"wrap"</c> (default),
    ///     <c>"flat"</c>, <c>"expand"</c>, or <c>"skills"</c>. Unknown values
    ///     fail validation with the list of registered strategies.
    /// </summary>
    [JsonPropertyName("strategy")]
    public string? Strategy { get; init; }

    /// <summary>
    ///     When set to <see cref="Strategies.GroupBy.Source"/>, every planned
    ///     destination is wrapped in an extra source-named sub-directory.
    ///     Composes with every strategy. Default is
    ///     <see cref="Strategies.GroupBy.None"/>.
    /// </summary>
    [JsonPropertyName("groupBy")]
    [JsonConverter(typeof(GroupByJsonConverter))]
    public GroupBy GroupBy { get; init; } = GroupBy.None;

    /// <summary>
    ///     Per-entry override for the collision policy applied by the
    ///     entry's <see cref="Strategy"/> when two planned destinations would
    ///     write to the same path. When unset, the strategy falls back to the
    ///     manifest-global default (e.g. <c>strategies.skills.onCollision</c>)
    ///     and ultimately to <see cref="OnCollisionPolicy.Error"/>.
    /// </summary>
    [JsonPropertyName("onCollision")]
    [JsonConverter(typeof(NullableOnCollisionJsonConverter))]
    public OnCollisionPolicy? OnCollision { get; init; }

    /// <summary>
    ///     Skills-strategy filter: when present and non-empty, only skills
    ///     whose folder basename (matching the <c>SKILL.md</c> frontmatter
    ///     <c>name:</c>) is listed are mirrored. Other skills in the source
    ///     are silently skipped. Ignored for non-<c>skills</c> strategies.
    /// </summary>
    [JsonPropertyName("skills")]
    public IReadOnlyList<string>? Skills { get; init; }

    /// <summary>
    ///     Skills-strategy filter: restricts which agent harnesses found
    ///     under the target the skill is fanned out into. Two shapes
    ///     supported by <see cref="HarnessSelectorJsonConverter"/>:
    ///     <list type="bullet">
    ///         <item><description>
    ///             A boolean (<c>true</c> = require harness discovery and
    ///             error if none are found; <c>false</c> = skip the harness
    ///             scan and treat the target as a literal skills directory).
    ///         </description></item>
    ///         <item><description>
    ///             An array of harness names (e.g. <c>["opencode", "claude"]</c>),
    ///             which both enables discovery and restricts it to the named
    ///             harnesses.
    ///         </description></item>
    ///     </list>
    ///     Ignored for non-<c>skills</c> strategies.
    /// </summary>
    [JsonPropertyName("harness")]
    [JsonConverter(typeof(HarnessSelectorJsonConverter))]
    public HarnessSelector? Harness { get; init; }

    /// <summary>
    ///     The resolved entry name. Equivalent to <see cref="Name"/> but
    ///     guarantees a non-null value, which is upheld by the inference
    ///     coordinator (it fills <see cref="Name"/> from the source or alias
    ///     before the validator runs). Throws if accessed on a raw
    ///     pre-inference entry whose <see cref="Name"/> is still null.
    /// </summary>
    [JsonIgnore]
    public string ResolvedName => Name
        ?? throw new InvalidOperationException(
            "ConduitEntry.Name is not resolved. The inference coordinator must run on the manifest before downstream code reads ResolvedName.");

    /// <summary>
    ///     Index of the on-disk entry this in-memory entry came from. For
    ///     array-expanded sub-entries, this is the index of the
    ///     <i>parent</i> on-disk entry that carried the array. Populated by
    ///     <see cref="Sources.Inference.SourceInferenceCoordinator"/>; used
    ///     by <c>conduit pin</c> / <c>conduit unpin</c> to address the exact
    ///     spot in the manifest file for surgical rewrites.
    /// </summary>
    [JsonIgnore]
    public int? OriginalDiskEntryIndex { get; init; }

    /// <summary>
    ///     For an entry expanded from an array source, the index of this
    ///     element within the parent entry's <c>source</c> array.
    ///     <see langword="null"/> for non-array entries.
    /// </summary>
    [JsonIgnore]
    public int? OriginalArrayElementIndex { get; init; }
}

/// <summary>
///     Accepts <c>"none"</c> (case-insensitive) or <c>"source"</c> for
///     <see cref="ConduitEntry.GroupBy"/>. Writes the lowercase string form.
///     Unrecognised values throw at deserialisation time.
/// </summary>
public sealed class GroupByJsonConverter : JsonConverter<GroupBy>
{
    public override GroupBy Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException($"Expected a string for 'groupBy'; got {reader.TokenType}.");
        }

        var raw = reader.GetString();
        return raw?.Trim().ToLowerInvariant() switch
        {
            null or "" or "none" => GroupBy.None,
            "source" => GroupBy.Source,
            _ => throw new JsonException($"Unknown 'groupBy' value '{raw}'. Allowed: 'none', 'source'."),
        };
    }

    public override void Write(Utf8JsonWriter writer, GroupBy value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(value switch
        {
            GroupBy.Source => "source",
            _ => "none",
        });
    }
}

/// <summary>
///     Accepts <c>"error"</c>, <c>"skip"</c>, or <c>"last-wins"</c>
///     (case-insensitive) for <see cref="ConduitEntry.OnCollision"/>. Returns
///     <see langword="null"/> when the property is JSON null (meaning "fall
///     back to manifest-global or hard-coded default").
/// </summary>
public sealed class NullableOnCollisionJsonConverter : JsonConverter<OnCollisionPolicy?>
{
    public override OnCollisionPolicy? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException($"Expected a string for 'onCollision'; got {reader.TokenType}.");
        }

        var raw = reader.GetString();
        return raw?.Trim().ToLowerInvariant() switch
        {
            null or "" => null,
            "error" => OnCollisionPolicy.Error,
            "skip" => OnCollisionPolicy.Skip,
            "last-wins" or "lastwins" or "last_wins" => OnCollisionPolicy.LastWins,
            _ => throw new JsonException($"Unknown 'onCollision' value '{raw}'. Allowed: 'error', 'skip', 'last-wins'."),
        };
    }

    public override void Write(Utf8JsonWriter writer, OnCollisionPolicy? value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStringValue(value.Value switch
        {
            OnCollisionPolicy.Skip => "skip",
            OnCollisionPolicy.LastWins => "last-wins",
            _ => "error",
        });
    }
}

/// <summary>
///     Per-entry harness selector for the skills strategy. Two shapes
///     accepted by <see cref="HarnessSelectorJsonConverter"/>:
///     <list type="bullet">
///         <item><description>
///             Boolean: <c>true</c> = require discovery, <c>false</c> = skip
///             discovery and treat the target as a literal skills directory.
///         </description></item>
///         <item><description>
///             Array of harness names: enables discovery, restricted to the
///             named harnesses.
///         </description></item>
///     </list>
/// </summary>
public sealed record HarnessSelector(bool Enabled, IReadOnlyList<string>? Filter)
{
    /// <summary>Discovery requested with no name filter.</summary>
    public static HarnessSelector EnabledAll { get; } = new(Enabled: true, Filter: null);

    /// <summary>Discovery suppressed entirely.</summary>
    public static HarnessSelector Disabled { get; } = new(Enabled: false, Filter: null);

    /// <summary>Discovery requested, restricted to the named harnesses.</summary>
    public static HarnessSelector Named(IReadOnlyList<string> names) => new(Enabled: true, Filter: names);
}

/// <summary>
///     Accepts <c>true</c>, <c>false</c>, <c>"name"</c>, or <c>["a","b"]</c>
///     for <see cref="ConduitEntry.Harness"/>. Writes the most compact shape
///     that round-trips the value.
/// </summary>
public sealed class HarnessSelectorJsonConverter : JsonConverter<HarnessSelector>
{
    public override HarnessSelector? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;

            case JsonTokenType.True:
                return HarnessSelector.EnabledAll;

            case JsonTokenType.False:
                return HarnessSelector.Disabled;

            case JsonTokenType.String:
            {
                var single = reader.GetString();
                if (string.IsNullOrWhiteSpace(single))
                {
                    throw new JsonException("'harness' string must not be empty.");
                }

                return HarnessSelector.Named(new[] { single.Trim() });
            }

            case JsonTokenType.StartArray:
            {
                var names = new List<string>();
                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndArray)
                    {
                        if (names.Count == 0)
                        {
                            throw new JsonException("'harness' array must contain at least one name.");
                        }

                        return HarnessSelector.Named(names);
                    }

                    if (reader.TokenType != JsonTokenType.String)
                    {
                        throw new JsonException($"'harness' array elements must be strings; got {reader.TokenType}.");
                    }

                    var name = reader.GetString();
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        throw new JsonException("'harness' array elements must be non-empty strings.");
                    }

                    names.Add(name.Trim());
                }

                throw new JsonException("Unexpected end of JSON while reading 'harness' array.");
            }

            default:
                throw new JsonException($"Unexpected token {reader.TokenType} for 'harness'.");
        }
    }

    public override void Write(Utf8JsonWriter writer, HarnessSelector value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);

        if (value.Filter is null)
        {
            writer.WriteBooleanValue(value.Enabled);
            return;
        }

        writer.WriteStartArray();
        foreach (var name in value.Filter)
        {
            writer.WriteStringValue(name);
        }
        writer.WriteEndArray();
    }
}
