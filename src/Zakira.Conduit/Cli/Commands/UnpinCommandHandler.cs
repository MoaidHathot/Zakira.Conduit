using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Zakira.Conduit.Manifest;
using Zakira.Conduit.Sources.Azdo;
using Zakira.Conduit.Sources.GitHub;

namespace Zakira.Conduit.Cli.Commands;

/// <summary>
///     Implements <c>conduit unpin</c>: restores branch tracking on entries
///     that were previously pinned by <c>conduit pin</c>. For GitHub sources
///     that means rewriting the URL from <c>tree/&lt;sha&gt;/&lt;path&gt;</c>
///     back to <c>tree/&lt;branch&gt;/&lt;path&gt;</c>; for AzDO it means
///     swapping <c>?version=GC&lt;sha&gt;</c> for <c>?version=GB&lt;branch&gt;</c>.
///     Object-form sources have their <c>commit</c> field removed and
///     <c>branch</c> set.
/// </summary>
internal sealed class UnpinCommandHandler
{
    private readonly IManifestLocator _locator;
    private readonly IManifestLoader _loader;
    private readonly IManifestWriter _writer;
    private readonly IGitHubRefResolver _refResolver;
    private readonly Sources.GitHub.Credentials.ChainedGitHubCredentialProvider _ghCredentials;
    private readonly IAzdoRefResolver _azdoRefResolver;
    private readonly ConsoleStyle _style;
    private readonly ILogger<UnpinCommandHandler> _logger;

    public UnpinCommandHandler(
        IManifestLocator locator,
        IManifestLoader loader,
        IManifestWriter writer,
        IGitHubRefResolver refResolver,
        Sources.GitHub.Credentials.ChainedGitHubCredentialProvider ghCredentials,
        IAzdoRefResolver azdoRefResolver,
        ConsoleStyle style,
        ILogger<UnpinCommandHandler> logger)
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

    public async Task<int> InvokeAsync(
        string? manifest,
        IReadOnlyList<string> entries,
        string? toBranch,
        bool toDefault,
        bool dryRun,
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

        var filter = entries.Count > 0
            ? new HashSet<string>(entries, StringComparer.OrdinalIgnoreCase)
            : null;

        var rewrites = new List<UnpinRewrite>();
        var skipped = new List<(string Name, string Reason)>();
        var errors = new List<(string Name, string Error)>();
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
                skipped.Add((entry.ResolvedName,
                    "entry has no on-disk position recorded by the inference pass; this is a bug."));
                continue;
            }

