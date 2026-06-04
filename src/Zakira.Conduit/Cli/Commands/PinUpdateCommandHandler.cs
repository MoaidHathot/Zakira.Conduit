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
    private readonly Sources.GitHub.Credentials.ChainedGitHubCredentialProvider _ghCredentials;
    private readonly IAzdoRefResolver _azdoRefResolver;
    private readonly ConsoleStyle _style;
    private readonly ILogger<PinUpdateCommandHandler> _logger;

    public PinUpdateCommandHandler(
        IManifestLocator locator,
        IManifestLoader loader,
        IManifestWriter writer,
        IGitHubRefResolver refResolver,
        Sources.GitHub.Credentials.ChainedGitHubCredentialProvider ghCredentials,
        IAzdoRefResolver azdoRefResolver,
        ConsoleStyle style,
        ILogger<PinUpdateCommandHandler> logger)
    {
        _locator = locator;
        _loader = loader;
        _writer = writer;
        _refResolver = refResolver;
        _ghCredentials = ghCredentials;
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
            if (filter is not null && !filter.Contains(entry.ResolvedName))
            {
                continue;
            }

            if (entry.Source is GitHubSource gh)
            {
                if (string.IsNullOrWhiteSpace(gh.Branch))
                {
                    skipped.Add((entry.ResolvedName, "no 'branch' field to resolve"));
                    continue;
                }

                try
                {
                    var authHeader = await _ghCredentials.TryGetAsync(gh, cancellationToken).ConfigureAwait(false);
                    var newSha = await _refResolver.ResolveAsync(gh.Owner, gh.RepoName, gh.Branch, authHeader, cancellationToken).ConfigureAwait(false);
                    var oldCommit = gh.Commit ?? string.Empty;

                    if (string.Equals(oldCommit, newSha, StringComparison.OrdinalIgnoreCase))
                    {
                        skipped.Add((entry.ResolvedName, $"already at {Shorten(newSha)}"));
                        continue;
                    }

                    updates.Add((i, entry.ResolvedName, oldCommit, newSha, gh.Branch));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Failed to resolve branch '{Branch}' for entry '{Name}'", gh.Branch, entry.ResolvedName);
                    errors.Add((entry.ResolvedName, ex.Message));
                }

                continue;
            }

            if (entry.Source is AzdoSource azdo)
            {
                var intentValue = !string.IsNullOrWhiteSpace(azdo.Branch) ? azdo.Branch :
                                  !string.IsNullOrWhiteSpace(azdo.Tag) ? azdo.Tag : null;
                var intentKind = !string.IsNullOrWhiteSpace(azdo.Branch) ? "branch" :
                                 !string.IsNullOrWhiteSpace(azdo.Tag) ? "tag" : null;

                if (intentValue is null || intentKind is null)
                {
                    skipped.Add((entry.ResolvedName, "no 'branch' or 'tag' field to resolve"));
                    continue;
                }

                try
                {
                    var newSha = await _azdoRefResolver.ResolveAsync(azdo, intentValue, intentKind, cancellationToken).ConfigureAwait(false);
                    var oldCommit = azdo.Commit ?? string.Empty;

                    if (string.Equals(oldCommit, newSha, StringComparison.OrdinalIgnoreCase))
                    {
                        skipped.Add((entry.ResolvedName, $"already at {Shorten(newSha)}"));
                        continue;
                    }

                    updates.Add((i, entry.ResolvedName, oldCommit, newSha, intentValue));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Failed to resolve {Kind} '{Ref}' for entry '{Name}'", intentKind, intentValue, entry.ResolvedName);
                    errors.Add((entry.ResolvedName, ex.Message));
                }

                continue;
            }

            skipped.Add((entry.ResolvedName, $"unsupported source kind '{entry.Source.Kind}'"));
        }

        // Build the new manifest with updated commits.
        string? backupPath = null;
        if (updates.Count > 0 && !dryRun)
        {
            // Build a quick lookup of name -> new SHA so the mutator can find
            // entries cheaply by walking the JSON tree once.
            var newCommitByName = updates.ToDictionary(u => u.Name, u => u.NewCommit, StringComparer.OrdinalIgnoreCase);

            // First try the trivia-preserving surgical patch: walks the file
            // text and replaces each affected `commit` leaf in-place,
            // keeping comments / trailing commas / formatting intact. This
            // only works when every target entry already has a `commit`
            // field; if any is missing we need to insert one, which the
            // patcher refuses to do, and we fall back to the full rewrite.
            var leafEdits = BuildLeafEdits(manifestPath, newCommitByName, cancellationToken);
            if (leafEdits is not null)
            {
                var (patched, surgicalBackup) = await _writer.ReplaceStringLeavesAsync(manifestPath, leafEdits, cancellationToken).ConfigureAwait(false);
                if (patched)
                {
                    backupPath = surgicalBackup;
                }
                else
                {
                    leafEdits = null; // fall through to full rewrite
                }
            }

            if (leafEdits is null)
            {
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

            Console.WriteLine($"  {_style.Dim("Note: pin/update prefers a surgical in-place edit that preserves comments and trailing commas. A full reformat is only used as a fallback when the surgical patch can't be applied (typically when a 'commit' field needs to be inserted rather than replaced).")}");
        }
    }

    private static string Shorten(string sha) =>
        string.IsNullOrEmpty(sha) || sha.Length <= 12 ? sha : sha[..12];

    /// <summary>
    ///     Builds the surgical leaf-edit list <see cref="IManifestWriter.ReplaceStringLeavesAsync"/>
    ///     consumes. Returns <see langword="null"/> when we can't safely
    ///     compute the disk index for an updated entry (e.g. malformed file,
    ///     or array-source expansion confounds index mapping); callers fall
    ///     back to the full <c>RewriteAsync</c> path.
    /// </summary>
    private static List<JsonValuePatcher.StringEdit>? BuildLeafEdits(
        string manifestPath,
        Dictionary<string, string> newCommitByName,
        CancellationToken cancellationToken)
    {
        Dictionary<string, int>? indexByName;
        try
        {
            indexByName = TryMapDiskIndices(manifestPath);
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return null;
        }

        if (indexByName is null)
        {
            return null;
        }

        var edits = new List<JsonValuePatcher.StringEdit>(newCommitByName.Count);
        foreach (var (name, newSha) in newCommitByName)
        {
            if (!indexByName.TryGetValue(name, out var idx))
            {
                // Entry referenced in updates but not present in the on-disk
                // JSON file under that name. Could happen if the in-memory
                // manifest was produced by array-source expansion (where the
                // synthesized child name doesn't exist in the file). Bail
                // out so the full rewrite path handles it correctly.
                return null;
            }

            edits.Add(new JsonValuePatcher.StringEdit($"entries[{idx}].source.commit", newSha));
            cancellationToken.ThrowIfCancellationRequested();
        }

        return edits;
    }

    /// <summary>
    ///     Reads the manifest file and maps every named entry to its index
    ///     in the on-disk <c>entries</c> array. Returns <see langword="null"/>
    ///     when the file isn't a JSON object or has no <c>entries</c> array.
    /// </summary>
    private static Dictionary<string, int>? TryMapDiskIndices(string manifestPath)
    {
        var raw = File.ReadAllText(manifestPath);
        var node = JsonNode.Parse(raw, documentOptions: new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        });

        if (node is not JsonObject root || root["entries"] is not JsonArray entries)
        {
            return null;
        }

        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < entries.Count; i++)
        {
            if (entries[i] is JsonObject obj && obj["name"]?.GetValue<string>() is { Length: > 0 } name)
            {
                map[name] = i;
            }
        }

        return map;
    }
}
