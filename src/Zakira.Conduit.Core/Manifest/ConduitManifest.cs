using System.Text.Json.Serialization;

namespace Zakira.Conduit.Manifest;

/// <summary>
///     The deserialized representation of a <c>conduit.json</c> manifest file.
/// </summary>
public sealed record ConduitManifest
{
    /// <summary>
    ///     The manifest schema version. Used to gate forward-compatible
    ///     changes. Must equal <see cref="ManifestNames.CurrentSchemaVersion"/>.
    /// </summary>
    [JsonPropertyName("version")]
    public int Version { get; init; } = ManifestNames.CurrentSchemaVersion;

    /// <summary>
    ///     The list of entries to sync.
    /// </summary>
    [JsonPropertyName("entries")]
    public IReadOnlyList<ConduitEntry> Entries { get; init; } = Array.Empty<ConduitEntry>();

    /// <summary>
    ///     Optional JSON-schema URL. Consumed by editors only; ignored at runtime.
    /// </summary>
    [JsonPropertyName("$schema")]
    public string? Schema { get; init; }
}
