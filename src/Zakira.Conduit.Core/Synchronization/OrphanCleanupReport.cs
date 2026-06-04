namespace Zakira.Conduit.Synchronization;

/// <summary>
///     Per-target outcome of an <see cref="IOrphanCleaner.CleanAsync"/>
///     invocation. One result per inspected (entry, target) pair.
/// </summary>
public sealed record OrphanCleanupResult(
    string EntryName,
    string Target,
    OrphanCleanupAction Action,
    string? Message = null);

/// <summary>Possible outcomes for a single orphan target.</summary>
public enum OrphanCleanupAction
{
    /// <summary>Directory existed and was deleted (or, in dry-run, would be).</summary>
    Removed,

    /// <summary>Directory was recorded but no longer exists on disk; state was simply pruned.</summary>
    AlreadyGone,

    /// <summary>
    ///     Directory exists but is still claimed by a live entry; the cleaner
    ///     refuses to delete it. <see cref="OrphanCleanupResult.Message"/>
    ///     contains the colliding live entry name.
    /// </summary>
    SkippedOwnedByLiveEntry,

    /// <summary>Delete attempt failed; <see cref="OrphanCleanupResult.Message"/> contains the OS error.</summary>
    Failed,
}

/// <summary>The aggregated outcome of one cleanup run.</summary>
public sealed record OrphanCleanupReport(
    IReadOnlyList<OrphanCleanupResult> Results,
    bool DryRun)
{
    /// <summary>Number of entries fully evicted from the state file.</summary>
    public int EntriesPruned { get; init; }
}
