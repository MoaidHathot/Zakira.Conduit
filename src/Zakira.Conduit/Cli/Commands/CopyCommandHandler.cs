using System.Text.Json;
using Microsoft.Extensions.Logging;
using Zakira.Conduit.Manifest;
using Zakira.Conduit.Sources.Inference;
using Zakira.Conduit.Strategies;
using Zakira.Conduit.Synchronization;

namespace Zakira.Conduit.Cli.Commands;

/// <summary>
///     Implements <c>conduit copy</c>: a one-shot copy that bypasses the
///     manifest entirely. The user supplies a source URI and a target path,
///     plus optional strategy flags; we synthesize a one-entry manifest and
///     hand it to the standard synchronizer.
/// </summary>
internal sealed class CopyCommandHandler
{
    private readonly IConduitSynchronizer _synchronizer;
    private readonly SourceInferenceCoordinator _inference;
    private readonly IPlanStrategyRegistry _strategies;
    private readonly ConsoleStyle _style;
    private readonly ILogger<CopyCommandHandler> _logger;

    public CopyCommandHandler(
        IConduitSynchronizer synchronizer,
        SourceInferenceCoordinator inference,
        IPlanStrategyRegistry strategies,
        ConsoleStyle style,
        ILogger<CopyCommandHandler> logger)
    {
        _synchronizer = synchronizer;
        _inference = inference;
        _strategies = strategies;
        _style = style;
        _logger = logger;
    }

