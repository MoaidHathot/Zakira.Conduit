using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Zakira.Conduit.Manifest;
using Zakira.Conduit.Sources.Azdo;
using Zakira.Conduit.Sources.GitHub;

namespace Zakira.Conduit.Cli.Commands;

/// <summary>
///     Implements both <c>conduit pin</c> and <c>conduit update</c>: locks
///     each GitHub / AzDO entry to a specific commit SHA by <i>rewriting the
///     source URL or object in place</i> to the URL-native pinned form
///     (GitHub: <c>tree/&lt;sha&gt;/&lt;path&gt;</c>; AzDO:
///     <c>?version=GC&lt;sha&gt;</c>).
///     <para>
///         The two commands share behaviour (the verb is for mental model
///         only). Already-pinned entries (those without a branch but with a
///         commit) are skipped with a pointer at <c>conduit unpin</c>; pin
///         is intentionally one-way to preserve the user's branch choice.
///     </para>
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

        var updates = new List<PinUpdate>();
        var skipped = new List<(string Name, string Reason)>();
        var errors = new List<(string Name, string Error)>();

        // Per-run cache of default-branch lookups keyed by 'kind:slug'. Several
        // entries pointing at the same repo without an explicit branch only
        // cost one extra API call total.
        var defaultBranchCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < model.Entries.Count; i++)
        {
            var entry = model.Entries[i];
            if (filter is not null && !filter.Contains(entry.ResolvedName))
            {
                continue;
            }

            if (!entry.OriginalDiskEntryIndex.HasValue)
            {
                // Defensive: every post-inference entry should have a disk
                // index. If it doesn't we can't safely write back.
                skipped.Add((entry.ResolvedName,
                    "entry has no on-disk position recorded by the inference pass; this is a bug. Please report it."));
                continue;
            }

            switch (entry.Source)
            {
                case GitHubSource gh:
                    await PinGitHubAsync(entry, gh, updates, skipped, errors, defaultBranchCache, cancellationToken).ConfigureAwait(false);
                    break;

                case AzdoSource azdo:
                    await PinAzdoAsync(entry, azdo, updates, skipped, errors, defaultBranchCache, cancellationToken).ConfigureAwait(false);
                    break;

                default:
                    skipped.Add((entry.ResolvedName, $"unsupported source kind '{entry.Source.Kind}'"));
                    break;
            }
        }

        // Apply the rewrites. We always go through the full RewriteAsync path
        // (JsonNode-based) for object sources because it needs to delete keys
        // (e.g. drop 'branch' when pinning), which the surgical JSONC patcher
        // can't do. For string sources we route to the JSONC patcher first
        // (comment-preserving), and only fall back to RewriteAsync if the
        // patcher refuses (e.g. file shape unexpected).
        string? backupPath = null;
        if (updates.Count > 0 && !dryRun)
        {
            backupPath = await ApplyUpdatesAsync(manifestPath, updates, cancellationToken).ConfigureAwait(false);
        }

        RenderReport(verb, manifestPath, backupPath, updates, skipped, errors, dryRun, output);
        return errors.Count == 0 ? 0 : 1;
    }

    private async Task PinGitHubAsync(
        ConduitEntry entry,
        GitHubSource gh,
        List<PinUpdate> updates,
        List<(string Name, string Reason)> skipped,
        List<(string Name, string Error)> errors,
        Dictionary<string, string> defaultBranchCache,
        CancellationToken cancellationToken)
    {
        // Already pinned (commit set, no branch): URL-native model treats pin
        // as one-way. Refresh requires `conduit unpin` first to put the
        // entry back on a branch.
        if (!string.IsNullOrWhiteSpace(gh.Commit) && string.IsNullOrWhiteSpace(gh.Branch))
        {
            skipped.Add((entry.ResolvedName,
                $"already pinned to {Shorten(gh.Commit!)}. Run 'conduit unpin --entry {entry.ResolvedName}' to put it back on a branch, then re-pin to refresh."));
            return;
        }

        // Determine which branch to resolve. Explicit branch wins; otherwise
        // discover the repo's default branch (cached per repo).
        var branch = gh.Branch;
        if (string.IsNullOrWhiteSpace(branch))
        {
            var cacheKey = "github:" + gh.Slug;
            if (!defaultBranchCache.TryGetValue(cacheKey, out var discovered))
            {
                try
                {
                    var authHeader = await _ghCredentials.TryGetAsync(gh, cancellationToken).ConfigureAwait(false);
                    discovered = await _refResolver.GetDefaultBranchAsync(gh.Owner, gh.RepoName, authHeader, cancellationToken).ConfigureAwait(false);
                    defaultBranchCache[cacheKey] = discovered;
                    _logger.LogInformation("Discovered default branch for {Slug}: {Branch}", gh.Slug, discovered);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Failed to discover default branch for {Slug} (entry '{Name}')", gh.Slug, entry.ResolvedName);
                    errors.Add((entry.ResolvedName, $"failed to discover default branch: {ex.Message}"));
                    return;
                }
            }

            branch = discovered;
        }

        try
        {
            var authHeader = await _ghCredentials.TryGetAsync(gh, cancellationToken).ConfigureAwait(false);
            var newSha = await _refResolver.ResolveAsync(gh.Owner, gh.RepoName, branch!, authHeader, cancellationToken).ConfigureAwait(false);

            updates.Add(new PinUpdate(
                EntryName: entry.ResolvedName,
                Kind: SourceKind.GitHub,
                DiskEntryIndex: entry.OriginalDiskEntryIndex!.Value,
                ArrayElementIndex: entry.OriginalArrayElementIndex,
                OldCommit: gh.Commit ?? string.Empty,
                NewCommit: newSha,
                Branch: branch!,
                GitHub: new GitHubPinInfo(gh.Owner, gh.RepoName, gh.Path),
                Azdo: null));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to resolve branch '{Branch}' for entry '{Name}'", branch, entry.ResolvedName);
            errors.Add((entry.ResolvedName, ex.Message));
        }
    }

    private async Task PinAzdoAsync(
        ConduitEntry entry,
        AzdoSource azdo,
        List<PinUpdate> updates,
        List<(string Name, string Reason)> skipped,
        List<(string Name, string Error)> errors,
        Dictionary<string, string> defaultBranchCache,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(azdo.Commit) && string.IsNullOrWhiteSpace(azdo.Branch) && string.IsNullOrWhiteSpace(azdo.Tag))
        {
            skipped.Add((entry.ResolvedName,
                $"already pinned to {Shorten(azdo.Commit!)}. Run 'conduit unpin --entry {entry.ResolvedName}' to put it back on a branch, then re-pin to refresh."));
            return;
        }

        // Explicit branch/tag wins; otherwise discover the default branch.
        string? refValue;
        string refKind;
        if (!string.IsNullOrWhiteSpace(azdo.Branch))
        {
            refValue = azdo.Branch;
            refKind = "branch";
        }
        else if (!string.IsNullOrWhiteSpace(azdo.Tag))
        {
            refValue = azdo.Tag;
            refKind = "tag";
        }
        else
        {
            var cacheKey = "azdo:" + azdo.Slug;
            if (!defaultBranchCache.TryGetValue(cacheKey, out var discovered))
            {
                try
                {
                    discovered = await _azdoRefResolver.GetDefaultBranchAsync(azdo, cancellationToken).ConfigureAwait(false);
                    defaultBranchCache[cacheKey] = discovered;
                    _logger.LogInformation("Discovered default branch for {Slug}: {Branch}", azdo.Slug, discovered);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Failed to discover default branch for {Slug} (entry '{Name}')", azdo.Slug, entry.ResolvedName);
                    errors.Add((entry.ResolvedName, $"failed to discover default branch: {ex.Message}"));
                    return;
                }
            }

            refValue = discovered;
            refKind = "branch";
        }

        try
        {
            var newSha = await _azdoRefResolver.ResolveAsync(azdo, refValue!, refKind, cancellationToken).ConfigureAwait(false);

            updates.Add(new PinUpdate(
                EntryName: entry.ResolvedName,
                Kind: SourceKind.Azdo,
                DiskEntryIndex: entry.OriginalDiskEntryIndex!.Value,
                ArrayElementIndex: entry.OriginalArrayElementIndex,
                OldCommit: azdo.Commit ?? string.Empty,
                NewCommit: newSha,
                Branch: refKind == "branch" ? refValue! : string.Empty,
                GitHub: null,
                Azdo: new AzdoPinInfo(azdo.ResolvedComponents, azdo.Path, RefKind: refKind, RefValue: refValue!)));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to resolve {Kind} '{Ref}' for entry '{Name}'", refKind, refValue, entry.ResolvedName);
            errors.Add((entry.ResolvedName, ex.Message));
        }
    }

    private async Task<string?> ApplyUpdatesAsync(string manifestPath, List<PinUpdate> updates, CancellationToken cancellationToken)
    {
        // Fast path: if every update only needs a string-leaf replacement
        // (the on-disk source is already a string we'll rewrite into a new
        // string URL), we route through the surgical JSONC patcher which
        // preserves comments, trailing commas, blank lines, and indentation.
        // For object-shaped sources we still need to delete the 'branch' key,
        // which the patcher can't do, so we fall back to the full RewriteAsync.
        var stringEdits = TryBuildStringLeafEdits(manifestPath, updates);
        if (stringEdits is not null)
        {
            var (patched, surgicalBackup) = await _writer.ReplaceStringLeavesAsync(manifestPath, stringEdits, cancellationToken).ConfigureAwait(false);
            if (patched)
            {
                return surgicalBackup;
            }
            // Patcher refused (unexpected after the shape check); fall through.
        }

        // RewriteAsync: handles object/string/array shapes via the full JsonNode
        // model. The trade-off vs the surgical patcher: comments and trailing
        // commas in the source file are lost on re-emit, because System.Text.Json
        // doesn't preserve trivia. This kicks in only when at least one entry
        // needs an object-key mutation (e.g. dropping 'branch' on pin).
        return await _writer.RewriteAsync(manifestPath, root =>
        {
            if (root["entries"] is not JsonArray entriesArray)
            {
                return;
            }

            foreach (var update in updates)
            {
                if (update.DiskEntryIndex < 0 || update.DiskEntryIndex >= entriesArray.Count)
                {
                    continue;
                }

                if (entriesArray[update.DiskEntryIndex] is not JsonObject entryObj)
                {
                    continue;
                }

                ApplyOnePin(entryObj, update);
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    private static void ApplyOnePin(JsonObject entryObj, PinUpdate update)
    {
        var sourceNode = entryObj["source"];

        // Walk into an array element if the entry was synthesized from one.
        if (update.ArrayElementIndex is { } arrayIndex)
        {
            if (sourceNode is not JsonArray sourceArr || arrayIndex < 0 || arrayIndex >= sourceArr.Count)
            {
                return;
            }

            sourceArr[arrayIndex] = RewriteSourceNode(sourceArr[arrayIndex], update);
            return;
        }

        entryObj["source"] = RewriteSourceNode(sourceNode, update);
    }

    private static JsonNode? RewriteSourceNode(JsonNode? sourceNode, PinUpdate update)
    {
        switch (sourceNode)
        {
            case JsonValue val when val.GetValueKind() == JsonValueKind.String:
                {
                    var raw = val.GetValue<string>();
                    var rewritten = BuildPinnedStringSource(raw, update);
                    return rewritten is null ? sourceNode : JsonValue.Create(rewritten);
                }

            case JsonObject obj:
                ApplyObjectPin(obj, update);
                return obj;

            default:
                return sourceNode;
        }
    }

    private static void ApplyObjectPin(JsonObject sourceObj, PinUpdate update)
    {
        // URL-native pin: store the SHA and remove the branch from the
        // manifest. The user can run `conduit unpin` to restore a branch.
        sourceObj["commit"] = update.NewCommit;
        sourceObj.Remove("branch");
        // AzDO sources can pin via 'commit' too; clear branch/tag for symmetry.
        if (update.Kind == SourceKind.Azdo)
        {
            sourceObj.Remove("tag");
        }
    }

    private static string? BuildPinnedStringSource(string raw, PinUpdate update)
    {
        // Preserve a trailing ' -> Alias' suffix if present (the inferrer
        // strips it during URL parsing).
        var (uri, aliasSuffix) = SplitOffAliasSuffix(raw);

        string newUri = update.Kind switch
        {
            SourceKind.GitHub => BuildPinnedGitHubUrl(uri, update),
            SourceKind.Azdo => BuildPinnedAzdoUrl(uri, update),
            _ => raw,
        };

        return aliasSuffix is null ? newUri : newUri + aliasSuffix;
    }

    private static string BuildPinnedGitHubUrl(string raw, PinUpdate update)
    {
        var gh = update.GitHub ?? throw new InvalidOperationException("GitHub pin info missing.");
        var url = new StringBuilder("https://github.com/").Append(gh.Owner).Append('/').Append(gh.Repo);
        url.Append("/tree/").Append(update.NewCommit);
        if (!string.IsNullOrEmpty(gh.Path))
        {
            url.Append('/').Append(gh.Path.TrimStart('/'));
        }
        return url.ToString();
    }

    private static string BuildPinnedAzdoUrl(string raw, PinUpdate update)
    {
        var azdo = update.Azdo ?? throw new InvalidOperationException("AzDO pin info missing.");
        var components = azdo.Components;
        var baseUrl = components.BaseUrl.AbsoluteUri.TrimEnd('/');
        var url = new StringBuilder(baseUrl)
            .Append('/').Append(components.Organization)
            .Append('/').Append(components.Project)
            .Append("/_git/").Append(components.Repo);

        var queryParts = new List<string>(2);
        if (!string.IsNullOrEmpty(azdo.Path))
        {
            queryParts.Add("path=/" + Uri.EscapeDataString(azdo.Path.TrimStart('/')).Replace("%2F", "/", StringComparison.Ordinal));
        }
        queryParts.Add("version=GC" + update.NewCommit);

        url.Append('?').Append(string.Join('&', queryParts));
        return url.ToString();
    }

    /// <summary>
    ///     Strips a trailing <c> -&gt; Alias</c> suffix from a URI shorthand
    ///     string (if present and the tail matches the entry-name charset),
    ///     returning the bare URI and the suffix separately. The suffix is
    ///     reattached after URL rewriting so pin/unpin don't lose aliases.
    /// </summary>
    private static (string Uri, string? AliasSuffix) SplitOffAliasSuffix(string raw)
    {
        const string Sep = " -> ";
        var idx = raw.LastIndexOf(Sep, StringComparison.Ordinal);
        if (idx <= 0)
        {
            return (raw, null);
        }

        var tail = raw[(idx + Sep.Length)..];
        // Same charset as the converter accepts.
        foreach (var c in tail)
        {
            var ok = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c is '-' or '_' or '.';
            if (!ok) return (raw, null);
        }

        return (raw[..idx], raw[idx..]);
    }

    private void RenderReport(
        string verb,
        string manifestPath,
        string? backupPath,
        List<PinUpdate> updates,
        List<(string Name, string Reason)> skipped,
        List<(string Name, string Error)> errors,
        bool dryRun,
        OutputFormat output)
    {
        if (output == OutputFormat.Json)
        {
            var dto = new
            {
                verb,
                manifest = manifestPath,
                backup = backupPath,
                dryRun,
                updates = updates.Select(u => new
                {
                    name = u.EntryName,
                    branch = u.Branch,
                    oldCommit = string.IsNullOrEmpty(u.OldCommit) ? null : u.OldCommit,
                    newCommit = u.NewCommit,
                }),
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
            var branchLabel = string.IsNullOrEmpty(u.Branch) ? string.Empty : $"  ({u.Branch})";
            Console.WriteLine($"  {arrow} {u.EntryName}{branchLabel}  {fromLabel} -> {_style.Cyan(Shorten(u.NewCommit))}");
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

            Console.WriteLine($"  {_style.Dim("Note: pinning rewrites source URLs to the canonical 'tree/<sha>/<path>' form (GitHub) or '?version=GC<sha>' (AzDO). Run 'conduit unpin' to switch the affected entries back to branch tracking.")}");
        }
    }

    private static string Shorten(string sha) =>
        string.IsNullOrEmpty(sha) || sha.Length <= 12 ? sha : sha[..12];

    /// <summary>
    ///     Inspects the on-disk manifest and, if every update targets a
    ///     string-leaf source (top-level scalar or array element), returns a
    ///     list of <see cref="JsonValuePatcher.StringEdit"/>s the trivia-
    ///     preserving surgical patcher can apply. Returns <see langword="null"/>
    ///     when at least one update needs an object-key mutation (which the
    ///     patcher can't do) or when the manifest shape doesn't line up.
    /// </summary>
    private List<JsonValuePatcher.StringEdit>? TryBuildStringLeafEdits(string manifestPath, List<PinUpdate> updates)
    {
        Dictionary<(int Entry, int? Element), JsonValueKind>? shapes;
        try
        {
            shapes = MapDiskSourceShapes(manifestPath);
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            _logger.LogDebug(ex, "Could not pre-map disk source shapes; falling back to full rewrite.");
            return null;
        }

        if (shapes is null)
        {
            return null;
        }

        var edits = new List<JsonValuePatcher.StringEdit>(updates.Count);
        foreach (var u in updates)
        {
            if (!shapes.TryGetValue((u.DiskEntryIndex, u.ArrayElementIndex), out var kind) || kind != JsonValueKind.String)
            {
                return null;
            }

            var newValue = u.Kind switch
            {
                SourceKind.GitHub => BuildPinnedGitHubUrl(string.Empty, u),
                SourceKind.Azdo => BuildPinnedAzdoUrl(string.Empty, u),
                _ => null,
            };

            if (newValue is null)
            {
                return null;
            }

            edits.Add(new JsonValuePatcher.StringEdit(
                Path: BuildDiskJsonPath(u.DiskEntryIndex, u.ArrayElementIndex),
                NewValue: newValue));
        }

        return edits;
    }

    /// <summary>
    ///     Walks the on-disk manifest once and records, per (entry-index,
    ///     array-element-index) the <see cref="JsonValueKind"/> of the
    ///     <c>source</c> at that position. Used by the patcher fast-path to
    ///     decide whether each update can be applied as a leaf replacement.
    /// </summary>
    private static Dictionary<(int Entry, int? Element), JsonValueKind>? MapDiskSourceShapes(string manifestPath)
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

        var map = new Dictionary<(int Entry, int? Element), JsonValueKind>();
        for (var i = 0; i < entries.Count; i++)
        {
            if (entries[i] is not JsonObject entryObj)
            {
                continue;
            }

            var sourceNode = entryObj["source"];
            switch (sourceNode)
            {
                case JsonValue val:
                    map[(i, null)] = val.GetValueKind();
                    break;

                case JsonObject:
                    map[(i, null)] = JsonValueKind.Object;
                    break;

                case JsonArray arr:
                    for (var j = 0; j < arr.Count; j++)
                    {
                        var element = arr[j];
                        switch (element)
                        {
                            case JsonValue elemVal:
                                map[(i, j)] = elemVal.GetValueKind();
                                break;
                            case JsonObject:
                                map[(i, j)] = JsonValueKind.Object;
                                break;
                            default:
                                map[(i, j)] = JsonValueKind.Null;
                                break;
                        }
                    }
                    break;
            }
        }

        return map;
    }

    /// <summary>
    ///     Returns the dotted JSON-pointer-ish path used by
    ///     <see cref="JsonValuePatcher"/>: <c>entries[N].source</c> for
    ///     top-level sources, <c>entries[N].source[M]</c> for array elements.
    /// </summary>
    internal static string BuildDiskJsonPath(int diskEntryIndex, int? arrayElementIndex) =>
        arrayElementIndex is { } j
            ? $"entries[{diskEntryIndex}].source[{j}]"
            : $"entries[{diskEntryIndex}].source";

    /// <summary>One queued pin write-back for a single entry.</summary>
    internal sealed record PinUpdate(
        string EntryName,
        SourceKind Kind,
        int DiskEntryIndex,
        int? ArrayElementIndex,
        string OldCommit,
        string NewCommit,
        string Branch,
        GitHubPinInfo? GitHub,
        AzdoPinInfo? Azdo);

    internal sealed record GitHubPinInfo(string Owner, string Repo, string? Path);

    internal sealed record AzdoPinInfo(AzdoUrlComponents Components, string? Path, string RefKind, string RefValue);

    internal enum SourceKind { GitHub, Azdo }
}
