using Zakira.Conduit.Manifest;

namespace Zakira.Conduit.Synchronization;

/// <summary>
///     Per-target outcome for an entry.
/// </summary>
public sealed record SyncTargetResult(string TargetPath, bool Succeeded, int FilesWritten, string? Error);

/// <summary>
///     Per-entry outcome of a sync run.
/// </summary>
public sealed record SyncEntryResult(
    ConduitEntry Entry,
    bool Skipped,
    bool Succeeded,
    string? ResolvedRef,
    IReadOnlyList<SyncTargetResult> Targets,
    string? Error,
    TimeSpan Elapsed);

/// <summary>
///     Aggregate outcome of a sync run.
/// </summary>
public sealed record SyncReport(IReadOnlyList<SyncEntryResult> Entries, TimeSpan Elapsed, bool DryRun)
{
    /// <summary>
    ///     True when every non-skipped entry succeeded against every target.
    /// </summary>
    public bool Succeeded =>
        Entries.All(e => e.Skipped || (e.Succeeded && e.Targets.All(t => t.Succeeded)));

    /// <summary>
    ///     The exit code that the CLI should return (0 on full success, 1 otherwise).
    /// </summary>
    public int ExitCode => Succeeded ? 0 : 1;
}