    public async Task<int> InvokeAsync(
        string sourceUri,
        string targetPath,
        string? strategy,
        string? groupBy,
        string? onCollision,
        IReadOnlyList<string>? skills,
        IReadOnlyList<string>? harness,
        IReadOnlyList<string>? addHarness,
        bool dryRun,
        OutputFormat output,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sourceUri))
        {
            Console.Error.WriteLine("conduit copy: <source> is required.");
            return 2;
        }

        if (string.IsNullOrWhiteSpace(targetPath))
        {
            Console.Error.WriteLine("conduit copy: <target> is required.");
            return 2;
        }

        // 1. Build the entry: infer the source from its URI, parse the strategy
        // flags, and synthesise a ConduitEntry.
        Manifest.ISource source;
        try
        {
            source = _inference.Infer(new UriBasedSource { Uri = sourceUri });
        }
        catch (SourceInferenceException ex)
        {
            Console.Error.WriteLine($"conduit copy: could not infer source kind from '{sourceUri}': {ex.Message}");
            return 2;
        }

        var resolvedStrategy = string.IsNullOrWhiteSpace(strategy) ? StrategyNames.Wrap : strategy.Trim();
        if (!_strategies.IsRegistered(resolvedStrategy))
        {
            Console.Error.WriteLine($"conduit copy: unknown strategy '{resolvedStrategy}'. Available: {string.Join(", ", _strategies.Names)}.");
            return 2;
        }

        var resolvedGroupBy = ParseGroupBy(groupBy);
        if (resolvedGroupBy is null)
        {
            Console.Error.WriteLine($"conduit copy: unknown --group-by '{groupBy}'. Allowed: none, source.");
            return 2;
        }

        var resolvedOnCollision = ParseOnCollision(onCollision);
        if (resolvedOnCollision.IsError)
        {
            Console.Error.WriteLine($"conduit copy: unknown --on-collision '{onCollision}'. Allowed: error, skip, last-wins.");
            return 2;
        }

        var entryName = DefaultSourceNameDeriver.Derive(source)
            ?? GenerateFallbackName(sourceUri);

        var entry = new ConduitEntry
        {
            Name = entryName,
            Source = source,
            Targets = [targetPath],
            Strategy = resolvedStrategy,
            GroupBy = resolvedGroupBy.Value,
            OnCollision = resolvedOnCollision.Value,
            Skills = skills is { Count: > 0 } ? skills.ToArray() : null,
            Harness = ParseHarness(harness, resolvedStrategy),
        };

        var strategiesSection = ParseAddHarness(addHarness);

        var manifest = new ConduitManifest
        {
            Entries = [entry],
            Strategies = strategiesSection,
        };

        // 2. Pick a working manifest path next to the cwd so the path resolver
        // has a base for relative target paths. The file doesn't need to
        // exist; the synchronizer only uses the directory.
        var manifestPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "conduit.copy.virtual"));

        // 3. Run.
        var options = new SyncOptions { DryRun = dryRun };
        SyncReport report;
        try
        {
            report = await _synchronizer.SyncAsync(manifest, manifestPath, options, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "conduit copy: sync failed");
            Console.Error.WriteLine($"conduit copy: {ex.Message}");
            return 1;
        }

        // 4. Render.
        if (output == OutputFormat.Json)
        {
            RenderJson(report, entry);
        }
        else
        {
            RenderText(report, entry);
        }

        return report.ExitCode;
    }

    private void RenderText(SyncReport report, ConduitEntry entry)
    {
        var label = report.Succeeded ? _style.Green("ok") : _style.Red("FAILED");
        var dry = report.DryRun ? _style.Yellow(" (dry-run)") : string.Empty;

        Console.WriteLine($"copy {entry.ResolvedName} [{label}]{dry}");

        var result = report.Entries.Count > 0 ? report.Entries[0] : null;
        if (result is null)
        {
            return;
        }

        foreach (var target in result.Targets)
        {
            var tlabel = target.Succeeded ? _style.Green("+") : _style.Red("X");
            var files = target.FilesWritten > 0 ? _style.Dim($" ({target.FilesWritten} files)") : string.Empty;
            Console.WriteLine($"  {tlabel} {target.TargetPath}{files}");
            if (!target.Succeeded && !string.IsNullOrEmpty(target.Error))
            {
                Console.WriteLine($"    {_style.Red(target.Error)}");
            }
        }
    }

    private static void RenderJson(SyncReport report, ConduitEntry entry)
    {
        var dto = new
        {
            name = entry.ResolvedName,
            strategy = entry.Strategy,
            dryRun = report.DryRun,
            succeeded = report.Succeeded,
            targets = (report.Entries.Count > 0 ? report.Entries[0] : null)?.Targets.Select(t => new
            {
                path = t.TargetPath,
                succeeded = t.Succeeded,
                filesWritten = t.FilesWritten,
                error = t.Error,
            }) ?? Array.Empty<object>().Cast<dynamic>(),
        };

        Console.WriteLine(JsonSerializer.Serialize(dto, ManifestJson.WriteOptions));
    }

    private static GroupBy? ParseGroupBy(string? raw) =>
        raw?.Trim().ToLowerInvariant() switch
        {
            null or "" or "none" => GroupBy.None,
            "source" => GroupBy.Source,
            _ => null,
        };

    private static (bool IsError, OnCollisionPolicy? Value) ParseOnCollision(string? raw)
    {
        return raw?.Trim().ToLowerInvariant() switch
        {
            null or "" => (false, null),
            "error" => (false, OnCollisionPolicy.Error),
            "skip" => (false, OnCollisionPolicy.Skip),
            "last-wins" or "lastwins" or "last_wins" => (false, OnCollisionPolicy.LastWins),
            _ => (true, null),
        };
    }

    private static HarnessSelector? ParseHarness(IReadOnlyList<string>? raw, string strategyName)
    {
        if (raw is null || raw.Count == 0)
        {
            return null;
        }

        // Skip per-entry harness when not skills strategy (validator would
        // reject it anyway). Avoids surprising behaviour with --copy --strategy wrap.
        if (!string.Equals(strategyName, StrategyNames.Skills, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // Special case: a single value of "true"/"false" maps to the boolean form.
        if (raw.Count == 1)
        {
            var single = raw[0].Trim();
            if (string.Equals(single, "true", StringComparison.OrdinalIgnoreCase))
            {
                return HarnessSelector.EnabledAll;
            }

            if (string.Equals(single, "false", StringComparison.OrdinalIgnoreCase))
            {
                return HarnessSelector.Disabled;
            }
        }

        return HarnessSelector.Named(raw);
    }

    private static StrategiesConfig? ParseAddHarness(IReadOnlyList<string>? entries)
    {
        if (entries is null || entries.Count == 0)
        {
            return null;
        }

        var registry = new Dictionary<string, IReadOnlyList<string>?>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in entries)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var sep = raw.IndexOf('=');
            if (sep <= 0 || sep == raw.Length - 1)
            {
                throw new ArgumentException($"--add-harness expects 'name=path' (got '{raw}').");
            }

            var name = raw[..sep].Trim();
            var path = raw[(sep + 1)..].Trim();

            if (registry.TryGetValue(name, out var existing) && existing is not null)
            {
                var merged = new List<string>(existing) { path };
                registry[name] = merged;
            }
            else
            {
                registry[name] = new[] { path };
            }
        }

        return new StrategiesConfig
        {
            Skills = new SkillsStrategyConfig { HarnessRegistry = registry },
        };
    }

    private static string GenerateFallbackName(string raw)
    {
        // Last resort: turn the URI into a safe entry name. The validator
        // accepts [A-Za-z0-9._-]+, so we replace everything else with '-'.
        var safe = new System.Text.StringBuilder(raw.Length);
        foreach (var c in raw)
        {
            if (char.IsLetterOrDigit(c) || c is '-' or '_' or '.')
            {
                safe.Append(c);
            }
            else
            {
                safe.Append('-');
            }
        }

        var result = safe.ToString().Trim('-');
        return string.IsNullOrEmpty(result) ? "copy-target" : result;
    }
}
