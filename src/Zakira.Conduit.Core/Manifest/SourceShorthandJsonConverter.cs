using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Zakira.Conduit.Manifest;

/// <summary>
///     Lets the <c>source</c> field of a manifest entry be one of several
///     JSON shapes:
///     <list type="bullet">
///         <item><description>
///             The canonical polymorphic object form
///             (<c>{ "type": "github", ... }</c>); delegated to the framework's
///             discriminator-driven deserialiser.
///         </description></item>
///         <item><description>
///             A bare URI shorthand string
///             (<c>"https://github.com/owner/repo"</c>); deserialised into a
///             <see cref="UriBasedSource"/> and later resolved by the
///             inference coordinator.
///         </description></item>
///         <item><description>
///             An <b>aliased shorthand string</b> that suffixes the URI with
///             <c> -&gt; Name</c>
///             (<c>"https://github.com/owner/repo -&gt; MyName"</c>); the
///             suffix becomes the destination alias and the leading portion is
///             treated as a normal URI shorthand. The alias must match
///             <c>^[A-Za-z0-9._-]+$</c>.
///         </description></item>
///         <item><description>
///             An <b>aliased wrapper object</b>
///             (<c>{ "source": ..., "as": "MyName" }</c>); recognised by the
///             absence of a <c>type</c> discriminator. The inner <c>source</c>
///             may itself be any of the above shapes (concrete object, bare
///             shorthand string, etc.) but not another wrapper.
///         </description></item>
///         <item><description>
///             A JSON array of any of the above (expanded by the inference
///             coordinator into one sub-entry per element).
///         </description></item>
///     </list>
///     <para>
///         On write the converter round-trips <see cref="AliasedSource"/>
///         back into its object-wrapper JSON shape, and delegates every other
///         concrete kind to the polymorphic serialiser by removing itself from
///         a sibling <see cref="JsonSerializerOptions"/> instance.
///     </para>
/// </summary>
public sealed class SourceShorthandJsonConverter : JsonConverter<ISource>
{
    /// <summary>The literal separator used by the in-string alias suffix.</summary>
    private const string ArrowSeparator = " -> ";

