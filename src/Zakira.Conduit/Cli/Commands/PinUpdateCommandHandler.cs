using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Zakira.Conduit.Manifest;
using Zakira.Conduit.Sources.Azdo;
using Zakira.Conduit.Sources.GitHub;

namespace Zakira.Conduit.Cli.Commands;

/// <summary>
///     Implements both <c>conduit pin</c> and <c>conduit update</c>: for every
///     GitHub entry that tracks a <c>branch</c>, resolve the branch's tip SHA
///     via the GitHub API and rewrite the manifest's <c>commit</c> field to
///     match. The two commands share behaviour; the verb is for user mental
///     model (<c>pin</c> reads as "lock", <c>update</c> reads as "refresh").
/// </summary>
internal sealed class PinUpdateCommandHandler
{
    private readonly IManifestLocator _locator;
    private readonly IManifestLoader _loader;
    private readonly IManifestWriter _writer;
    private readonly IGitHubRefResolver _refResolver;
    private readonly IAzdoRefResolver _azdoRefResolver;
    private readonly ConsoleStyle _style;
    private readonly ILogger<PinUpdateCommandHandler> _logger;

    public PinUpdateCommandHandler(
        IManifestLocator locator,
        IManifestLoader loader,
        IManifestWriter writer,
        IGitHubRefResolver refResolver,
        IAzdoRefResolver azdoRefResolver,
        ConsoleStyle style,
        ILogger<PinUpdateCommandHandler> logger)
    {
        _locator = locator;
        _loader = loader;
        _writer = writer;
        _refResolver = refResolver;
        _azdoRefResolver = azdoRefResolver;
        _style = style;
        _logger = logger;
    }

    public async Task<int> InvokeAsync(string verb, string? manifest, IReadOnlyList<string> entries, bool dryRun, OutputFormat output, CancellationToken cancellationToken)
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

        var filter = entries.Count > 0
            ? new HashSet<string>(entries, StringComparer.OrdinalIgnoreCase)
            : null;

        var updates = new List<(int Index, string Name, string OldCommit, string NewCommit, string Branch)>();
        var skipped = new List<(string Name, string Reason)>();
        var errors = new List<(string Name, string Error)>();

        for (var i = 0; i < model.Entries.Count; i++)
        {
            var entry = model.Entries[i];
            if (filter is not null && !filter.Contains(entry.Name))
            {
                continue;
            }

            if (entry.Source is GitHubSkillSource gh)
            {
                if (string.IsNullOrWhiteSpace(gh.Branch))
                {
                    skipped.Add((entry.Name, "no 'branch' field to resolve"));
                    continue;
                }

                try
                {
                    var newSha = await _refResolver.ResolveAsync(gh.Owner, gh.RepoName, gh.Branch, cancellationToken).ConfigureAwait(false);
                    var oldCommit = gh.Commit ?? string.Empty;

                    if (string.Equals(oldCommit, newSha, StringComparison.OrdinalIgnoreCase))
                    {
                        skipped.Add((entry.Name, $"already at {Shorten(newSha)}"));
                        continue;
                    }

                    updates.Add((i, entry.Name, oldCommit, newSha, gh.Branch));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Failed to resolve branch '{Branch}' for entry '{Name}'", gh.Branch, entry.Name);
                    errors.Add((entry.Name, ex.Message));
                }

                continue;
            }

            if (entry.Source is AzdoSkillSource azdo)
            {
                var intentValue = !string.IsNullOrWhiteSpace(azdo.Branch) ? azdo.Branch :
                                  !string.IsNullOrWhiteSpace(azdo.Tag) ? azdo.Tag : null;
                var intentKind = !string.IsNullOrWhiteSpace(azdo.Branch) ? "branch" :
                                 !string.IsNullOrWhiteSpace(azdo.Tag) ? "tag" : null;

                if (intentValue is null || intentKind is null)
                {
                    skipped.Add((entry.Name, "no 'branch' or 'tag' field to resolve"));
                    continue;
                }

                try
                {
                    var newSha = await _azdoRefResolver.ResolveAsync(azdo, intentValue, intentKind, cancellationToken).ConfigureAwait(false);
                    var oldCommit = azdo.Commit ?? string.Empty;

                    if (string.Equals(oldCommit, newSha, StringComparison.OrdinalIgnoreCase))
                    {
                        skipped.Add((entry.Name, $"already at {Shorten(newSha)}"));
                        continue;
                    }

                    updates.Add((i, entry.Name, oldCommit, newSha, intentValue));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Failed to resolve {Kind} '{Ref}' for entry '{Name}'", intentKind, intentValue, entry.Name);
                    errors.Add((entry.Name, ex.Message));
                }

                continue;
            }

            skipped.Add((entry.Name, $"unsupported source kind '{entry.Source.Kind}'"));
        }

