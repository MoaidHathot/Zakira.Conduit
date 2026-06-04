using System.Text.Json;
using System.Text.Json.Serialization;
using Zakira.Conduit.Strategies;

namespace Zakira.Conduit.Manifest;

/// <summary>
///     The manifest-global <c>strategies</c> section. Each property addresses
///     one strategy and supplies cross-entry configuration (e.g. additions
///     and removals to the skills-strategy harness registry, default
///     collision policy). All sub-sections are optional; the manifest is
///     valid without a <c>strategies</c> block at all.
/// </summary>
public sealed record StrategiesConfig
{
    /// <summary>
    ///     Skills-strategy configuration. See <see cref="SkillsStrategyConfig"/>.
    /// </summary>
    [JsonPropertyName("skills")]
    public SkillsStrategyConfig? Skills { get; init; }
}

/// <summary>
///     Manifest-global configuration for the skills strategy.
/// </summary>
public sealed record SkillsStrategyConfig
{
    /// <summary>
    ///     Custom harness map merged with the built-in registry. Keys are
    ///     harness names (the leading dot is optional; it is stripped during
    ///     normalisation). Values are either a single path string or an array
    ///     of path strings; both are stored as
    ///     <see cref="IReadOnlyList{T}"/> via
    ///     <see cref="HarnessRegistryConverter"/>. A null value removes the
    ///     built-in entry of the same name.
    /// </summary>
    [JsonPropertyName("harnessRegistry")]
    [JsonConverter(typeof(HarnessRegistryConverter))]
    public IReadOnlyDictionary<string, IReadOnlyList<string>?>? HarnessRegistry { get; init; }

    /// <summary>
    ///     Manifest-wide default collision policy for the skills strategy.
    ///     Per-entry <see cref="ConduitEntry.OnCollision"/> overrides this.
    /// </summary>
    [JsonPropertyName("onCollision")]
    [JsonConverter(typeof(NullableOnCollisionJsonConverter))]
    public OnCollisionPolicy? OnCollision { get; init; }
}

/// <summary>
///     Accepts the harness-registry shape:
///     <code>
///     {
///       ".opencode": "skills/",
///       ".other": ["a/skills/", "b/skills/"],
///       ".claude": null
///     }
///     </code>
///     Each value is normalised into an <see cref="IReadOnlyList{T}"/> of
///     strings (single string promoted to one-element list); <c>null</c>
///     values pass through as null and signal "remove this entry from the
///     built-in registry".
/// </summary>
public sealed class HarnessRegistryConverter : JsonConverter<IReadOnlyDictionary<string, IReadOnlyList<string>?>>
{
    public override IReadOnlyDictionary<string, IReadOnlyList<string>?>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException($"Expected an object for 'harnessRegistry'; got {reader.TokenType}.");
        }

        var result = new Dictionary<string, IReadOnlyList<string>?>(StringComparer.OrdinalIgnoreCase);

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return result;
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException($"Unexpected token {reader.TokenType} inside 'harnessRegistry'.");
            }

            var key = reader.GetString();
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new JsonException("'harnessRegistry' keys must be non-empty strings.");
            }

            reader.Read();

            switch (reader.TokenType)
            {
                case JsonTokenType.Null:
                    result[key] = null;
                    break;

                case JsonTokenType.String:
                {
                    var path = reader.GetString();
                    if (string.IsNullOrWhiteSpace(path))
                    {
                        throw new JsonException($"'harnessRegistry[\"{key}\"]' string must not be empty.");
                    }

                    result[key] = new[] { path };
                    break;
                }

                case JsonTokenType.StartArray:
                {
                    var paths = new List<string>();
                    while (reader.Read())
                    {
                        if (reader.TokenType == JsonTokenType.EndArray)
                        {
                            break;
                        }

                        if (reader.TokenType != JsonTokenType.String)
                        {
                            throw new JsonException($"'harnessRegistry[\"{key}\"]' array elements must be strings; got {reader.TokenType}.");
                        }

                        var path = reader.GetString();
                        if (string.IsNullOrWhiteSpace(path))
                        {
                            throw new JsonException($"'harnessRegistry[\"{key}\"]' array elements must be non-empty strings.");
                        }

                        paths.Add(path);
                    }

                    if (paths.Count == 0)
                    {
                        throw new JsonException($"'harnessRegistry[\"{key}\"]' array must contain at least one path.");
                    }

                    result[key] = paths;
                    break;
                }

                default:
                    throw new JsonException($"Unexpected token {reader.TokenType} for 'harnessRegistry[\"{key}\"]'.");
            }
        }

        throw new JsonException("Unexpected end of JSON while reading 'harnessRegistry'.");
    }

    public override void Write(Utf8JsonWriter writer, IReadOnlyDictionary<string, IReadOnlyList<string>?> value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);

        writer.WriteStartObject();

        foreach (var (key, paths) in value)
        {
            writer.WritePropertyName(key);

            if (paths is null)
            {
                writer.WriteNullValue();
                continue;
            }

            if (paths.Count == 1)
            {
                writer.WriteStringValue(paths[0]);
                continue;
            }

            writer.WriteStartArray();
            foreach (var path in paths)
            {
                writer.WriteStringValue(path);
            }
            writer.WriteEndArray();
        }

        writer.WriteEndObject();
    }
}
