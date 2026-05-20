using System.Text.Json;
using Microsoft.Extensions.Logging;
using Zakira.Conduit.Manifest;
using Zakira.Conduit.Synchronization;

namespace Zakira.Conduit.Cli.Commands;

/// <summary>
///     Implements <c>conduit status</c>. Reads the manifest and its sibling
///     state file and prints, per entry, the last resolved ref, last sync time,
///     and target presence &mdash; without touching the network.
/// </summary>
internal sealed class StatusCommandHandler
{
    private readonly IManifestLocator _locator;
    private readonly IManifestLoader _loader;
    private readonly IConduitStateStore _stateStore;
    private readonly ConsoleStyle _style;
    private readonly ILogger<StatusCommandHandler> _logger;

    public StatusCommandHandler(
        IManifestLocator locator,
        IManifestLoader loader,
        IConduitStateStore stateStore,
        ConsoleStyle style,
        ILogger<StatusCommandHandler> logger)
    {
        _locator = locator;
        _loader = loader;
        _stateStore = stateStore;
        _style = style;
        _logger = logger;
    }

    public async Task<int> InvokeAsync(string? manifest, OutputFormat output, CancellationToken cancellationToken)
    {
        string manifestPath;
        ConduitManifest model;
        try
        {
            manifestPath = _locator.Locate(manifest);
            _logger.LogDebug("Using manifest: {Path}", manifestPath);
            model = await _loader.LoadAsync(manifestPath, cancellationToken).ConfigureAwait(false);
        }
        catch (ManifestException ex)
        {
            ErrorRenderer.RenderManifestError(ex, output);
            return 2;
        }

        var state = await _stateStore.LoadAsync(manifestPath, cancellationToken).ConfigureAwait(false);
        var stateFilePath = _stateStore.GetStateFilePath(manifestPath);
        var now = DateTimeOffset.UtcNow;

        var rows = model.Entries.Select(entry =>
        {
            var entryState = _stateStore.GetEntry(state, entry.Name);
            var (allTargetsPresent, targetCheck) = ProbeTargets(entry, manifestPath, entryState);
            return new StatusRow(
                entry,
                EntryState: entryState,
                AllTargetsPresent: allTargetsPresent,
                Targets: targetCheck);
        }).ToList();

        if (output == OutputFormat.Json)
        {
            RenderJson(manifestPath, stateFilePath, state, rows, now);
        }
        else
        {
            RenderText(manifestPath, stateFilePath, state, rows, now);
        }

        return 0;
    }

    private static (bool All, IReadOnlyList<TargetProbe> Probes) ProbeTargets(ConduitEntry entry, string manifestPath, EntryState? state)
    {
        // The state file already stores absolute target paths. For entries that
        // have never been synced we can't say much - fall back to "unknown".
        if (state is null)
        {
            return (false, entry.Targets.Select(t => new TargetProbe(t.Path, Exists: false, Recorded: false)).ToList());
        }

        var probes = state.Targets
            .Select(t => new TargetProbe(t, Exists: Directory.Exists(t), Recorded: true))
            .ToList();

        return (probes.All(p => p.Exists), probes);
    }

    private void RenderText(string manifestPath, string stateFilePath, ConduitState state, IReadOnlyList<StatusRow> rows, DateTimeOffset now)
    {
        Console.WriteLine(_style.Dim($"# {manifestPath}"));
        Console.WriteLine(_style.Dim($"# state    {stateFilePath} {(File.Exists(stateFilePath) ? _style.Dim("(present)") : _style.Yellow("(missing - nothing has been synced yet)"))}"));
        Console.WriteLine();

        foreach (var row in rows)
        {
            var entry = row.Entry;
            var status = entry.Disabled ? _style.Yellow(" (disabled)") : string.Empty;

            string statusLabel;
            if (entry.Disabled)
            {
                statusLabel = _style.Yellow("disabled");
            }
            else if (row.EntryState is null)
            {
                statusLabel = _style.Yellow("never synced");
            }
            else if (!row.AllTargetsPresent)
            {
                statusLabel = _style.Red("targets drifted");
            }
            else
            {
                statusLabel = _style.Green("synced");
            }

            Console.WriteLine($"- {_style.Bold(entry.Name)}{status}  [{statusLabel}]");

            if (row.EntryState is { } es)
            {
                if (!string.IsNullOrEmpty(es.ResolvedRef))
                {
                    Console.WriteLine($"    ref     : {_style.Cyan(es.ResolvedRef)}");
                }

                if (es.LastSyncUtc is { } when0)
                {
                    var ago = now - when0;
                    Console.WriteLine($"    synced  : {when0:yyyy-MM-dd HH:mm:ss}Z {_style.Dim($"({Humanise(ago)} ago)")}");
                }

                if (!string.IsNullOrEmpty(es.Etag))
                {
                    Console.WriteLine($"    etag    : {_style.Dim(es.Etag)}");
                }

                if (!string.IsNullOrEmpty(es.SourceContentHash))
                {
                    Console.WriteLine($"    hash    : {_style.Dim(es.SourceContentHash[..Math.Min(es.SourceContentHash.Length, 12)])}");
                }
            }

            Console.WriteLine("    targets :");
            foreach (var probe in row.Targets)
            {
                var marker = probe.Exists ? _style.Green("+") : _style.Red("X");
                Console.WriteLine($"      {marker} {probe.Path}");
            }
        }
    }

    private static void RenderJson(string manifestPath, string stateFilePath, ConduitState state, IReadOnlyList<StatusRow> rows, DateTimeOffset now)
    {
        var dto = new
        {
            manifest = manifestPath,
            state = stateFilePath,
            stateFilePresent = File.Exists(stateFilePath),
            generatedAt = now,
            entries = rows.Select(r => new
            {
                name = r.Entry.Name,
                disabled = r.Entry.Disabled,
                kind = r.Entry.Source.Kind,
                neverSynced = r.EntryState is null,
                allTargetsPresent = r.AllTargetsPresent,
                resolvedRef = r.EntryState?.ResolvedRef,
                etag = r.EntryState?.Etag,
                sourceContentHash = r.EntryState?.SourceContentHash,
                lastSyncUtc = r.EntryState?.LastSyncUtc,
                lastSyncSecondsAgo = r.EntryState?.LastSyncUtc is { } when0 ? (long)(now - when0).TotalSeconds : (long?)null,
                targets = r.Targets.Select(t => new { path = t.Path, exists = t.Exists, recorded = t.Recorded }),
            }),
        };

        Console.WriteLine(JsonSerializer.Serialize(dto, ManifestJson.WriteOptions));
    }

    private static string Humanise(TimeSpan elapsed)
    {
        if (elapsed.TotalSeconds < 60)
        {
            return $"{(int)elapsed.TotalSeconds}s";
        }

        if (elapsed.TotalMinutes < 60)
        {
            return $"{(int)elapsed.TotalMinutes}m";
        }

        if (elapsed.TotalHours < 24)
        {
            return $"{(int)elapsed.TotalHours}h";
        }

        if (elapsed.TotalDays < 30)
        {
            return $"{(int)elapsed.TotalDays}d";
        }

        return $"{(int)(elapsed.TotalDays / 30)}mo";
    }

    private sealed record StatusRow(ConduitEntry Entry, EntryState? EntryState, bool AllTargetsPresent, IReadOnlyList<TargetProbe> Targets);

    private sealed record TargetProbe(string Path, bool Exists, bool Recorded);
}
