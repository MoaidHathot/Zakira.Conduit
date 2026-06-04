using Zakira.Conduit.Manifest;

namespace Zakira.Conduit.Synchronization;

/// <summary>
///     Identifies and removes destination directories that no longer
///     correspond to any live manifest entry &mdash; i.e. directories that
///     were written by a previous sync but whose owning entry has since been
///     removed from the manifest. Per-entry "live" destinations of the
///     <i>current</i> manifest are computed from the same name/alias rules
///     the synchronizer uses, so the cleaner never deletes something a
///     subsequent sync would re-create.
/// </summary>
public interface IOrphanCleaner
{
    /// <summary>
    ///     Inspects <paramref name="state"/> against <paramref name="manifest"/>
    ///     and removes (or, on dry-runs, simulates removing) every recorded
    ///     destination that no longer belongs to a live entry. State is
    ///     persisted via the registered <see cref="IConduitStateStore"/> when
    ///     <paramref name="dryRun"/> is <see langword="false"/>.
    /// </summary>
    Task<OrphanCleanupReport> CleanAsync(
        ConduitManifest manifest,
        string manifestPath,
        ConduitState state,
        bool dryRun,
        CancellationToken cancellationToken = default);
}