    /// <summary>
    ///     Charset accepted for both in-string and wrapper-object aliases.
    ///     Mirrors the entry-name pattern enforced by
    ///     <see cref="ManifestValidator"/> so an alias can be substituted
    ///     anywhere an entry name is expected.
    /// </summary>
    private static readonly Regex AliasNamePattern = new(
        "^[A-Za-z0-9._-]+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public override ISource? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;

            case JsonTokenType.String:
                return ReadStringShorthand(ref reader);

            case JsonTokenType.StartArray:
                return ReadArray(ref reader, typeToConvert, options);

            case JsonTokenType.StartObject:
                return ReadObject(ref reader, options);

            default:
                throw new JsonException($"Unexpected token '{reader.TokenType}' for source; expected string, array or object.");
        }
    }

    private static ISource ReadStringShorthand(ref Utf8JsonReader reader)
    {
        var s = reader.GetString();
        if (string.IsNullOrWhiteSpace(s))
        {
            throw new JsonException("Source shorthand string must not be empty.");
        }

        // In-string alias: split on the LAST occurrence of " -> ". A real URI
        // virtually never contains the substring " -> "; if it ever does, the
        // alias suffix only triggers when the tail also matches the strict
        // entry-name charset, so the false-positive surface is essentially nil.
        var sepIdx = s.LastIndexOf(ArrowSeparator, StringComparison.Ordinal);
        if (sepIdx > 0)
        {
            var candidateAlias = s[(sepIdx + ArrowSeparator.Length)..];
            if (AliasNamePattern.IsMatch(candidateAlias))
            {
                var leftUri = s[..sepIdx];
                if (string.IsNullOrWhiteSpace(leftUri))
                {
                    throw new JsonException(
                        "Source shorthand string must have a non-empty URI before the ' -> ' alias suffix.");
                }

                return new AliasedSource
                {
                    Inner = new UriBasedSource { Uri = leftUri },
                    As = candidateAlias,
                };
            }
        }

        return new UriBasedSource { Uri = s };
    }

    private ArraySource ReadArray(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var elements = new List<ISource>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray)
            {
                if (elements.Count == 0)
                {
                    throw new JsonException("'source' array must not be empty.");
                }

                return new ArraySource { Elements = elements };
            }

            // Recurse on each element. Re-entering this converter is fine: we
            // only short-circuit objects/strings/wrappers here, and the
            // StartArray branch never re-enters with another StartArray token at
            // this position (the inner reader has already advanced).
            var element = Read(ref reader, typeToConvert, options);
            if (element is null)
            {
                throw new JsonException("'source' array elements must not be null.");
            }

            if (element is ArraySource)
            {
                throw new JsonException("'source' array elements must not themselves be arrays.");
            }

            elements.Add(element);
        }

        throw new JsonException("Unexpected end of JSON while reading 'source' array.");
    }

    private static ISource? ReadObject(ref Utf8JsonReader reader, JsonSerializerOptions options)
    {
        // Buffer the object so we can peek for the wrapper shape without
        // consuming the reader twice. Manifests are small, so the extra
        // allocation is fine; the alternative (manual look-ahead through the
        // ref-struct reader) is much more error-prone.
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        var hasSource = root.TryGetProperty("source", out var sourceProp);
        var hasType = root.TryGetProperty("type", out _);

        if (hasSource && !hasType)
        {
            return ReadWrapper(root, sourceProp, options);
        }

        // Concrete object form: delegate to the framework's polymorphic
        // deserialiser via a sibling options instance that doesn't include
        // this converter (otherwise we'd recurse infinitely).
        var fallback = WithoutThisConverter(options);
        return root.Deserialize<ISource>(fallback);
    }

    private static AliasedSource ReadWrapper(JsonElement root, JsonElement sourceProp, JsonSerializerOptions options)
    {
        if (!root.TryGetProperty("as", out var asProp))
        {
            throw new JsonException(
                "Source wrapper object must include 'as' (the destination alias). " +
                "Use { \"source\": ..., \"as\": \"Name\" }.");
        }

        if (asProp.ValueKind != JsonValueKind.String)
        {
            throw new JsonException("Source wrapper 'as' must be a string.");
        }

        var alias = asProp.GetString();
        if (string.IsNullOrWhiteSpace(alias))
        {
            throw new JsonException("Source wrapper 'as' must be a non-empty string.");
        }

        if (!AliasNamePattern.IsMatch(alias))
        {
            throw new JsonException(
                $"Source wrapper 'as' value '{alias}' contains invalid characters; " +
                "only letters, digits, '.', '_' and '-' are allowed.");
        }

        // Reject stray properties so typos don't silently no-op.
        foreach (var prop in root.EnumerateObject())
        {
            if (prop.NameEquals("source") || prop.NameEquals("as"))
            {
                continue;
            }

            throw new JsonException(
                $"Unexpected property '{prop.Name}' in source wrapper; only 'source' and 'as' are allowed.");
        }

        // Deserialise the inner 'source' through this converter (allows the
        // inner source to itself be a shorthand string, concrete object, etc.).
        // We can't reuse JsonElement.Deserialize<ISource> with this
        // converter in options because ISource is also polymorphism-
        // attributed and the framework rejects the combination (the base
        // converter doesn't opt-in to polymorphism metadata). Instead we round-
        // trip the element through a fresh Utf8JsonReader and re-enter Read.
        ISource? innerSource;
        try
        {
            innerSource = DeserializeInner(sourceProp, options);
        }
        catch (System.Text.Json.JsonException)
        {
            throw;
        }
        catch (NotSupportedException ex)
        {
            throw new JsonException($"Source wrapper 'source' could not be parsed: {ex.Message}", ex);
        }

        if (innerSource is null)
        {
            throw new JsonException("Source wrapper 'source' must not be null.");
        }

        if (innerSource is AliasedSource)
        {
            throw new JsonException(
                "Source wrapper 'source' must not itself be another aliased wrapper; " +
                "only one alias may apply per source.");
        }

        if (innerSource is ArraySource)
        {
            throw new JsonException(
                "Source wrapper 'source' must not be an array; " +
                "aliases apply to single sources only.");
        }

        return new AliasedSource { Inner = innerSource, As = alias! };
    }

    public override void Write(Utf8JsonWriter writer, ISource value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);

        // Round-trip aliased sources back into their wrapper object shape.
        // Normal pipelines unwrap these before serialisation, but supporting
        // it keeps the converter symmetric (and helps any external tool that
        // re-emits a freshly-parsed pre-inference manifest).
        if (value is AliasedSource aliased)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("source");
            JsonSerializer.Serialize(writer, aliased.Inner, options);
            writer.WriteString("as", aliased.As);
            writer.WriteEndObject();
            return;
        }

        // Concrete kinds: delegate to the polymorphic serialiser. Serialise as
        // the base type so the framework writes the 'type' discriminator from
        // [JsonDerivedType] on ISource. Writing as value.GetType() would
        // emit the concrete subtype with no discriminator and break re-reads.
        var fallback = WithoutThisConverter(options);
        JsonSerializer.Serialize<ISource>(writer, value, fallback);
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

    /// <summary>
    ///     Returns a sibling <see cref="JsonSerializerOptions"/> that
    ///     guarantees this converter is in the converter list, so a nested
    ///     <see cref="JsonElement.Deserialize{T}(JsonSerializerOptions?)"/>
    ///     call on the inner <c>source</c> of a wrapper routes back through
    ///     this converter's <see cref="Read"/>. The converter is normally
    ///     wired only via <c>[JsonConverter]</c> on
    ///     <see cref="ConduitEntry.Source"/>, which is a property-scoped hint
    ///     the framework doesn't honour for ad-hoc element deserialisation.
    /// </summary>
    private static JsonSerializerOptions WithThisConverter(JsonSerializerOptions options)
    {
        if (options.Converters.Any(c => c is SourceShorthandJsonConverter))
        {
            return options;
        }

        var clone = new JsonSerializerOptions(options);
        clone.Converters.Add(new SourceShorthandJsonConverter());
        return clone;
    }

    /// <summary>
    ///     Re-enters <see cref="Read"/> on an inner <see cref="JsonElement"/>
    ///     by round-tripping it through a fresh <see cref="Utf8JsonReader"/>.
    ///     This is necessary because <c>JsonElement.Deserialize&lt;ISource&gt;</c>
    ///     conflicts with the framework's polymorphism handling when this
    ///     converter is added to <see cref="JsonSerializerOptions.Converters"/>.
    /// </summary>
    private static ISource? DeserializeInner(JsonElement element, JsonSerializerOptions options)
    {
        // GetRawText returns canonical JSON for the element. UTF-8 encode and
        // hand the bytes to a fresh reader; advance once so the reader is
        // positioned at the value start (the same state the framework hands
        // to JsonConverter.Read).
        var bytes = System.Text.Encoding.UTF8.GetBytes(element.GetRawText());
        var reader = new Utf8JsonReader(bytes);
        if (!reader.Read())
        {
            return null;
        }

        var instance = new SourceShorthandJsonConverter();
        return instance.Read(ref reader, typeof(ISource), options);
    }
}
