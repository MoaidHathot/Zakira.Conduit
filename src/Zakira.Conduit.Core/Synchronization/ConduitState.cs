using System.Text.Json.Serialization;

namespace Zakira.Conduit.Synchronization;

/// <summary>
///     On-disk state recorded after each successful sync of an entry. Lives
///     next to the manifest as <c>.conduit-state.json</c>. Used to skip
///     unchanged entries on subsequent runs and to enable conditional fetches
///     (ETag, commit pinning).
/// </summary>
public sealed record ConduitState
{
    /// <summary>State-file schema version.</summary>
    [JsonPropertyName("version")]
    public int Version { get; init; } = 1;

    /// <summary>
    ///     Per-entry records keyed by <see cref="Manifest.ConduitEntry.Name"/>.
    /// </summary>
    [JsonPropertyName("entries")]
    public Dictionary<string, EntryState> Entries { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>The last-known state for one manifest entry.</summary>
public sealed record EntryState
{
    /// <summary>
    ///     The commit / branch / tag that was most recently fetched, resolved
    ///     to the most specific identifier we know (typically a commit SHA).
    /// </summary>
    [JsonPropertyName("resolvedRef")]
    public string? ResolvedRef { get; init; }

    /// <summary>
    ///     The HTTP <c>ETag</c> returned by the source on the last fetch.
    ///     Used to issue conditional GETs (<c>If-None-Match</c>).
    /// </summary>
    [JsonPropertyName("etag")]
    public string? Etag { get; init; }

    /// <summary>UTC timestamp of the last successful sync.</summary>
    [JsonPropertyName("lastSyncUtc")]
    public DateTimeOffset? LastSyncUtc { get; init; }

    /// <summary>
    ///     Absolute target directories the entry was mirrored to. Used to
    ///     detect manual deletions so we re-mirror instead of erroneously
    ///     skipping.
    /// </summary>
    [JsonPropertyName("targets")]
    public IReadOnlyList<string> Targets { get; init; } = Array.Empty<string>();

    /// <summary>
    ///     A hash of the source's content fingerprint, used to short-circuit
    ///     local-source syncs when nothing on disk has changed. The format is
    ///     implementation-defined and only meaningful within a single major
    ///     version of conduit; treat it as opaque.
    /// </summary>
    [JsonPropertyName("sourceContentHash")]
    public string? SourceContentHash { get; init; }
}
