namespace Zakira.Conduit.Synchronization;

/// <summary>
///     Reads, mutates, and atomically writes the <c>.conduit-state.json</c>
///     file that lives next to a manifest. Implementations must be
///     thread-safe for concurrent <see cref="UpdateEntry"/> calls because
///     the synchronizer may process multiple entries in parallel.
/// </summary>
public interface IConduitStateStore
{
    /// <summary>
    ///     Loads the state for the manifest at <paramref name="manifestPath"/>.
    ///     Returns an empty state when no file exists or it is malformed.
    /// </summary>
    Task<ConduitState> LoadAsync(string manifestPath, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Looks up the entry by name from the supplied in-memory state.
    ///     Returns <see langword="null"/> when no record exists.
    /// </summary>
    EntryState? GetEntry(ConduitState state, string entryName);

    /// <summary>
    ///     Replaces the entry record in-memory. Does not touch disk; pair
    ///     with <see cref="SaveAsync"/> to persist.
    /// </summary>
    void UpdateEntry(ConduitState state, string entryName, EntryState newRecord);

    /// <summary>
    ///     Removes the entry record from the in-memory state.
    /// </summary>
    void RemoveEntry(ConduitState state, string entryName);

    /// <summary>
    ///     Persists the state to disk next to <paramref name="manifestPath"/>.
    /// </summary>
    Task SaveAsync(string manifestPath, ConduitState state, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Returns the absolute path of the state file for a given manifest.
    /// </summary>
    string GetStateFilePath(string manifestPath);
}
