using Microsoft.Extensions.Logging;
using Zakira.Conduit.Manifest;
using Zakira.Conduit.Synchronization;

namespace Zakira.Conduit.Cli.Commands;

/// <summary>
///     Implements <c>conduit watch</c>: keeps the CLI alive, runs <c>sync</c>
///     once up front, then re-runs it whenever the manifest file changes on
///     disk (debounced) until Ctrl+C is pressed.
/// </summary>
internal sealed class WatchCommandHandler
{
    private readonly IManifestLocator _locator;
    private readonly IManifestLoader _loader;
    private readonly IConduitSynchronizer _synchronizer;
    private readonly ConsoleStyle _style;
    private readonly ILogger<WatchCommandHandler> _logger;

    public WatchCommandHandler(
        IManifestLocator locator,
        IManifestLoader loader,
        IConduitSynchronizer synchronizer,
        ConsoleStyle style,
        ILogger<WatchCommandHandler> logger)
    {
        _locator = locator;
        _loader = loader;
        _synchronizer = synchronizer;
        _style = style;
        _logger = logger;
    }

    public async Task<int> InvokeAsync(
        string? manifest,
        int debounceMs,
        int maxParallelism,
        OutputFormat output,
        CancellationToken cancellationToken)
    {
        string manifestPath;
        try
        {
            manifestPath = _locator.Locate(manifest);
        }
        catch (ManifestException ex)
        {
            ErrorRenderer.RenderManifestError(ex, output);
            return 2;
        }

        var directory = Path.GetDirectoryName(manifestPath)
                        ?? throw new InvalidOperationException("Manifest path has no parent directory.");
        var fileName = Path.GetFileName(manifestPath);

        Console.WriteLine(_style.Bold($"Watching {manifestPath} (Ctrl+C to stop)..."));

        // Initial pass.
        await RunSyncOnceAsync(manifestPath, maxParallelism, output, "initial sync", cancellationToken).ConfigureAwait(false);

        // FSW + debounce: coalesce burst writes from editors that do
        // create-temp / rename-over-original, plus rapid double-saves.
        using var watcher = new FileSystemWatcher(directory, fileName)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            EnableRaisingEvents = true,
        };

        var changeSignal = new SemaphoreSlim(initialCount: 0, maxCount: 1);
        FileSystemEventHandler bump = (_, _) =>
        {
            try { changeSignal.Release(); } catch (SemaphoreFullException) { /* coalesced */ }
        };
        RenamedEventHandler bumpRenamed = (_, _) =>
        {
            try { changeSignal.Release(); } catch (SemaphoreFullException) { /* coalesced */ }
        };

        watcher.Changed += bump;
        watcher.Created += bump;
        watcher.Renamed += bumpRenamed;

        var delay = TimeSpan.FromMilliseconds(Math.Max(50, debounceMs));

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                // Wait for one or more signals.
                await changeSignal.WaitAsync(cancellationToken).ConfigureAwait(false);

                // Debounce window: drain any further signals that arrive shortly after.
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                while (changeSignal.CurrentCount > 0)
                {
                    await changeSignal.WaitAsync(0, cancellationToken).ConfigureAwait(false);
                }

                await RunSyncOnceAsync(manifestPath, maxParallelism, output, "manifest changed", cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // User pressed Ctrl+C; that's a clean exit.
        }
        finally
        {
            watcher.Changed -= bump;
            watcher.Created -= bump;
            watcher.Renamed -= bumpRenamed;
        }

        Console.WriteLine(_style.Dim("watch: stopped."));
        return 0;
    }

    private async Task RunSyncOnceAsync(string manifestPath, int maxParallelism, OutputFormat output, string reason, CancellationToken cancellationToken)
    {
        Console.WriteLine(_style.Dim($"-- {DateTime.Now:HH:mm:ss}  {reason} --"));
        try
        {
            var model = await _loader.LoadAsync(manifestPath, cancellationToken).ConfigureAwait(false);
            var options = new SyncOptions { MaxParallelism = maxParallelism <= 0 ? 4 : maxParallelism };
            var report = await _synchronizer.SyncAsync(model, manifestPath, options, cancellationToken).ConfigureAwait(false);
            ReportRenderer.Render(report, output, _style);
        }
        catch (ManifestException ex)
        {
            ErrorRenderer.RenderManifestError(ex, output);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Sync run failed while watching");
        }
    }
}
