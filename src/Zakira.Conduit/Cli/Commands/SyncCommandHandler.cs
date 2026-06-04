using Microsoft.Extensions.Logging;
using Zakira.Conduit.Manifest;
using Zakira.Conduit.Synchronization;

namespace Zakira.Conduit.Cli.Commands;

/// <summary>
///     Implements the <c>sync</c> command.
/// </summary>
internal sealed class SyncCommandHandler
{
    private readonly IManifestLocator _locator;
    private readonly IManifestLoader _loader;
    private readonly IConduitSynchronizer _synchronizer;
    private readonly IConduitStateStore _stateStore;
    private readonly IOrphanCleaner _cleaner;
    private readonly ConsoleStyle _style;
    private readonly ILogger<SyncCommandHandler> _logger;

    public SyncCommandHandler(
        IManifestLocator locator,
        IManifestLoader loader,
        IConduitSynchronizer synchronizer,
        IConduitStateStore stateStore,
        IOrphanCleaner cleaner,
        ConsoleStyle style,
        ILogger<SyncCommandHandler> logger)
    {
        _locator = locator;
        _loader = loader;
        _synchronizer = synchronizer;
        _stateStore = stateStore;
        _cleaner = cleaner;
        _style = style;
        _logger = logger;
    }

    public async Task<int> InvokeAsync(
        string? manifest,
        IReadOnlyList<string> entries,
        bool dryRun,
        bool stopOnFirstError,
        bool force,
        int maxParallelism,
        bool prune,
        bool pruneYes,
        OutputFormat output,
        CancellationToken cancellationToken)
    {
        string manifestPath;
        ConduitManifest model;
        try
        {
            manifestPath = _locator.Locate(manifest);
            _logger.LogInformation("Using manifest: {Path}", manifestPath);
            model = await _loader.LoadAsync(manifestPath, cancellationToken).ConfigureAwait(false);
        }
        catch (ManifestException ex)
        {
            ErrorRenderer.RenderManifestError(ex, output);
            return 2;
        }

        var options = new SyncOptions
        {
            EntryNames = entries.Count == 0 ? null : entries,
            DryRun = dryRun,
            StopOnFirstError = stopOnFirstError,
            Force = force,
            MaxParallelism = maxParallelism <= 0 ? 4 : maxParallelism,
        };

        var report = await _synchronizer.SyncAsync(model, manifestPath, options, cancellationToken).ConfigureAwait(false);
        ReportRenderer.Render(report, output, _style);

        if (prune && report.ExitCode == 0)
        {
            await RunPostSyncPruneAsync(model, manifestPath, dryRun, pruneYes, output, cancellationToken).ConfigureAwait(false);
        }

        return report.ExitCode;
    }

    private async Task RunPostSyncPruneAsync(
        ConduitManifest model,
        string manifestPath,
        bool dryRun,
        bool pruneYes,
        OutputFormat output,
        CancellationToken cancellationToken)
    {
        if (!pruneYes && output == OutputFormat.Json)
        {
            _logger.LogWarning("--prune requested in JSON mode but --prune-yes was not supplied; skipping the cleanup pass.");
            return;
        }

        if (!pruneYes && Console.IsInputRedirected)
        {
            _logger.LogWarning("--prune requested but stdin is not a TTY; pass --prune-yes to confirm. Skipping the cleanup pass.");
            return;
        }

        var state = await _stateStore.LoadAsync(manifestPath, cancellationToken).ConfigureAwait(false);
        var preview = await _cleaner.CleanAsync(model, manifestPath, state, dryRun: true, cancellationToken).ConfigureAwait(false);

        var wouldDelete = preview.Results.Count(r => r.Action == OrphanCleanupAction.Removed);
        if (wouldDelete == 0)
        {
            return;
        }

        if (!pruneYes && !dryRun)
        {
            Console.WriteLine();
            Console.WriteLine($"{_style.Bold("--prune")} would remove {wouldDelete} orphan {(wouldDelete == 1 ? "directory" : "directories")}:");
            foreach (var r in preview.Results.Where(r => r.Action == OrphanCleanupAction.Removed))
            {
                Console.WriteLine($"  - {r.EntryName}: {r.Target}");
            }

            Console.Write($"\n{_style.Bold("Proceed?")} [y/N]: ");
            var answer = Console.ReadLine();
            if (answer is null || !string.Equals(answer.Trim(), "y", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("Cleanup skipped; nothing deleted.");
                return;
            }
        }

        var actual = await _cleaner.CleanAsync(model, manifestPath, state, dryRun, cancellationToken).ConfigureAwait(false);
        var removed = actual.Results.Count(r => r.Action == OrphanCleanupAction.Removed);
        var failed = actual.Results.Count(r => r.Action == OrphanCleanupAction.Failed);

        if (output != OutputFormat.Json)
        {
            Console.WriteLine();
            Console.WriteLine(dryRun
                ? $"{_style.Dim("[dry-run]")} --prune would remove {removed} orphan {(removed == 1 ? "directory" : "directories")}."
                : $"--prune removed {removed} orphan {(removed == 1 ? "directory" : "directories")} ({failed} failed, {actual.EntriesPruned} state entries pruned).");
        }
    }
}
