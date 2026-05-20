using System.Text.Json;
using System.Text.Json.Serialization;

namespace Zakira.Conduit.Manifest;

/// <summary>
///     A path with an optional rename. Used in two places in the manifest:
///     <list type="bullet">
///         <item><description>
///             As a source sub-path (the <c>paths</c> field on a source). When
///             the entry mirrors more than one of these, <see cref="As"/>
///             overrides the basename-derived destination directory name.
///         </description></item>
///         <item><description>
///             As a target directory (the <c>targets</c> field on an entry).
///             When the entry produces exactly one content unit, <see cref="As"/>
///             overrides the entry name as the destination sub-directory.
///         </description></item>
///     </list>
///     <para>
///         Accepts two JSON shapes thanks to <see cref="PathSpecJsonConverter"/>:
///         <c>"some/path"</c> (compact) or <c>{"path": "some/path", "as": "alias"}</c>
///         (renamed).
///     </para>
/// </summary>
[JsonConverter(typeof(PathSpecJsonConverter))]
public sealed record PathSpec(string Path, string? As = null)
{
    /// <summary>
    ///     Implicit conversion from <see cref="string"/>, so existing C#
    ///     collection initializers like <c>[ "./out" ]</c> still bind to a
    ///     <see cref="IReadOnlyList{T}"/> of <see cref="PathSpec"/>.
    /// </summary>
    public static implicit operator PathSpec(string path) => new(path);

    /// <summary>
    ///     Returns <see cref="As"/> when set, otherwise the basename of
    ///     <see cref="Path"/>. Used by the synchronizer to derive a destination
    ///     sub-directory name for multi-unit entries.
    /// </summary>
    public string ResolvedBasename
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(As))
            {
                return As;
            }

            var normalized = Path.Replace('\\', '/').TrimEnd('/');
            var slash = normalized.LastIndexOf('/');
            return slash < 0 ? normalized : normalized[(slash + 1)..];
        }
    }
}

/// <summary>
///     Accepts either a JSON string (compact form) or a JSON object with
///     <c>path</c> + optional <c>as</c> (renamed form). Writes as a string
///     when <see cref="PathSpec.As"/> is null so round-trips preserve the
///     simplest form.
/// </summary>
public sealed class PathSpecJsonConverter : JsonConverter<PathSpec>
{
    public override PathSpec? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var s = reader.GetString();
            if (string.IsNullOrWhiteSpace(s))
            {
                throw new JsonException("Path string must not be empty.");
            }

            return new PathSpec(s);
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException($"Expected a string or object for PathSpec; got {reader.TokenType}.");
        }

        string? path = null;
        string? @as = null;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    throw new JsonException("PathSpec object is missing the required 'path' property.");
                }

                return new PathSpec(path, string.IsNullOrWhiteSpace(@as) ? null : @as);
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException($"Unexpected token {reader.TokenType} inside PathSpec object.");
            }

            var name = reader.GetString();
            reader.Read();

            switch (name)
            {
                case "path":
                case "from":
                    path = reader.GetString();
                    break;
                case "as":
                case "alias":
                    @as = reader.GetString();
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }

        throw new JsonException("Unexpected end of JSON while reading PathSpec object.");
    }

    public override void Write(Utf8JsonWriter writer, PathSpec value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);

        if (string.IsNullOrWhiteSpace(value.As))
        {
            writer.WriteStringValue(value.Path);
            return;
        }

        writer.WriteStartObject();
        writer.WriteString("path", value.Path);
        writer.WriteString("as", value.As);
        writer.WriteEndObject();
    }
}
