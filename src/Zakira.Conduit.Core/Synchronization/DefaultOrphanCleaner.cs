using Microsoft.Extensions.Logging;
using Zakira.Conduit.Manifest;
using Zakira.Conduit.Paths;

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
    private readonly ILogger<DefaultOrphanCleaner> _logger;

    public DefaultOrphanCleaner(
        IConduitStateStore stateStore,
        IPathResolver pathResolver,
        ILogger<DefaultOrphanCleaner> logger)
    {
        ArgumentNullException.ThrowIfNull(stateStore);
        ArgumentNullException.ThrowIfNull(pathResolver);
        ArgumentNullException.ThrowIfNull(logger);
        _stateStore = stateStore;
        _pathResolver = pathResolver;
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

        // 1. Build the set of "live" destination directories the current
        // manifest produces. The cleaner refuses to delete any directory in
        // this set, even if a stale state row also claims it.
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
            foreach (var dest in EnumerateLiveDestinations(entry, manifestDir))
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
    ///     Computes the set of destination directories an entry would write
    ///     to on a fresh sync. Mirrors the rules in
    ///     <see cref="DefaultConduitSynchronizer"/> so the cleaner never
    ///     reports a "live" dir as orphan, nor an orphan dir as still live.
    /// </summary>
    private IEnumerable<string> EnumerateLiveDestinations(ConduitEntry entry, string manifestDir)
    {
        if (entry.Targets is null || entry.Targets.Count == 0)
        {
            yield break;
        }

        var multiUnitDestNames = entry.Source switch
        {
            GitHubSource gh when gh.EffectivePaths.Count > 1 => gh.EffectivePaths.Select(p => p.ResolvedBasename).ToList(),
            AzdoSource azdo when azdo.EffectivePaths.Count > 1 => azdo.EffectivePaths.Select(p => p.ResolvedBasename).ToList(),
            LocalDirectorySource local when local.EffectivePaths.Count > 1 => local.EffectivePaths.Select(p => p.ResolvedBasename).ToList(),
            _ => null,
        };

        foreach (var target in entry.Targets)
        {
            if (target is null || string.IsNullOrWhiteSpace(target.Path))
            {
                continue;
            }

            var resolvedParent = _pathResolver.Resolve(target.Path, manifestDir);

            if (multiUnitDestNames is not null)
            {
                foreach (var unitName in multiUnitDestNames)
                {
                    yield return Path.Combine(resolvedParent, unitName);
                }
            }
            else
            {
                var destName = string.IsNullOrWhiteSpace(target.As) ? entry.Name : target.As;
                if (!string.IsNullOrWhiteSpace(destName))
                {
                    yield return Path.Combine(resolvedParent, destName!);
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
