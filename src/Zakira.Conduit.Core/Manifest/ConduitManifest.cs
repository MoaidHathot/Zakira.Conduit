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

    /// <summary>
    ///     Optional manifest-global strategy configuration: per-strategy
    ///     defaults and (for the skills strategy) custom harness registry
    ///     additions and removals.
    /// </summary>
    [JsonPropertyName("strategies")]
    public StrategiesConfig? Strategies { get; init; }
}
