using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Zakira.Conduit.Manifest;
using Zakira.Conduit.Mirroring;
using Zakira.Conduit.Paths;
using Zakira.Conduit.Sources;

namespace Zakira.Conduit.Synchronization;

/// <summary>
///     Default <see cref="IConduitSynchronizer"/>. Orchestrates the per-entry
///     pipeline: load state -> select fetcher -> fetch (skipping when state +
///     targets prove the entry is up-to-date) -> mirror each content unit to
///     each target -> persist state.
/// </summary>
public sealed class DefaultConduitSynchronizer : IConduitSynchronizer
{
    private readonly ISkillSourceFetcherRegistry _fetchers;
    private readonly IDirectoryMirror _mirror;
    private readonly IPathResolver _pathResolver;
    private readonly IConduitStateStore _stateStore;
    private readonly ILogger<DefaultConduitSynchronizer> _logger;

    public DefaultConduitSynchronizer(
        ISkillSourceFetcherRegistry fetchers,
        IDirectoryMirror mirror,
        IPathResolver pathResolver,
        IConduitStateStore stateStore,
        ILogger<DefaultConduitSynchronizer> logger)
    {
        ArgumentNullException.ThrowIfNull(fetchers);
        ArgumentNullException.ThrowIfNull(mirror);
        ArgumentNullException.ThrowIfNull(pathResolver);
        ArgumentNullException.ThrowIfNull(stateStore);
        ArgumentNullException.ThrowIfNull(logger);

        _fetchers = fetchers;
        _mirror = mirror;
        _pathResolver = pathResolver;
        _stateStore = stateStore;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<SyncReport> SyncAsync(ConduitManifest manifest, string manifestPath, SyncOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);
        ArgumentNullException.ThrowIfNull(options);

        var manifestFullPath = Path.GetFullPath(manifestPath);
        var manifestDir = Path.GetDirectoryName(manifestFullPath)
                          ?? throw new ArgumentException("Manifest path has no parent directory.", nameof(manifestPath));

        var swOverall = Stopwatch.StartNew();
        var state = await _stateStore.LoadAsync(manifestFullPath, cancellationToken).ConfigureAwait(false);

        var entryFilter = (options.EntryNames is { Count: > 0 })
            ? new HashSet<string>(options.EntryNames, StringComparer.OrdinalIgnoreCase)
            : null;

        // Resolve which entries we'll actually process versus which we'll
        // skip up-front (filtered out / disabled). Skipped entries keep their
        // original manifest order so the final report makes sense.
        var ordered = manifest.Entries
            .Select((entry, index) => (entry, index))
            .ToList();

        var preSkippedResults = new ConcurrentDictionary<int, SyncEntryResult>();
        var toProcess = new List<(ConduitEntry entry, int index)>(ordered.Count);

        foreach (var (entry, index) in ordered)
        {
            if (entryFilter is not null && !entryFilter.Contains(entry.Name))
            {
                preSkippedResults[index] = SkippedResult(entry);
                continue;
            }

            if (entry.Disabled)
            {
                _logger.LogInformation("Skipping disabled entry '{Name}'.", entry.Name);
                preSkippedResults[index] = SkippedResult(entry);
                continue;
            }

            toProcess.Add((entry, index));
        }

        var processedResults = new ConcurrentDictionary<int, SyncEntryResult>();
        var stopRequested = 0;

        var parallelism = Math.Max(1, options.MaxParallelism);
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = parallelism,
            CancellationToken = cancellationToken,
        };

        await Parallel.ForEachAsync(toProcess, parallelOptions, async (tuple, ct) =>
        {
            // StopOnFirstError: once another entry has failed, every subsequent
            // entry short-circuits to a "skipped" result so the user sees they
            // weren't processed (rather than silently disappearing).
            if (Volatile.Read(ref stopRequested) == 1)
            {
                processedResults[tuple.index] = SkippedResult(tuple.entry);
                return;
            }

            var result = await SyncEntryAsync(tuple.entry, manifestFullPath, manifestDir, options, state, ct).ConfigureAwait(false);
            processedResults[tuple.index] = result;

            if (!result.Succeeded && options.StopOnFirstError)
            {
                Interlocked.Exchange(ref stopRequested, 1);
                _logger.LogError("Aborting sync due to first error in entry '{Name}'.", tuple.entry.Name);
            }
        }).ConfigureAwait(false);

