using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Zakira.Conduit.Synchronization;

/// <summary>
///     Default <see cref="IConduitStateStore"/> backed by a JSON file
///     <c>.conduit-state.json</c> next to the manifest. Writes are atomic
///     (temp file + rename) and serialised with a per-state-file lock to
///     keep parallel entry syncs safe.
/// </summary>
public sealed class JsonConduitStateStore : IConduitStateStore
{
    /// <summary>The filename that lives next to the manifest.</summary>
    public const string StateFileName = ".conduit-state.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
    };

    private readonly object _gate = new();
    private readonly ILogger<JsonConduitStateStore> _logger;

    public JsonConduitStateStore(ILogger<JsonConduitStateStore> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public string GetStateFilePath(string manifestPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);
        var dir = Path.GetDirectoryName(Path.GetFullPath(manifestPath))
                  ?? throw new ArgumentException("Manifest path has no parent directory.", nameof(manifestPath));
        return Path.Combine(dir, StateFileName);
    }

    /// <inheritdoc />
    public async Task<ConduitState> LoadAsync(string manifestPath, CancellationToken cancellationToken = default)
    {
        var path = GetStateFilePath(manifestPath);
        if (!File.Exists(path))
        {
            return new ConduitState();
        }

        try
        {
            await using var stream = File.OpenRead(path);
            var state = await JsonSerializer.DeserializeAsync<ConduitState>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
            return state ?? new ConduitState();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "State file '{Path}' is corrupt; ignoring and starting fresh", path);
            return new ConduitState();
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "State file '{Path}' could not be read; ignoring", path);
            return new ConduitState();
        }
    }

    /// <inheritdoc />
    public EntryState? GetEntry(ConduitState state, string entryName)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(entryName);

        lock (_gate)
        {
            return state.Entries.TryGetValue(entryName, out var entry) ? entry : null;
        }
    }

    /// <inheritdoc />
    public void UpdateEntry(ConduitState state, string entryName, EntryState newRecord)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(entryName);
        ArgumentNullException.ThrowIfNull(newRecord);

        lock (_gate)
        {
            state.Entries[entryName] = newRecord;
        }
    }

    /// <inheritdoc />
    public void RemoveEntry(ConduitState state, string entryName)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(entryName);

        lock (_gate)
        {
            state.Entries.Remove(entryName);
        }
    }

    /// <inheritdoc />
    public async Task SaveAsync(string manifestPath, ConduitState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        var path = GetStateFilePath(manifestPath);
        var dir = Path.GetDirectoryName(path)
                  ?? throw new InvalidOperationException("State path has no parent directory.");

        // Snapshot under the lock so the write can be done outside of it.
        ConduitState snapshot;
        lock (_gate)
        {
            snapshot = new ConduitState
            {
                Version = state.Version,
                Entries = new Dictionary<string, EntryState>(state.Entries, StringComparer.OrdinalIgnoreCase),
            };
        }

        Directory.CreateDirectory(dir);
        var tempPath = Path.Combine(dir, $".{StateFileName}.tmp-{Guid.NewGuid():N}");

        try
        {
            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, snapshot, JsonOptions, cancellationToken).ConfigureAwait(false);
                await stream.WriteAsync("\n"u8.ToArray(), cancellationToken).ConfigureAwait(false);
            }

            // Atomic rename. Overwrites the destination if it exists.
            File.Move(tempPath, path, overwrite: true);
        }
        catch
        {
            try { if (File.Exists(tempPath)) { File.Delete(tempPath); } } catch { /* best-effort */ }
            throw;
        }
    }
}
