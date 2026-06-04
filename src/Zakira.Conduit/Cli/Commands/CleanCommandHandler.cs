using System.Text.Json;
using Microsoft.Extensions.Logging;
using Zakira.Conduit.Manifest;
using Zakira.Conduit.Synchronization;

namespace Zakira.Conduit.Cli.Commands;

/// <summary>
///     Implements the <c>clean</c> command. Removes destination directories
///     recorded in state whose owning entry no longer exists in the manifest.
/// </summary>
internal sealed class CleanCommandHandler
{
    private readonly IManifestLocator _locator;
    private readonly IManifestLoader _loader;
    private readonly IConduitStateStore _stateStore;
    private readonly IOrphanCleaner _cleaner;
    private readonly ConsoleStyle _style;
    private readonly ILogger<CleanCommandHandler> _logger;

    public CleanCommandHandler(
        IManifestLocator locator,
        IManifestLoader loader,
        IConduitStateStore stateStore,
        IOrphanCleaner cleaner,
        ConsoleStyle style,
        ILogger<CleanCommandHandler> logger)
    {
        _locator = locator;
        _loader = loader;
        _stateStore = stateStore;
        _cleaner = cleaner;
        _style = style;
        _logger = logger;
    }

    public async Task<int> InvokeAsync(
        string? manifest,
        bool dryRun,
        bool yes,
        OutputFormat output,
        CancellationToken cancellationToken)
    {
        string manifestPath;
        ConduitManifest manifestModel;
        try
        {
            manifestPath = _locator.Locate(manifest);
            manifestModel = await _loader.LoadAsync(manifestPath, cancellationToken).ConfigureAwait(false);
        }
        catch (ManifestException ex)
        {
            ErrorRenderer.RenderManifestError(ex, output);
            return 2;
        }

        var state = await _stateStore.LoadAsync(manifestPath, cancellationToken).ConfigureAwait(false);

        // Always start with a dry-run preview so we know what we'd touch; if
        // the user hasn't passed --yes (or we're in JSON output mode), we
        // either prompt or refuse-to-mutate based on the mode.
        var preview = await _cleaner.CleanAsync(manifestModel, manifestPath, state, dryRun: true, cancellationToken).ConfigureAwait(false);

        var wouldRemove = preview.Results
            .Count(r => r.Action is OrphanCleanupAction.Removed);

        if (wouldRemove == 0 && preview.Results.Count == 0)
        {
            if (output == OutputFormat.Json)
            {
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    ok = true,
                    manifest = manifestPath,
                    dryRun,
                    entriesPruned = 0,
                    results = Array.Empty<object>(),
                }, ManifestJson.WriteOptions));
            }
            else
            {
                Console.WriteLine("Nothing to clean: no orphan entries found in the state file.");
            }
            return 0;
        }

        // Dry-run: short-circuit reporting preview, never touches FS.
        if (dryRun)
        {
            RenderReport(preview, manifestPath, output, dryRun: true);
            return 0;
        }

        // Non-dry-run: confirm with user unless --yes, or we're in JSON mode
        // (which has no human to prompt; require explicit --yes for safety).
        if (!yes)
        {
            if (output == OutputFormat.Json)
            {
                Console.Error.WriteLine("error: 'conduit clean' refuses to delete without confirmation in JSON mode; pass --yes to acknowledge.");
                return 2;
            }

            if (Console.IsInputRedirected)
            {
                Console.Error.WriteLine("error: 'conduit clean' would delete directories; pass --yes to confirm (stdin is not a TTY so I can't prompt).");
                return 2;
            }

            RenderReport(preview, manifestPath, output, dryRun: true);
            Console.Write($"\n{_style.Bold("Proceed with deletion?")} [y/N]: ");
            var answer = Console.ReadLine();
            if (answer is null || !string.Equals(answer.Trim(), "y", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("Aborted; nothing deleted.");
                return 0;
            }
        }

        var actual = await _cleaner.CleanAsync(manifestModel, manifestPath, state, dryRun: false, cancellationToken).ConfigureAwait(false);
        RenderReport(actual, manifestPath, output, dryRun: false);

        var failures = actual.Results.Count(r => r.Action == OrphanCleanupAction.Failed);
        return failures > 0 ? 1 : 0;
    }

    private void RenderReport(OrphanCleanupReport report, string manifestPath, OutputFormat output, bool dryRun)
    {
        if (output == OutputFormat.Json)
        {
            var dto = new
            {
                ok = report.Results.All(r => r.Action != OrphanCleanupAction.Failed),
                manifest = manifestPath,
                dryRun,
                entriesPruned = report.EntriesPruned,
                results = report.Results.Select(r => new
                {
                    entry = r.EntryName,
                    target = r.Target,
                    action = r.Action.ToString().ToLowerInvariant(),
                    message = r.Message,
                }),
            };
            Console.WriteLine(JsonSerializer.Serialize(dto, ManifestJson.WriteOptions));
            return;
        }

        Console.WriteLine();
        Console.WriteLine(_style.Bold(dryRun ? "Conduit clean dry-run" : "Conduit clean report"));
        Console.WriteLine(new string('-', 60));

        if (report.Results.Count == 0)
        {
            Console.WriteLine("  (no orphan entries)");
            return;
        }

        foreach (var r in report.Results)
        {
            var label = r.Action switch
            {
                OrphanCleanupAction.Removed => dryRun ? _style.Dim("- (would remove)") : _style.Red("- removed"),
                OrphanCleanupAction.AlreadyGone => _style.Dim("~ already gone"),
                OrphanCleanupAction.SkippedOwnedByLiveEntry => _style.Dim("~ skipped"),
                OrphanCleanupAction.Failed => _style.Red("X failed"),
                _ => "?",
            };

            Console.WriteLine($"  {label} {r.EntryName}: {r.Target}");
            if (!string.IsNullOrEmpty(r.Message))
            {
                Console.WriteLine($"      {_style.Dim(r.Message)}");
            }
        }

        Console.WriteLine(new string('-', 60));
        if (!dryRun)
        {
            Console.WriteLine($"  {report.EntriesPruned} state {(report.EntriesPruned == 1 ? "entry" : "entries")} pruned.");
        }

        _logger.LogDebug("Cleanup report: {Count} results, {Pruned} pruned, dryRun={DryRun}", report.Results.Count, report.EntriesPruned, dryRun);
    }
}