        // Merge pre-skipped + processed back into manifest order.
        var results = new List<SyncEntryResult>(ordered.Count);
        for (var i = 0; i < ordered.Count; i++)
        {
            if (preSkippedResults.TryGetValue(i, out var pre))
            {
                results.Add(pre);
            }
            else if (processedResults.TryGetValue(i, out var done))
            {
                results.Add(done);
            }
            else
            {
                // Should not happen, but never lose an entry from the report.
                results.Add(SkippedResult(ordered[i].entry));
            }
        }

        // Persist state on real runs only. Dry-runs must not mutate anything.
        if (!options.DryRun)
        {
            try
            {
                await _stateStore.SaveAsync(manifestFullPath, state, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Failed to persist conduit state file");
            }
        }

        swOverall.Stop();
        return new SyncReport(results, swOverall.Elapsed, options.DryRun);
    }

    private static SyncEntryResult SkippedResult(ConduitEntry entry) =>
        new(entry, Skipped: true, Succeeded: true, ResolvedRef: null, Targets: Array.Empty<SyncTargetResult>(), Error: null, Elapsed: TimeSpan.Zero);

    private async Task<SyncEntryResult> SyncEntryAsync(ConduitEntry entry, string manifestFullPath, string manifestDir, SyncOptions options, ConduitState state, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var previousState = _stateStore.GetEntry(state, entry.Name);

        // Pre-flight skip: applies only when we can be sure the upstream content
        // hasn't changed without going to network. Today that's commit-pinned
        // GitHub sources whose previous resolved ref matches the pin AND local
        // sources whose content fingerprint hasn't changed - in both cases the
        // target directories must still exist on disk.
        string? localSourceHash = null;
        if (!options.Force && CanShortCircuit(entry, previousState, manifestDir, out localSourceHash))
        {
            _logger.LogInformation("Skipping '{Name}': source unchanged and all targets present.", entry.Name);
            sw.Stop();
            return new SyncEntryResult(
                entry,
                Skipped: true,
                Succeeded: true,
                ResolvedRef: previousState!.ResolvedRef,
                Targets: previousState.Targets.Select(t => new SyncTargetResult(t, Succeeded: true, FilesWritten: 0, Error: null)).ToArray(),
                Error: null,
                Elapsed: sw.Elapsed);
        }

        _logger.LogInformation("Syncing entry '{Name}' from {Kind}", entry.Name, entry.Source.Kind);

        FetchedSource? fetched = null;
        var attemptedRetryWithoutEtag = false;
        try
        {
            var fetcher = _fetchers.GetFetcher(entry.Source);

            while (true)
            {
                var fetchContext = new FetchContext(manifestFullPath)
                {
                    PreviousEtag = (options.Force || attemptedRetryWithoutEtag) ? null : previousState?.Etag,
                };

                if (fetched is not null)
                {
                    await fetched.DisposeAsync().ConfigureAwait(false);
                }

                fetched = await fetcher.FetchAsync(entry.Source, fetchContext, cancellationToken).ConfigureAwait(false);

                if (!fetched.NotModified)
                {
                    break;
                }

                // 304 path. Verify the targets still exist; if so, treat as up-to-date.
                var targetsResolved = entry.Targets
                    .Select(targetSpec =>
                    {
                        var resolvedParent = _pathResolver.Resolve(targetSpec.Path, manifestDir);
                        var destName = targetSpec.As ?? entry.Name;
                        return Path.Combine(resolvedParent, destName);
                    })
                    .ToList();

                if (targetsResolved.All(Directory.Exists))
                {
                    _logger.LogInformation("304 Not Modified for '{Name}'; targets are present.", entry.Name);

                    if (!options.DryRun)
                    {
                        _stateStore.UpdateEntry(state, entry.Name, new EntryState
                        {
                            ResolvedRef = previousState?.ResolvedRef ?? fetched.ResolvedRef,
                            Etag = fetched.Etag ?? previousState?.Etag,
                            LastSyncUtc = DateTimeOffset.UtcNow,
                            Targets = previousState?.Targets ?? targetsResolved,
                            SourceContentHash = previousState?.SourceContentHash,
                        });
                    }

                    sw.Stop();
                    return new SyncEntryResult(
                        entry,
                        Skipped: true,
                        Succeeded: true,
                        ResolvedRef: previousState?.ResolvedRef ?? fetched.ResolvedRef,
                        Targets: targetsResolved.Select(t => new SyncTargetResult(t, Succeeded: true, FilesWritten: 0, Error: null)).ToArray(),
                        Error: null,
                        Elapsed: sw.Elapsed);
                }

                // Cache said "unchanged" but a target was deleted manually.
                // Invalidate and retry once without the ETag hint so the server
                // sends the full body.
                if (attemptedRetryWithoutEtag)
                {
                    _logger.LogWarning("'{Name}': source unchanged but one or more targets remain missing after retry.", entry.Name);
                    sw.Stop();
                    return new SyncEntryResult(
                        entry,
                        Skipped: false,
                        Succeeded: false,
                        ResolvedRef: fetched.ResolvedRef ?? previousState?.ResolvedRef,
                        Targets: targetsResolved.Select(t => new SyncTargetResult(t, Directory.Exists(t), 0, Directory.Exists(t) ? null : "target missing")).ToArray(),
                        Error: "Source replied 'unchanged' but one or more targets are missing.",
                        Elapsed: sw.Elapsed);
                }

                _logger.LogInformation("'{Name}': server replied 304 but a target is missing; retrying without ETag.", entry.Name);
                attemptedRetryWithoutEtag = true;
                // Loop to re-fetch without the etag hint.
            }

            // Per design: when there is exactly one content unit the entry name
            // is the destination sub-directory; with two or more, each unit's
            // suggested name becomes the destination and the entry name is
            // metadata only (logs / --entry filtering).
            var singleUnit = fetched.Contents.Count == 1;

            // capacity = N units * M targets
            var targetResults = new List<SyncTargetResult>(fetched.Contents.Count * entry.Targets.Count);

            foreach (var unit in fetched.Contents)
            {
                var fetchedFull = Path.GetFullPath(unit.ContentDirectory);

                foreach (var targetSpec in entry.Targets)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Per design:
                    //   - single unit: destination = target.As (per-target alias) ?? entry.Name
                    //   - multi unit:  destination = unit.SuggestedDestinationName (path basename / alias)
                    string destName;
                    if (singleUnit)
                    {
                        destName = targetSpec.As ?? entry.Name;
                    }
                    else
                    {
                        destName = unit.SuggestedDestinationName
                                   ?? throw new InvalidOperationException(
                                       $"Source '{entry.Source.Kind}' returned multiple content units but failed to suggest a destination name for one of them.");
                    }

                    var resolvedParent = _pathResolver.Resolve(targetSpec.Path, manifestDir);
                    var resolvedTarget = Path.Combine(resolvedParent, destName);

                    try
                    {
                        GuardAgainstOverlap(fetchedFull, resolvedTarget);

                        if (options.DryRun)
                        {
                            _logger.LogInformation("[dry-run] Would mirror '{Source}' to '{Target}'", unit.ContentDirectory, resolvedTarget);
                            var fileCount = Directory.Exists(unit.ContentDirectory)
                                ? Directory.EnumerateFiles(unit.ContentDirectory, "*", SearchOption.AllDirectories).Count()
                                : 0;
                            targetResults.Add(new SyncTargetResult(resolvedTarget, Succeeded: true, FilesWritten: fileCount, Error: null));
                        }
                        else
                        {
                            var written = await _mirror.MirrorAsync(unit.ContentDirectory, resolvedTarget, cancellationToken).ConfigureAwait(false);
                            targetResults.Add(new SyncTargetResult(resolvedTarget, Succeeded: true, FilesWritten: written, Error: null));
                        }
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogError(ex, "Failed to mirror entry '{Name}' into target '{Target}'", entry.Name, resolvedTarget);
                        targetResults.Add(new SyncTargetResult(resolvedTarget, Succeeded: false, FilesWritten: 0, Error: ex.Message));
                    }
                }
            }

            var allOk = targetResults.All(t => t.Succeeded);

            // Persist updated state for the entry on real runs only.
            if (allOk && !options.DryRun)
            {
                _stateStore.UpdateEntry(state, entry.Name, new EntryState
                {
                    ResolvedRef = fetched.ResolvedRef,
                    Etag = fetched.Etag,
                    LastSyncUtc = DateTimeOffset.UtcNow,
                    Targets = targetResults.Select(t => t.TargetPath).Distinct(StringComparer.Ordinal).ToArray(),
                    // Local sources: record the just-computed content hash if we
                    // calculated one in CanShortCircuit, otherwise compute it now.
                    SourceContentHash = entry.Source is LocalDirectorySkillSource local
                        ? (localSourceHash ?? ComputeLocalSourceHash(local, manifestDir))
                        : null,
                });
            }

            sw.Stop();
            return new SyncEntryResult(entry, Skipped: false, Succeeded: allOk, ResolvedRef: fetched.ResolvedRef, Targets: targetResults, Error: null, Elapsed: sw.Elapsed);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            sw.Stop();
            _logger.LogError(ex, "Failed to sync entry '{Name}'", entry.Name);
            return new SyncEntryResult(entry, Skipped: false, Succeeded: false, ResolvedRef: null, Targets: Array.Empty<SyncTargetResult>(), Error: ex.Message, Elapsed: sw.Elapsed);
        }
        finally
        {
            if (fetched is not null)
            {
                await fetched.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    ///     Returns <see langword="true"/> when the entry is provably up-to-date
    ///     without any network IO. Supports:
    ///     <list type="bullet">
    ///         <item><description>Commit-pinned GitHub entries whose state matches the pin.</description></item>
    ///         <item><description>Local sources whose content fingerprint hasn't changed.</description></item>
    ///     </list>
    ///     In both cases the recorded target directories must still exist.
    /// </summary>
    private bool CanShortCircuit(ConduitEntry entry, EntryState? previousState, string manifestDir, out string? currentSourceHash)
    {
        currentSourceHash = null;

        if (previousState is null)
        {
            return false;
        }

        switch (entry.Source)
        {
            case GitHubSkillSource gh when !string.IsNullOrWhiteSpace(gh.Commit):
                if (!string.Equals(previousState.ResolvedRef, gh.Commit, StringComparison.Ordinal))
                {
                    return false;
                }

                return GithubExpectedTargetsExist(entry, gh, manifestDir, previousState);

            case AzdoSkillSource azdo when !string.IsNullOrWhiteSpace(azdo.Commit):
                if (!string.Equals(previousState.ResolvedRef, azdo.Commit, StringComparison.Ordinal))
                {
                    return false;
                }

                if (azdo.EffectivePaths.Count > 1)
                {
                    return false;
                }

                return EveryConfiguredTargetExists(entry, manifestDir, previousState);

            case LocalDirectorySkillSource local:
                currentSourceHash = ComputeLocalSourceHash(local, manifestDir);
                if (currentSourceHash is null)
                {
                    // Source directory missing; let the fetcher raise the proper error.
                    return false;
                }

                if (!string.Equals(previousState.SourceContentHash, currentSourceHash, StringComparison.Ordinal))
                {
                    return false;
                }

                return LocalExpectedTargetsExist(entry, local, manifestDir, previousState);

            default:
                return false;
        }
    }

    private bool GithubExpectedTargetsExist(ConduitEntry entry, GitHubSkillSource gh, string manifestDir, EntryState previousState)
    {
        // Multi-path sources can't be short-circuited cheaply because per-unit
        // destinations depend on basenames the fetcher would compute.
        if (gh.EffectivePaths.Count > 1)
        {
            return false;
        }

        return EveryConfiguredTargetExists(entry, manifestDir, previousState);
    }

    private bool LocalExpectedTargetsExist(ConduitEntry entry, LocalDirectorySkillSource local, string manifestDir, EntryState previousState)
    {
        if (local.EffectivePaths.Count > 1)
        {
            // Multi-path local: destinations are basenames; the simple "entry.name"
            // mapping doesn't apply. Skip the optimisation conservatively.
            return false;
        }

        return EveryConfiguredTargetExists(entry, manifestDir, previousState);
    }

    private bool EveryConfiguredTargetExists(ConduitEntry entry, string manifestDir, EntryState previousState)
    {
        var sameComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        var expectedTargets = entry.Targets
            .Select(targetSpec => Path.Combine(_pathResolver.Resolve(targetSpec.Path, manifestDir), targetSpec.As ?? entry.Name))
            .ToList();

        return expectedTargets.All(t =>
            Directory.Exists(t) &&
            previousState.Targets.Any(saved => string.Equals(Path.GetFullPath(saved), Path.GetFullPath(t), sameComparison)));
    }

    /// <summary>
    ///     Walks every <see cref="LocalDirectorySkillSource"/> path and produces
    ///     a deterministic hash of <c>(relative path, size, last-write-time)</c>
    ///     for every file it contains. Returns <see langword="null"/> when any
    ///     resolved source directory is missing.
    /// </summary>
    private string? ComputeLocalSourceHash(LocalDirectorySkillSource source, string manifestDir)
    {
        var sb = new StringBuilder();

        foreach (var spec in source.EffectivePaths.OrderBy(p => p.Path, StringComparer.Ordinal))
        {
            var resolved = _pathResolver.Resolve(spec.Path, manifestDir);
            if (!Directory.Exists(resolved))
            {
                return null;
            }

            sb.Append('[').Append(spec.Path).Append('|').Append(spec.As ?? string.Empty).Append("]\n");

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(resolved, "*", SearchOption.AllDirectories);
            }
            catch (IOException)
            {
                return null;
            }

            foreach (var file in files.OrderBy(f => f, StringComparer.Ordinal))
            {
                FileInfo info;
                try
                {
                    info = new FileInfo(file);
                }
                catch (IOException)
                {
                    return null;
                }

                var rel = Path.GetRelativePath(resolved, file).Replace('\\', '/');
                sb.Append(rel).Append('|').Append(info.Length).Append('|').Append(info.LastWriteTimeUtc.Ticks).Append('\n');
            }
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(bytes);
    }

    /// <summary>
    ///     Refuses to proceed when the source and target paths overlap. Without
    ///     this guard a misconfigured local source could (a) copy a previous
    ///     run's output back into itself, or (b) recurse into the target during
    ///     the directory enumeration step.
    /// </summary>
    private static void GuardAgainstOverlap(string fetchedFullPath, string targetPath)
    {
        var targetFull = Path.GetFullPath(targetPath);
        var sep = Path.DirectorySeparatorChar;

        var sameComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        if (string.Equals(fetchedFullPath, targetFull, sameComparison) ||
            fetchedFullPath.StartsWith(targetFull + sep, sameComparison) ||
            targetFull.StartsWith(fetchedFullPath + sep, sameComparison))
        {
            throw new InvalidOperationException(
                $"Source directory '{fetchedFullPath}' overlaps with target directory '{targetFull}'. " +
                "Choose a target that is not inside, nor a parent of, the source.");
        }
    }
}
