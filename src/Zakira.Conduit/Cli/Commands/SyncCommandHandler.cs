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
    private readonly ConsoleStyle _style;
    private readonly ILogger<SyncCommandHandler> _logger;

    public SyncCommandHandler(IManifestLocator locator, IManifestLoader loader, IConduitSynchronizer synchronizer, ConsoleStyle style, ILogger<SyncCommandHandler> logger)
    {
        _locator = locator;
        _loader = loader;
        _synchronizer = synchronizer;
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
        return report.ExitCode;
    }
}
