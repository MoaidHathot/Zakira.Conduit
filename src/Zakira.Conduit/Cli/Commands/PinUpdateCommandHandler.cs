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

        // Map of in-memory entry names to their disk index (if any), so we can
        // skip array-expanded children up front instead of doing pointless
        // network IO. A missing entry is one that was synthesized from an
        // array source (no on-disk `name` field to match) or otherwise
        // unaddressable.
        Dictionary<string, int>? diskIndexByName = null;
        try
        {
            diskIndexByName = TryMapDiskIndices(manifestPath);
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            _logger.LogDebug(ex, "Could not pre-map disk entry indices; pin will attempt the write blindly.");
        }

        var updates = new List<(int Index, string Name, string OldCommit, string NewCommit, string Branch)>();
        var skipped = new List<(string Name, string Reason)>();
        var errors = new List<(string Name, string Error)>();

        // Per-run cache of default-branch lookups keyed by 'owner/repo'.
        // Multiple manifest entries that reference the same repo without an
        // explicit branch only cost one extra API call total.
        var defaultBranchCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < model.Entries.Count; i++)
        {
            var entry = model.Entries[i];
            if (filter is not null && !filter.Contains(entry.ResolvedName))
            {
                continue;
            }

            if (entry.Source is GitHubSource gh)
            {
                if (diskIndexByName is not null && !diskIndexByName.ContainsKey(entry.ResolvedName))
                {
                    skipped.Add((entry.ResolvedName,
                        "entry has no addressable on-disk source (typically because it was synthesized from an array-source parent, or the on-disk entry has no 'name' field). Add an explicit 'name' to the disk entry, or split the array element out into its own object-source entry, to pin it."));
                    continue;
                }

                // Discover the default branch when none was supplied. Allows
                // bare 'repo: "owner/repo"' (no branch) to still be pinnable.
                var branch = gh.Branch;
                var branchWasDiscovered = false;
                if (string.IsNullOrWhiteSpace(branch))
                {
                    var cacheKey = $"{gh.Owner}/{gh.RepoName}";
                    if (!defaultBranchCache.TryGetValue(cacheKey, out var discovered))
                    {
                        try
                        {
                            var discoverAuth = await _ghCredentials.TryGetAsync(gh, cancellationToken).ConfigureAwait(false);
                            discovered = await _refResolver.GetDefaultBranchAsync(gh.Owner, gh.RepoName, discoverAuth, cancellationToken).ConfigureAwait(false);
                            defaultBranchCache[cacheKey] = discovered;
                            _logger.LogInformation("Discovered default branch for {Slug}: {Branch}", gh.Slug, discovered);
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            _logger.LogError(ex, "Failed to discover default branch for {Slug} (entry '{Name}')", gh.Slug, entry.ResolvedName);
                            errors.Add((entry.ResolvedName, $"failed to discover default branch: {ex.Message}"));
                            continue;
                        }
                    }

                    branch = discovered;
                    branchWasDiscovered = true;
                }

                try
                {
                    var authHeader = await _ghCredentials.TryGetAsync(gh, cancellationToken).ConfigureAwait(false);
                    var newSha = await _refResolver.ResolveAsync(gh.Owner, gh.RepoName, branch!, authHeader, cancellationToken).ConfigureAwait(false);
                    var oldCommit = gh.Commit ?? string.Empty;

                    // Skip the no-op only when the manifest already records the
                    // same SHA AND already records this branch. If branch was
                    // freshly discovered we still want to write it back so a
                    // future pin can refresh without re-querying.
                    if (string.Equals(oldCommit, newSha, StringComparison.OrdinalIgnoreCase) && !branchWasDiscovered)
                    {
                        skipped.Add((entry.ResolvedName, $"already at {Shorten(newSha)}"));
                        continue;
                    }

                    updates.Add((i, entry.ResolvedName, oldCommit, newSha, branch!));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Failed to resolve branch '{Branch}' for entry '{Name}'", branch, entry.ResolvedName);
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
            // Build a quick lookup of name -> (new SHA, branch) so the mutator
            // can find entries cheaply by walking the JSON tree once and also
            // record the branch for entries that didn't previously have one.
            var updateByName = updates.ToDictionary(
                u => u.Name,
                u => (NewCommit: u.NewCommit, Branch: u.Branch),
                StringComparer.OrdinalIgnoreCase);
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
                        if (string.IsNullOrEmpty(name) || !updateByName.TryGetValue(name, out var u))
                        {
                            continue;
                        }

                        var sourceNode = entryObj["source"];
                        switch (sourceNode)
                        {
                            case JsonObject sourceObj:
                                // Object form: set 'commit', and 'branch' if
                                // the manifest didn't already record one (so
                                // a future pin can refresh without a fresh
                                // default-branch lookup).
                                sourceObj["commit"] = u.NewCommit;
                                if (sourceObj["branch"] is null && !string.IsNullOrEmpty(u.Branch))
                                {
                                    sourceObj["branch"] = u.Branch;
                                }
                                break;

                            case JsonValue val when val.GetValueKind() == JsonValueKind.String:
                                // Bare-string URL: rebuild as a concrete
                                // object form so we can attach branch+commit.
                                // The conversion preserves repo + sub-path
                                // and (when present) the URL's own branch
                                // segment.
                                var raw = val.GetValue<string>();
                                if (TryConvertBareGitHubUrlToObject(raw, u.Branch, u.NewCommit) is { } replacement)
                                {
                                    entryObj["source"] = replacement;
                                }
                                else
                                {
                                    _logger.LogWarning(
                                        "Entry '{Name}' has a string source that could not be parsed as a github URL ('{Raw}'); leaving it untouched.",
                                        name, raw);
                                }
                                break;

                            default:
                                // Array or null sources are unreachable here:
                                // the per-entry skip above (diskIndexByName)
                                // would have rejected them, and an unnamed
                                // entry wouldn't match the name lookup.
                                break;
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

    /// <summary>
    ///     Converts a bare github URL (e.g.
    ///     <c>https://github.com/owner/repo</c>,
    ///     <c>https://github.com/owner/repo/skills</c>,
    ///     <c>https://github.com/owner/repo/tree/main/skills</c>) into the
    ///     explicit <c>{ "type": "github", "repo": ..., "path"?: ..., "branch": ..., "commit": ... }</c>
    ///     object form so a pin/update can persist the resolved branch and
    ///     commit. Returns <see langword="null"/> when the input doesn't
    ///     parse as a github URL, or when an in-string alias suffix
    ///     (<c> -&gt; Name</c>) is present and would be lost by the rewrite.
    /// </summary>
    private static JsonObject? TryConvertBareGitHubUrlToObject(string rawUrl, string branch, string commit)
    {
        if (string.IsNullOrWhiteSpace(rawUrl))
        {
            return null;
        }

        // Preserve the in-string alias by refusing to convert: rebuilding the
        // wrapper { "source": ..., "as": ... } is a larger restructure and
        // the conservative choice is to skip and let the user opt in.
        if (rawUrl.Contains(" -> ", StringComparison.Ordinal))
        {
            return null;
        }

        if (!GitHubRepoReference.TryParseExtended(rawUrl, out var owner, out var name, out var urlSubPath, out _, out _, allowExtraPath: true))
        {
            return null;
        }

        var obj = new JsonObject
        {
            ["type"] = "github",
            ["repo"] = $"{owner}/{name}",
        };

        if (!string.IsNullOrWhiteSpace(urlSubPath))
        {
            obj["path"] = urlSubPath;
        }

        obj["branch"] = branch;
        obj["commit"] = commit;
        return obj;
    }
}
