using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Zakira.Conduit.Manifest;
using Zakira.Conduit.Mirroring;
using Zakira.Conduit.Paths;
using Zakira.Conduit.Sources;

namespace Zakira.Conduit.Synchronization;

/// <summary>
///     Default <see cref="IConduitSynchronizer"/>. Orchestrates the per-entry
///     pipeline: select fetcher → fetch → mirror to each target.
/// </summary>
public sealed class DefaultConduitSynchronizer : IConduitSynchronizer
{
    private readonly ISkillSourceFetcherRegistry _fetchers;
    private readonly IDirectoryMirror _mirror;
    private readonly IPathResolver _pathResolver;
    private readonly ILogger<DefaultConduitSynchronizer> _logger;

    public DefaultConduitSynchronizer(
        ISkillSourceFetcherRegistry fetchers,
        IDirectoryMirror mirror,
        IPathResolver pathResolver,
        ILogger<DefaultConduitSynchronizer> logger)
    {
        ArgumentNullException.ThrowIfNull(fetchers);
        ArgumentNullException.ThrowIfNull(mirror);
        ArgumentNullException.ThrowIfNull(pathResolver);
        ArgumentNullException.ThrowIfNull(logger);

        _fetchers = fetchers;
        _mirror = mirror;
        _pathResolver = pathResolver;
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
        var results = new List<SyncEntryResult>(manifest.Entries.Count);

        var entryFilter = (options.EntryNames is { Count: > 0 })
            ? new HashSet<string>(options.EntryNames, StringComparer.OrdinalIgnoreCase)
            : null;

        foreach (var entry in manifest.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (entryFilter is not null && !entryFilter.Contains(entry.Name))
            {
                results.Add(new SyncEntryResult(entry, Skipped: true, Succeeded: true, ResolvedRef: null, Targets: Array.Empty<SyncTargetResult>(), Error: null, Elapsed: TimeSpan.Zero));
                continue;
            }

            if (entry.Disabled)
            {
                _logger.LogInformation("Skipping disabled entry '{Name}'.", entry.Name);
                results.Add(new SyncEntryResult(entry, Skipped: true, Succeeded: true, ResolvedRef: null, Targets: Array.Empty<SyncTargetResult>(), Error: null, Elapsed: TimeSpan.Zero));
                continue;
            }

            var result = await SyncEntryAsync(entry, manifestFullPath, manifestDir, options, cancellationToken).ConfigureAwait(false);
            results.Add(result);

            if (!result.Succeeded && options.StopOnFirstError)
            {
                _logger.LogError("Aborting sync due to first error in entry '{Name}'.", entry.Name);
                break;
            }
        }

        swOverall.Stop();
        return new SyncReport(results, swOverall.Elapsed, options.DryRun);
    }

    private async Task<SyncEntryResult> SyncEntryAsync(ConduitEntry entry, string manifestFullPath, string manifestDir, SyncOptions options, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        _logger.LogInformation("Syncing entry '{Name}' from {Kind}", entry.Name, entry.Source.Kind);

        FetchedSource? fetched = null;
        try
        {
            var fetcher = _fetchers.GetFetcher(entry.Source);
            var fetchContext = new FetchContext(manifestFullPath);
            fetched = await fetcher.FetchAsync(entry.Source, fetchContext, cancellationToken).ConfigureAwait(false);

            var fetchedFull = Path.GetFullPath(fetched.ContentDirectory);

            var targetResults = new List<SyncTargetResult>(entry.Targets.Count);
            foreach (var rawTarget in entry.Targets)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var resolvedParent = _pathResolver.Resolve(rawTarget, manifestDir);
                var resolvedTarget = Path.Combine(resolvedParent, entry.Name);

                try
                {
                    GuardAgainstOverlap(fetchedFull, resolvedTarget);

                    if (options.DryRun)
                    {
                        _logger.LogInformation("[dry-run] Would mirror '{Source}' to '{Target}'", fetched.ContentDirectory, resolvedTarget);
                        var fileCount = Directory.Exists(fetched.ContentDirectory)
                            ? Directory.EnumerateFiles(fetched.ContentDirectory, "*", SearchOption.AllDirectories).Count()
                            : 0;
                        targetResults.Add(new SyncTargetResult(resolvedTarget, Succeeded: true, FilesWritten: fileCount, Error: null));
                    }
                    else
                    {
                        var written = await _mirror.MirrorAsync(fetched.ContentDirectory, resolvedTarget, cancellationToken).ConfigureAwait(false);
                        targetResults.Add(new SyncTargetResult(resolvedTarget, Succeeded: true, FilesWritten: written, Error: null));
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Failed to mirror entry '{Name}' into target '{Target}'", entry.Name, resolvedTarget);
                    targetResults.Add(new SyncTargetResult(resolvedTarget, Succeeded: false, FilesWritten: 0, Error: ex.Message));
                }
            }

            sw.Stop();
            var allOk = targetResults.All(t => t.Succeeded);
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
