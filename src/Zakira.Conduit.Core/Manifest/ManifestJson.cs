using System.Text.Json;
using System.Text.Json.Serialization;

namespace Zakira.Conduit.Manifest;

/// <summary>
///     Centralized <see cref="JsonSerializerOptions"/> for manifest IO.
/// </summary>
public static class ManifestJson
{
    /// <summary>
    ///     Read options used when loading a manifest.
    /// </summary>
    public static JsonSerializerOptions ReadOptions { get; } = CreateOptions(writeIndented: false);

    /// <summary>
    ///     Write options used when generating a starter manifest. Indented for
    ///     human consumption.
    /// </summary>
    public static JsonSerializerOptions WriteOptions { get; } = CreateOptions(writeIndented: true);

    private static JsonSerializerOptions CreateOptions(bool writeIndented) =>
        new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = writeIndented,
            Converters =
            {
                new JsonStringEnumConverter(JsonNamingPolicy.CamelCase),
            },
        };
}
