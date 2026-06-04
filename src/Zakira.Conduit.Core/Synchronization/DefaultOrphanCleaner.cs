using Microsoft.Extensions.Logging;
using Zakira.Conduit.Manifest;
using Zakira.Conduit.Paths;
using Zakira.Conduit.Strategies;

namespace Zakira.Conduit.Synchronization;

/// <summary>
///     Default <see cref="IOrphanCleaner"/>. Walks the in-memory state, finds
///     entry names no longer present in the supplied manifest, and removes
///     their recorded destination directories &mdash; <i>unless</i> a live
///     entry now writes to the same directory, in which case the cleaner
///     refuses and reports a collision instead.
/// </summary>
public sealed class DefaultOrphanCleaner : IOrphanCleaner
{
    private readonly IConduitStateStore _stateStore;
    private readonly IPathResolver _pathResolver;
    private readonly IPlanStrategyRegistry _strategies;
    private readonly ILogger<DefaultOrphanCleaner> _logger;

    public DefaultOrphanCleaner(
        IConduitStateStore stateStore,
        IPathResolver pathResolver,
        IPlanStrategyRegistry strategies,
        ILogger<DefaultOrphanCleaner> logger)
    {
        ArgumentNullException.ThrowIfNull(stateStore);
        ArgumentNullException.ThrowIfNull(pathResolver);
        ArgumentNullException.ThrowIfNull(strategies);
        ArgumentNullException.ThrowIfNull(logger);
        _stateStore = stateStore;
        _pathResolver = pathResolver;
        _strategies = strategies;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<OrphanCleanupReport> CleanAsync(
        ConduitManifest manifest,
        string manifestPath,
        ConduitState state,
        bool dryRun,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);

        var manifestDir = Path.GetDirectoryName(Path.GetFullPath(manifestPath)) ?? Directory.GetCurrentDirectory();
        var pathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var strategiesConfig = StrategyConfigSnapshotBuilder.Build(manifest);

        // 1. Build the set of "live" destination directories the current
        // manifest produces. For strategies that enumerate statically (wrap,
        // flat) we use their static destinations. For strategies that don't
        // (skills, expand) we trust the previous-sync state.Targets so a
        // surviving entry's existing destinations don't get pulled out from
        // under it on rename of an unrelated entry.
        var liveDestinations = new Dictionary<string, string>(OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal);
        var liveEntryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in manifest.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Name))
            {
                continue;
            }

            liveEntryNames.Add(entry.Name);

            foreach (var dest in EnumerateLiveDestinations(entry, manifestDir, state, strategiesConfig))
            {
                liveDestinations[NormaliseFullPath(dest)] = entry.Name;
            }
        }

        // 2. Walk state entries; collect orphans (names absent from manifest)
        // and per-target outcomes for each. Per-target deletion is best-effort:
        // a failure on one target shouldn't abort cleanup for the rest.
        var results = new List<OrphanCleanupResult>();
        var entryNamesToRemove = new List<string>();

        foreach (var (name, record) in state.Entries.OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (liveEntryNames.Contains(name))
            {
                continue;
            }

            foreach (var rawTarget in record.Targets)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(rawTarget))
                {
                    continue;
                }

                var fullTarget = NormaliseFullPath(rawTarget);

                if (liveDestinations.TryGetValue(fullTarget, out var liveOwner))
                {
                    results.Add(new OrphanCleanupResult(
                        EntryName: name,
                        Target: rawTarget,
                        Action: OrphanCleanupAction.SkippedOwnedByLiveEntry,
                        Message: $"directory is now owned by live entry '{liveOwner}'."));
                    continue;
                }

                if (!Directory.Exists(rawTarget))
                {
                    results.Add(new OrphanCleanupResult(
                        EntryName: name,
                        Target: rawTarget,
                        Action: OrphanCleanupAction.AlreadyGone));
                    continue;
                }

                if (dryRun)
                {
                    results.Add(new OrphanCleanupResult(
                        EntryName: name,
                        Target: rawTarget,
                        Action: OrphanCleanupAction.Removed,
                        Message: "(dry-run: not actually deleted)"));
                    continue;
                }

                try
                {
                    Directory.Delete(rawTarget, recursive: true);
                    results.Add(new OrphanCleanupResult(
                        EntryName: name,
                        Target: rawTarget,
                        Action: OrphanCleanupAction.Removed));
                    _logger.LogInformation("Removed orphan directory '{Target}' (entry '{Name}' is no longer in the manifest).", rawTarget, name);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    results.Add(new OrphanCleanupResult(
                        EntryName: name,
                        Target: rawTarget,
                        Action: OrphanCleanupAction.Failed,
                        Message: ex.Message));
                    _logger.LogWarning(ex, "Failed to delete orphan directory '{Target}' for entry '{Name}'.", rawTarget, name);
                }
            }

            entryNamesToRemove.Add(name);
        }

        // 3. Persist state changes (skip on dry-run). Only prune entries
        // whose target deletions all succeeded OR were already-gone OR were
        // skipped because a live entry now owns the dir; leave entries with
        // hard failures in place so the user can re-attempt cleanup.
        var entriesPruned = 0;
        if (!dryRun && entryNamesToRemove.Count > 0)
        {
            foreach (var name in entryNamesToRemove)
            {
                var perEntry = results.Where(r =>
                        string.Equals(r.EntryName, name, StringComparison.OrdinalIgnoreCase))
                    .ToArray();

                if (perEntry.Any(r => r.Action == OrphanCleanupAction.Failed))
                {
                    continue;
                }

                _stateStore.RemoveEntry(state, name);
                entriesPruned++;
            }

            try
            {
                await _stateStore.SaveAsync(manifestPath, state, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Cleanup completed but persisting the updated state failed.");
            }
        }

        // Note: pathComparison is currently only used implicitly via the
        // case-insensitive dictionaries above; reserved here for future
        // per-target string comparisons if needed.
        _ = pathComparison;

        return new OrphanCleanupReport(results, dryRun)
        {
            EntriesPruned = entriesPruned,
        };
    }

    /// <summary>
    ///     Returns the destination directories <paramref name="entry"/> claims
    ///     as "mine" right now. Strategy-aware:
    ///     <list type="bullet">
    ///         <item><description>
    ///             Strategies that enumerate statically (wrap, flat) report
    ///             every dir a fresh sync would produce.
    ///         </description></item>
    ///         <item><description>
    ///             Strategies that depend on fetched content (skills, expand)
    ///             return an empty static list; we fall back to the entry's
    ///             previous-sync state targets so live destinations from the
    ///             last successful run are still claimed.
    ///         </description></item>
    ///     </list>
    /// </summary>
    private IEnumerable<string> EnumerateLiveDestinations(
        ConduitEntry entry,
        string manifestDir,
        ConduitState state,
        StrategyConfigSnapshot strategiesConfig)
    {
        if (entry.Targets is null || entry.Targets.Count == 0)
        {
            yield break;
        }

        IPlanStrategy strategy;
        try
        {
            strategy = _strategies.Resolve(entry.Strategy);
        }
        catch (UnknownStrategyException)
        {
            // Validator already surfaces this; treat as no static claims.
            yield break;
        }

        var ctx = new StaticPlanContext(entry, manifestDir, _pathResolver, strategiesConfig);
        IReadOnlyList<string> staticDests;
        try
        {
            staticDests = strategy.EnumerateStaticDestinations(ctx);
        }
        catch
        {
            staticDests = Array.Empty<string>();
        }

        if (staticDests.Count > 0)
        {
            foreach (var d in staticDests)
            {
                yield return d;
            }

            yield break;
        }

        // Content-dependent strategy: claim whatever the previous successful
        // sync recorded under this entry's name in state.
        if (!string.IsNullOrWhiteSpace(entry.Name) &&
            state.Entries.TryGetValue(entry.Name, out var record))
        {
            foreach (var target in record.Targets)
            {
                if (!string.IsNullOrWhiteSpace(target))
                {
                    yield return target;
                }
            }
        }
    }

    private static string NormaliseFullPath(string path)
    {
        var trimmed = path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.IsNullOrEmpty(trimmed) ? trimmed : Path.GetFullPath(trimmed);
    }
}
