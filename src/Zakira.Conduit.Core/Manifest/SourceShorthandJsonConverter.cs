using System.Text.Json;
using System.Text.Json.Serialization;

namespace Zakira.Conduit.Manifest;

/// <summary>
///     Lets the <c>source</c> field of a manifest entry be either a JSON
///     object (the canonical, polymorphic form) or a JSON string (the
///     bare-URI shorthand, which deserialises into a <see cref="UriBasedSkillSource"/>
///     and is then resolved by the inference coordinator at load time).
///     <para>
///         When reading an object, the converter delegates back to the
///         framework's built-in polymorphic deserialiser by removing itself
///         from a sibling <see cref="JsonSerializerOptions"/> instance.
///         On write, the same sibling-options trick is used so the concrete
///         subtype is emitted with its <c>type</c> discriminator.
///     </para>
/// </summary>
public sealed class SourceShorthandJsonConverter : JsonConverter<ISkillSource>
{
    public override ISkillSource? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;

            case JsonTokenType.String:
            {
                var s = reader.GetString();
                if (string.IsNullOrWhiteSpace(s))
                {
                    throw new JsonException("Source shorthand string must not be empty.");
                }

                return new UriBasedSkillSource { Uri = s };
            }

            case JsonTokenType.StartArray:
            {
                var elements = new List<ISkillSource>();
                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndArray)
                    {
                        if (elements.Count == 0)
                        {
                            throw new JsonException("'source' array must not be empty.");
                        }

                        return new ArraySkillSource { Elements = elements };
                    }

                    // Recurse on each element. Re-entering this converter is fine:
                    // we only short-circuit objects/strings here, and the StartArray
                    // branch never re-enters with another StartArray token at this
                    // position (the inner reader has already advanced).
                    var element = Read(ref reader, typeToConvert, options);
                    if (element is null)
                    {
                        throw new JsonException("'source' array elements must not be null.");
                    }

                    if (element is ArraySkillSource)
                    {
                        throw new JsonException("'source' array elements must not themselves be arrays.");
                    }

                    elements.Add(element);
                }

                throw new JsonException("Unexpected end of JSON while reading 'source' array.");
            }

            case JsonTokenType.StartObject:
            {
                // Delegate to the framework's polymorphic deserialiser by using a
                // sibling options instance that doesn't include this converter
                // (otherwise we'd recurse infinitely).
                var fallback = WithoutThisConverter(options);
                return JsonSerializer.Deserialize<ISkillSource>(ref reader, fallback);
            }

            default:
                throw new JsonException($"Unexpected token '{reader.TokenType}' for source; expected string, array or object.");
        }
    }

    public override void Write(Utf8JsonWriter writer, ISkillSource value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);

        // Round-trip via the polymorphic object form. Serialise as the base
        // type so the framework writes the 'type' discriminator from
        // [JsonDerivedType] on ISkillSource. Writing as value.GetType() would
        // emit the concrete subtype with no discriminator and break re-reads.
        var fallback = WithoutThisConverter(options);
        JsonSerializer.Serialize<ISkillSource>(writer, value, fallback);
    }

    private static JsonSerializerOptions WithoutThisConverter(JsonSerializerOptions options)
    {
        var clone = new JsonSerializerOptions(options);
        for (var i = clone.Converters.Count - 1; i >= 0; i--)
        {
            if (clone.Converters[i] is SourceShorthandJsonConverter)
            {
                clone.Converters.RemoveAt(i);
            }
        }

        return clone;
    }
}