        // Build the new manifest with updated commits.
        string? backupPath = null;
        if (updates.Count > 0 && !dryRun)
        {
            // Build a quick lookup of name -> new SHA so the mutator can find
            // entries cheaply by walking the JSON tree once.
            var newCommitByName = updates.ToDictionary(u => u.Name, u => u.NewCommit, StringComparer.OrdinalIgnoreCase);

            backupPath = await _writer.RewriteAsync(manifestPath, root =>
            {
                if (root["entries"] is not JsonArray entriesArray)
                {
                    return;
                }

                foreach (var node in entriesArray)
                {
                    if (node is not JsonObject entryObj)
                    {
                        continue;
                    }

                    var name = entryObj["name"]?.GetValue<string>();
                    if (string.IsNullOrEmpty(name) || !newCommitByName.TryGetValue(name, out var newSha))
                    {
                        continue;
                    }

                    if (entryObj["source"] is JsonObject sourceObj)
                    {
                        sourceObj["commit"] = newSha;
                    }
                }
            }, cancellationToken).ConfigureAwait(false);
        }

        RenderReport(verb, manifestPath, backupPath, updates, skipped, errors, dryRun, output);
        return errors.Count == 0 ? 0 : 1;
    }

    private void RenderReport(string verb, string manifestPath, string? backupPath, List<(int Index, string Name, string OldCommit, string NewCommit, string Branch)> updates, List<(string Name, string Reason)> skipped, List<(string Name, string Error)> errors, bool dryRun, OutputFormat output)
    {
        if (output == OutputFormat.Json)
        {
            var dto = new
            {
                verb,
                manifest = manifestPath,
                backup = backupPath,
                dryRun,
                updates = updates.Select(u => new { name = u.Name, branch = u.Branch, oldCommit = string.IsNullOrEmpty(u.OldCommit) ? null : u.OldCommit, newCommit = u.NewCommit }),
                skipped = skipped.Select(s => new { name = s.Name, reason = s.Reason }),
                errors = errors.Select(e => new { name = e.Name, error = e.Error }),
            };
            Console.WriteLine(JsonSerializer.Serialize(dto, ManifestJson.WriteOptions));
            return;
        }

        Console.WriteLine();
        Console.WriteLine(_style.Bold($"{verb} report for {manifestPath}"));
        Console.WriteLine(new string('-', 60));

        foreach (var u in updates)
        {
            var arrow = string.IsNullOrEmpty(u.OldCommit)
                ? _style.Green("+")
                : _style.Cyan("~");
            var fromLabel = string.IsNullOrEmpty(u.OldCommit) ? "<unpinned>" : Shorten(u.OldCommit);
            Console.WriteLine($"  {arrow} {u.Name}  ({u.Branch})  {fromLabel} -> {_style.Cyan(Shorten(u.NewCommit))}");
        }

        foreach (var s in skipped)
        {
            Console.WriteLine($"  {_style.Dim("~")} {_style.Dim(s.Name)}  {_style.Dim($"(skipped: {s.Reason})")}");
        }

        foreach (var e in errors)
        {
            Console.WriteLine($"  {_style.Red("X")} {e.Name}  {_style.Red("(error)")}");
            Console.WriteLine($"      {_style.Red("error:")} {e.Error}");
        }

        Console.WriteLine(new string('-', 60));
        Console.WriteLine($"  {updates.Count} updated, {skipped.Count} skipped, {errors.Count} failed.");

        if (dryRun)
        {
            Console.WriteLine($"  {_style.Yellow("(dry-run: manifest was not modified.)")}");
        }
        else if (updates.Count > 0)
        {
            if (!string.IsNullOrEmpty(backupPath))
            {
                Console.WriteLine($"  {_style.Dim($"Backup of the original manifest: {backupPath}")}");
            }

            Console.WriteLine($"  {_style.Yellow("Note: pin/update reformat the manifest. Comments and trailing commas in the source file are lost.")}");
        }
    }

    private static string Shorten(string sha) =>
        string.IsNullOrEmpty(sha) || sha.Length <= 12 ? sha : sha[..12];
}