            switch (entry.Source)
            {
                case GitHubSource gh:
                    await UnpinGitHubAsync(entry, gh, rewrites, skipped, errors, toBranch, toDefault, defaultBranchCache, cancellationToken).ConfigureAwait(false);
                    break;

                case AzdoSource azdo:
                    await UnpinAzdoAsync(entry, azdo, rewrites, skipped, errors, toBranch, toDefault, defaultBranchCache, cancellationToken).ConfigureAwait(false);
                    break;

                default:
                    skipped.Add((entry.ResolvedName, $"unsupported source kind '{entry.Source.Kind}'"));
                    break;
            }
        }

        string? backupPath = null;
        if (rewrites.Count > 0 && !dryRun)
        {
            backupPath = await _writer.RewriteAsync(manifestPath, root =>
            {
                if (root["entries"] is not JsonArray entriesArray)
                {
                    return;
                }

                foreach (var u in rewrites)
                {
                    if (u.DiskEntryIndex < 0 || u.DiskEntryIndex >= entriesArray.Count)
                    {
                        continue;
                    }

                    if (entriesArray[u.DiskEntryIndex] is not JsonObject entryObj)
                    {
                        continue;
                    }

                    ApplyOneUnpin(entryObj, u);
                }
            }, cancellationToken).ConfigureAwait(false);
        }

        RenderReport(manifestPath, backupPath, rewrites, skipped, errors, dryRun, output);
        return errors.Count == 0 ? 0 : 1;
    }

    private async Task UnpinGitHubAsync(
        ConduitEntry entry,
        GitHubSource gh,
        List<UnpinRewrite> rewrites,
        List<(string Name, string Reason)> skipped,
        List<(string Name, string Error)> errors,
        string? toBranch,
        bool toDefault,
        Dictionary<string, string> defaultBranchCache,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(gh.Commit))
        {
            skipped.Add((entry.ResolvedName, "not pinned (no commit set) - nothing to unpin."));
            return;
        }

        var branch = await ResolveTargetBranchAsync(
            entry,
            isExplicit: !string.IsNullOrWhiteSpace(toBranch),
            explicitBranch: toBranch,
            useDefaultDiscovery: toDefault,
            discoverDefault: async () =>
            {
                var authHeader = await _ghCredentials.TryGetAsync(gh, cancellationToken).ConfigureAwait(false);
                return await _refResolver.GetDefaultBranchAsync(gh.Owner, gh.RepoName, authHeader, cancellationToken).ConfigureAwait(false);
            },
            cacheKey: "github:" + gh.Slug,
            cache: defaultBranchCache,
            errors: errors,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (branch is null)
        {
            return;
        }

        rewrites.Add(new UnpinRewrite(
            EntryName: entry.ResolvedName,
            Kind: PinUpdateCommandHandler.SourceKind.GitHub,
            DiskEntryIndex: entry.OriginalDiskEntryIndex!.Value,
            ArrayElementIndex: entry.OriginalArrayElementIndex,
            OldCommit: gh.Commit!,
            NewBranch: branch,
            GitHub: new PinUpdateCommandHandler.GitHubPinInfo(gh.Owner, gh.RepoName, gh.Path),
            Azdo: null));
    }

    private async Task UnpinAzdoAsync(
        ConduitEntry entry,
        AzdoSource azdo,
        List<UnpinRewrite> rewrites,
        List<(string Name, string Reason)> skipped,
        List<(string Name, string Error)> errors,
        string? toBranch,
        bool toDefault,
        Dictionary<string, string> defaultBranchCache,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(azdo.Commit))
        {
            skipped.Add((entry.ResolvedName, "not pinned (no commit set) - nothing to unpin."));
            return;
        }

        var branch = await ResolveTargetBranchAsync(
            entry,
            isExplicit: !string.IsNullOrWhiteSpace(toBranch),
            explicitBranch: toBranch,
            useDefaultDiscovery: toDefault,
            discoverDefault: () => _azdoRefResolver.GetDefaultBranchAsync(azdo, cancellationToken),
            cacheKey: "azdo:" + azdo.Slug,
            cache: defaultBranchCache,
            errors: errors,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (branch is null)
        {
            return;
        }

        rewrites.Add(new UnpinRewrite(
            EntryName: entry.ResolvedName,
            Kind: PinUpdateCommandHandler.SourceKind.Azdo,
            DiskEntryIndex: entry.OriginalDiskEntryIndex!.Value,
            ArrayElementIndex: entry.OriginalArrayElementIndex,
            OldCommit: azdo.Commit!,
            NewBranch: branch,
            GitHub: null,
            Azdo: new PinUpdateCommandHandler.AzdoPinInfo(azdo.ResolvedComponents, azdo.Path, RefKind: "branch", RefValue: branch)));
    }

    /// <summary>
    ///     Centralises the "which branch should we unpin to?" decision.
    ///     Priority: explicit <c>--to</c> wins; otherwise <c>--to-default</c>
    ///     (or its implicit form when neither flag is set) discovers via API;
    ///     bare-default fallback is the literal "main".
    /// </summary>
    private async Task<string?> ResolveTargetBranchAsync(
        ConduitEntry entry,
        bool isExplicit,
        string? explicitBranch,
        bool useDefaultDiscovery,
        Func<Task<string>> discoverDefault,
        string cacheKey,
        Dictionary<string, string> cache,
        List<(string Name, string Error)> errors,
        CancellationToken cancellationToken)
    {
        if (isExplicit)
        {
            return explicitBranch;
        }

        // Default behaviour (no flags) and --to-default both go to API
        // discovery. The literal "main" is only the fallback if discovery
        // fails for any reason (offline, 404, etc.).
        if (cache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        try
        {
            var discovered = await discoverDefault().ConfigureAwait(false);
            cache[cacheKey] = discovered;
            _logger.LogInformation("Discovered default branch for '{Entry}': {Branch}", entry.ResolvedName, discovered);
            return discovered;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (useDefaultDiscovery)
            {
                // User explicitly opted into discovery; surface the error
                // rather than silently falling back.
                _logger.LogError(ex, "Default-branch discovery failed for entry '{Entry}'", entry.ResolvedName);
                errors.Add((entry.ResolvedName, $"default-branch discovery failed: {ex.Message}"));
                return null;
            }

            // Implicit default (no flag): try the conventional 'main' as a
            // last-resort fallback so unpin doesn't require network in the
            // common case.
            _logger.LogWarning(ex, "Default-branch discovery failed for entry '{Entry}'; falling back to 'main'.", entry.ResolvedName);
            cache[cacheKey] = "main";
            return "main";
        }
    }

    private static void ApplyOneUnpin(JsonObject entryObj, UnpinRewrite rewrite)
    {
        var sourceNode = entryObj["source"];

        if (rewrite.ArrayElementIndex is { } arrayIndex)
        {
            if (sourceNode is not JsonArray sourceArr || arrayIndex < 0 || arrayIndex >= sourceArr.Count)
            {
                return;
            }

            sourceArr[arrayIndex] = RewriteSourceNode(sourceArr[arrayIndex], rewrite);
            return;
        }

        entryObj["source"] = RewriteSourceNode(sourceNode, rewrite);
    }

    private static JsonNode? RewriteSourceNode(JsonNode? sourceNode, UnpinRewrite rewrite)
    {
        switch (sourceNode)
        {
            case JsonValue val when val.GetValueKind() == JsonValueKind.String:
                {
                    var raw = val.GetValue<string>();
                    var rewritten = BuildUnpinnedStringSource(raw, rewrite);
                    return rewritten is null ? sourceNode : JsonValue.Create(rewritten);
                }

            case JsonObject obj:
                ApplyObjectUnpin(obj, rewrite);
                return obj;

            default:
                return sourceNode;
        }
    }

    private static void ApplyObjectUnpin(JsonObject sourceObj, UnpinRewrite rewrite)
    {
        sourceObj["branch"] = rewrite.NewBranch;
        sourceObj.Remove("commit");
    }

    private static string? BuildUnpinnedStringSource(string raw, UnpinRewrite rewrite)
    {
        var (uri, aliasSuffix) = SplitOffAliasSuffix(raw);

        string newUri = rewrite.Kind switch
        {
            PinUpdateCommandHandler.SourceKind.GitHub => BuildUnpinnedGitHubUrl(uri, rewrite),
            PinUpdateCommandHandler.SourceKind.Azdo => BuildUnpinnedAzdoUrl(uri, rewrite),
            _ => raw,
        };

        return aliasSuffix is null ? newUri : newUri + aliasSuffix;
    }

    private static string BuildUnpinnedGitHubUrl(string raw, UnpinRewrite rewrite)
    {
        var gh = rewrite.GitHub ?? throw new InvalidOperationException("GitHub unpin info missing.");
        var url = new StringBuilder("https://github.com/").Append(gh.Owner).Append('/').Append(gh.Repo);
        url.Append("/tree/").Append(rewrite.NewBranch);
        if (!string.IsNullOrEmpty(gh.Path))
        {
            url.Append('/').Append(gh.Path.TrimStart('/'));
        }
        return url.ToString();
    }

    private static string BuildUnpinnedAzdoUrl(string raw, UnpinRewrite rewrite)
    {
        var azdo = rewrite.Azdo ?? throw new InvalidOperationException("AzDO unpin info missing.");
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
        queryParts.Add("version=GB" + rewrite.NewBranch);

        url.Append('?').Append(string.Join('&', queryParts));
        return url.ToString();
    }

    private static (string Uri, string? AliasSuffix) SplitOffAliasSuffix(string raw)
    {
        const string Sep = " -> ";
        var idx = raw.LastIndexOf(Sep, StringComparison.Ordinal);
        if (idx <= 0)
        {
            return (raw, null);
        }

        var tail = raw[(idx + Sep.Length)..];
        foreach (var c in tail)
        {
            var ok = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c is '-' or '_' or '.';
            if (!ok) return (raw, null);
        }

        return (raw[..idx], raw[idx..]);
    }

    private void RenderReport(
        string manifestPath,
        string? backupPath,
        List<UnpinRewrite> rewrites,
        List<(string Name, string Reason)> skipped,
        List<(string Name, string Error)> errors,
        bool dryRun,
        OutputFormat output)
    {
        if (output == OutputFormat.Json)
        {
            var dto = new
            {
                manifest = manifestPath,
                backup = backupPath,
                dryRun,
                unpinned = rewrites.Select(r => new
                {
                    name = r.EntryName,
                    fromCommit = r.OldCommit,
                    toBranch = r.NewBranch,
                }),
                skipped = skipped.Select(s => new { name = s.Name, reason = s.Reason }),
                errors = errors.Select(e => new { name = e.Name, error = e.Error }),
            };
            Console.WriteLine(JsonSerializer.Serialize(dto, ManifestJson.WriteOptions));
            return;
        }

        Console.WriteLine();
        Console.WriteLine(_style.Bold($"unpin report for {manifestPath}"));
        Console.WriteLine(new string('-', 60));

        foreach (var r in rewrites)
        {
            Console.WriteLine($"  {_style.Cyan("~")} {r.EntryName}  {Shorten(r.OldCommit)} -> {_style.Cyan(r.NewBranch)} (branch)");
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
        Console.WriteLine($"  {rewrites.Count} unpinned, {skipped.Count} skipped, {errors.Count} failed.");

        if (dryRun)
        {
            Console.WriteLine($"  {_style.Yellow("(dry-run: manifest was not modified.)")}");
        }
        else if (rewrites.Count > 0 && !string.IsNullOrEmpty(backupPath))
        {
            Console.WriteLine($"  {_style.Dim($"Backup of the original manifest: {backupPath}")}");
        }
    }

    private static string Shorten(string sha) =>
        string.IsNullOrEmpty(sha) || sha.Length <= 12 ? sha : sha[..12];

    internal sealed record UnpinRewrite(
        string EntryName,
        PinUpdateCommandHandler.SourceKind Kind,
        int DiskEntryIndex,
        int? ArrayElementIndex,
        string OldCommit,
        string NewBranch,
        PinUpdateCommandHandler.GitHubPinInfo? GitHub,
        PinUpdateCommandHandler.AzdoPinInfo? Azdo);
}
