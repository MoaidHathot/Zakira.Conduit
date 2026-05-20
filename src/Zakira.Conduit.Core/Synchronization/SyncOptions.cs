namespace Zakira.Conduit.Synchronization;

/// <summary>
///     Options controlling a single sync run.
/// </summary>
public sealed record SyncOptions
{
    /// <summary>
    ///     When set, only the entries whose names appear here are processed.
    ///     Case-insensitive. When <see langword="null"/> or empty, all enabled
    ///     entries are processed.
    /// </summary>
    public IReadOnlyCollection<string>? EntryNames { get; init; }

    /// <summary>
    ///     When <see langword="true"/>, sources are fetched and validated but
    ///     no changes are written to target directories.
    /// </summary>
    public bool DryRun { get; init; }

    /// <summary>
    ///     When <see langword="true"/>, the first entry failure aborts the run.
    ///     When <see langword="false"/> (default), remaining entries are still
    ///     attempted and the overall result is degraded.
    /// </summary>
    public bool StopOnFirstError { get; init; }

    /// <summary>
    ///     When <see langword="true"/>, ignores any cached state: every entry
    ///     is re-fetched and re-mirrored even if the state file says it's
    ///     already up-to-date. Useful for verifying targets after manual edits.
    /// </summary>
    public bool Force { get; init; }

    /// <summary>
    ///     Maximum number of entries processed concurrently. Defaults to <c>4</c>.
    ///     Set to <c>1</c> to force sequential execution.
    /// </summary>
    public int MaxParallelism { get; init; } = 4;
}
