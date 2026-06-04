using Zakira.Conduit.Strategies;

namespace Zakira.Conduit.Manifest;

/// <summary>
///     Validates a deserialized <see cref="ConduitManifest"/> for structural and
///     semantic correctness. This is intentionally pure (no IO), so it can be
///     unit-tested in isolation.
/// </summary>
public static class ManifestValidator
{
    /// <summary>
    ///     Returns the list of validation errors. Empty when the manifest is valid.
    /// </summary>
    /// <remarks>
    ///     Overload that does not consult any <see cref="IPlanStrategyRegistry"/>;
    ///     strategy fields are validated only superficially (shape of the
    ///     name string). Most callers should use the
    ///     <see cref="Validate(ConduitManifest?, IPlanStrategyRegistry?)"/>
    ///     overload so unknown strategies are flagged with the list of
    ///     registered names.
    /// </remarks>
    public static IReadOnlyList<string> Validate(ConduitManifest? manifest) =>
        Validate(manifest, strategyRegistry: null);

    /// <summary>
    ///     Returns the list of validation errors. Empty when the manifest is valid.
    /// </summary>
    /// <param name="manifest">The manifest to validate.</param>
    /// <param name="strategyRegistry">
    ///     Optional registry used to resolve each entry's <c>strategy</c>
    ///     field. When supplied, unknown strategies become a hard error and
    ///     each strategy's own
    ///     <see cref="IPlanStrategy.ValidateEntry"/> is invoked.
    /// </param>
    public static IReadOnlyList<string> Validate(ConduitManifest? manifest, IPlanStrategyRegistry? strategyRegistry)
    {
        var errors = new List<string>();

        if (manifest is null)
        {
            errors.Add("Manifest is null.");
            return errors;
        }

        if (manifest.Version != ManifestNames.CurrentSchemaVersion)
        {
            errors.Add($"Unsupported manifest version '{manifest.Version}'. This release supports version {ManifestNames.CurrentSchemaVersion}.");
        }

        if (manifest.Entries.Count == 0)
        {
            errors.Add("Manifest has no entries.");
            return errors;
        }

        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < manifest.Entries.Count; i++)
        {
            ValidateEntry(manifest.Entries[i], i, seenNames, strategyRegistry, errors);
        }

        // Cross-entry static destination collisions. When a strategy is
        // registered, its EnumerateStaticDestinations defines the collision
        // surface (content-dependent strategies return empty and are
        // skipped here; their collisions surface at sync time instead).
        if (strategyRegistry is not null)
        {
            ValidateCrossEntryDestinations(manifest, strategyRegistry, errors);
        }
        else
        {
            // Legacy callers without a registry get the original wrap-only
            // collision check, preserved verbatim from the pre-strategy code.
            ValidateLegacyCrossEntryDestinations(manifest, errors);
        }

        return errors;
    }

    private static void ValidateEntry(
        ConduitEntry entry,
        int index,
        HashSet<string> seenNames,
        IPlanStrategyRegistry? strategyRegistry,
        List<string> errors)
    {
        var prefix = $"entries[{index}]";

        if (string.IsNullOrWhiteSpace(entry.Name))
        {
            errors.Add(
                $"{prefix}.name could not be determined. Either set 'name' on the entry, supply an explicit alias " +
                $"(\"...source -> Name\" or {{ \"source\": ..., \"as\": \"Name\" }}), or use a source kind whose " +
                $"identity yields a default name (a GitHub/AzDO repo, or a local directory).");
        }
        else if (!IsValidEntryName(entry.Name))
        {
            errors.Add($"{prefix}.name '{entry.Name}' contains invalid characters; only letters, digits, '-', '_' and '.' are allowed.");
        }
        else if (!seenNames.Add(entry.Name))
        {
            errors.Add($"{prefix}.name '{entry.Name}' is duplicated.");
        }

        if (entry.Targets is null || entry.Targets.Count == 0)
        {
            errors.Add($"{prefix}.targets must contain at least one directory path.");
        }
        else
        {
            for (var t = 0; t < entry.Targets.Count; t++)
            {
                var target = entry.Targets[t];
                if (target is null || string.IsNullOrWhiteSpace(target.Path))
                {
                    errors.Add($"{prefix}.targets[{t}] must be a non-empty path.");
                }
            }

            // Per-target `as` aliases only make sense when the entry produces
            // exactly one content unit AND the chosen strategy honours them.
            // The wrap strategy is the only one that does in v1; other
            // strategies emit their own (strategy-specific) error from
            // IPlanStrategy.ValidateEntry. This check stays here for
            // legacy/no-registry callers and to keep the original error
            // message for the multi-path wrap case.
            var aliasedTargets = entry.Targets.Count(t => t is not null && !string.IsNullOrWhiteSpace(t.As));
            var sourceProducesMultiple = entry.Source switch
            {
                GitHubSource gh => gh.EffectivePaths.Count > 1,
                LocalDirectorySource local => local.EffectivePaths.Count > 1,
                AzdoSource azdo => azdo.EffectivePaths.Count > 1,
                _ => false,
            };

            if (aliasedTargets > 0 && sourceProducesMultiple && IsLegacyWrapStrategy(entry))
            {
                errors.Add($"{prefix}.targets: per-target 'as' aliases are not allowed on multi-path entries (the source produces multiple destinations).");
            }
        }

        // Strategy-level validation.
        if (entry.Strategy is { } strategyName && !string.IsNullOrWhiteSpace(strategyName))
        {
            if (strategyRegistry is not null)
            {
                if (!strategyRegistry.IsRegistered(strategyName))
                {
                    errors.Add($"{prefix}.strategy '{strategyName}' is not a registered strategy. Available: {string.Join(", ", strategyRegistry.Names)}.");
                }
                else
                {
                    var strategy = strategyRegistry.Resolve(strategyName);
                    foreach (var strategyError in strategy.ValidateEntry(entry, index))
                    {
                        errors.Add(strategyError);
                    }
                }
            }
            // Without a registry we can't validate the name (legacy callers
            // never had strategies). Skip silently rather than false-flag.
        }
        else if (strategyRegistry is not null)
        {
            // No explicit strategy -> default to "wrap". Let it run its own
            // validation in case it gains rules in the future.
            strategyRegistry.Resolve(StrategyNames.Wrap)
                .ValidateEntry(entry, index)
                .ToList()
                .ForEach(errors.Add);
        }

        // skills/harness fields only make sense for the skills strategy.
        var effectiveStrategy = string.IsNullOrWhiteSpace(entry.Strategy) ? StrategyNames.Wrap : entry.Strategy!.Trim();
        if (entry.Skills is { Count: > 0 } && !string.Equals(effectiveStrategy, StrategyNames.Skills, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"{prefix}.skills is only valid with strategy 'skills'; remove it or set strategy: 'skills'.");
        }

        if (entry.Harness is not null && !string.Equals(effectiveStrategy, StrategyNames.Skills, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"{prefix}.harness is only valid with strategy 'skills'; remove it or set strategy: 'skills'.");
        }

        switch (entry.Source)
        {
            case null:
                errors.Add($"{prefix}.source is required.");
                break;

            case GitHubSource gh:
                ValidateGitHubSource(gh, prefix, errors);
                break;

            case LocalDirectorySource local:
                ValidateLocalSource(local, prefix, errors);
                break;

            case AzdoSource azdo:
                ValidateAzdoSource(azdo, prefix, errors);
                break;

            case UriBasedSource:
                errors.Add($"{prefix}.source: 'uri'-shaped source reached the validator without being resolved. This is a wiring bug; ensure the manifest loader's inference coordinator is registered.");
                break;

            case AliasedSource:
                errors.Add($"{prefix}.source: aliased wrapper source reached the validator without being unwrapped. This is a wiring bug; ensure the manifest loader's inference coordinator is registered.");
                break;

            default:
                errors.Add($"{prefix}.source has unsupported kind '{entry.Source.Kind}'.");
                break;
        }
    }

    private static bool IsLegacyWrapStrategy(ConduitEntry entry) =>
        string.IsNullOrWhiteSpace(entry.Strategy) ||
        string.Equals(entry.Strategy, StrategyNames.Wrap, StringComparison.OrdinalIgnoreCase);

    private static void ValidateGitHubSource(GitHubSource source, string prefix, List<string> errors)
    {
        if (!GitHubRepoReference.TryParse(source.Repo, out _, out _, out var parseError))
        {
            errors.Add($"{prefix}.source.repo: {parseError}");
        }

        // `branch` and `commit` may coexist: when both are set, `branch` is the
        // tracking intent (used by `conduit pin` / `conduit update`) and
        // `commit` is the snapshot the synchronizer actually fetches. Pinning
        // is therefore non-lossy: you keep the branch metadata for later refresh.

        if (source.Path is not null && source.Paths is { Count: > 0 })
        {
            errors.Add($"{prefix}.source: 'path' and 'paths' are mutually exclusive.");
        }

        if (source.Auth is { Count: > 0 })
        {
            var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "env", "gh", "pat", "anonymous", "options" };
            foreach (var mode in source.Auth)
            {
                if (!allowed.Contains(mode))
                {
                    errors.Add($"{prefix}.source.auth: unknown mode '{mode}'. Allowed: env, gh, pat, anonymous.");
                }
            }
        }

        ValidateSubPaths(source.EffectivePaths, $"{prefix}.source", requireRepoRelative: true, errors);
        ValidateFilterPatterns(source.Include, $"{prefix}.source.include", errors);
        ValidateFilterPatterns(source.Exclude, $"{prefix}.source.exclude", errors);
    }

    private static void ValidateLocalSource(LocalDirectorySource source, string prefix, List<string> errors)
    {
        if (source.Path is not null && source.Paths is { Count: > 0 })
        {
            errors.Add($"{prefix}.source: 'path' and 'paths' are mutually exclusive.");
        }

        if (source.EffectivePaths.Count == 0)
        {
            errors.Add($"{prefix}.source: a local source must declare at least one 'path' or one 'paths' entry.");
        }

        ValidateSubPaths(source.EffectivePaths, $"{prefix}.source", requireRepoRelative: false, errors);
        ValidateFilterPatterns(source.Include, $"{prefix}.source.include", errors);
        ValidateFilterPatterns(source.Exclude, $"{prefix}.source.exclude", errors);
    }

    private static void ValidateAzdoSource(AzdoSource source, string prefix, List<string> errors)
    {
        var hasUrl = !string.IsNullOrWhiteSpace(source.Url);
        var hasTriplet = !string.IsNullOrWhiteSpace(source.Organization)
                         && !string.IsNullOrWhiteSpace(source.Project)
                         && !string.IsNullOrWhiteSpace(source.Repo);

        if (hasUrl && (hasTriplet || !string.IsNullOrWhiteSpace(source.Organization) || !string.IsNullOrWhiteSpace(source.Project) || !string.IsNullOrWhiteSpace(source.Repo)))
        {
            errors.Add($"{prefix}.source: 'url' and the explicit ('organization', 'project', 'repo') triplet are mutually exclusive.");
        }
        else if (hasUrl)
        {
            if (!AzdoUrlParser.TryParse(source.Url, out _, out var parseError))
            {
                errors.Add($"{prefix}.source.url: {parseError}");
            }
        }
        else if (!hasTriplet)
        {
            errors.Add($"{prefix}.source: provide either 'url' or all of 'organization', 'project', 'repo'.");
        }

        if (!string.IsNullOrWhiteSpace(source.BaseUrl) && !Uri.TryCreate(source.BaseUrl, UriKind.Absolute, out _))
        {
            errors.Add($"{prefix}.source.baseUrl: '{source.BaseUrl}' is not a valid absolute URL.");
        }

        var refsSet = new[] { source.Branch, source.Tag, source.Commit }.Count(v => !string.IsNullOrWhiteSpace(v));
        // branch + commit may coexist (branch = intent, commit = snapshot). Same for tag + commit.
        if (refsSet > 1 && !string.IsNullOrWhiteSpace(source.Branch) && !string.IsNullOrWhiteSpace(source.Tag))
        {
            errors.Add($"{prefix}.source: 'branch' and 'tag' are mutually exclusive.");
        }

        if (source.Path is not null && source.Paths is { Count: > 0 })
        {
            errors.Add($"{prefix}.source: 'path' and 'paths' are mutually exclusive.");
        }

        if (source.Auth is { Count: > 0 })
        {
            var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "env", "az", "pat", "anonymous" };
            foreach (var mode in source.Auth)
            {
                if (!allowed.Contains(mode))
                {
                    errors.Add($"{prefix}.source.auth: unknown mode '{mode}'. Allowed: env, az, pat, anonymous.");
                }
            }
        }

        ValidateSubPaths(source.EffectivePaths, $"{prefix}.source", requireRepoRelative: true, errors);
        ValidateFilterPatterns(source.Include, $"{prefix}.source.include", errors);
        ValidateFilterPatterns(source.Exclude, $"{prefix}.source.exclude", errors);
    }

    /// <summary>
    ///     Common validation for an effective list of paths: non-empty entries,
    ///     no <c>..</c> for repo-relative paths, no leading <c>/</c> for
    ///     repo-relative paths, and (when count &gt; 1) unique resolved
    ///     basenames (alias-or-derived) so destination directories never collide.
    /// </summary>
    private static void ValidateSubPaths(IReadOnlyList<PathSpec> paths, string prefix, bool requireRepoRelative, List<string> errors)
    {
        if (paths.Count == 0)
        {
            return;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < paths.Count; i++)
        {
            var spec = paths[i];
            var slot = paths.Count == 1 && prefix.EndsWith(".source", StringComparison.Ordinal)
                ? $"{prefix}.path"
                : $"{prefix}.paths[{i}]";

            if (spec is null || string.IsNullOrWhiteSpace(spec.Path))
            {
                errors.Add($"{slot} must be a non-empty path.");
                continue;
            }

            var p = spec.Path;

            if (requireRepoRelative)
            {
                if (p.StartsWith('/'))
                {
                    errors.Add($"{slot} '{p}' must be a repository-relative path (no leading '/').");
                }

                if (p.Contains("..", StringComparison.Ordinal))
                {
                    errors.Add($"{slot} '{p}' must not contain '..' segments.");
                }
            }

            // Basename collision check across multiple paths. Honors any
            // explicit `as` alias, since aliases become the destination name.
            if (paths.Count > 1)
            {
                var basename = spec.ResolvedBasename.Trim();
                if (!string.IsNullOrEmpty(basename) && !seen.Add(basename))
                {
                    errors.Add($"{prefix}.paths: multiple entries share the destination name '{basename}', which would collide in the target directory.");
                }
            }
        }
    }

    private static bool IsValidEntryName(string name)
    {
        foreach (var c in name)
        {
            var ok = char.IsLetterOrDigit(c) || c is '-' or '_' or '.';
            if (!ok)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    ///     Validates a list of glob patterns used by <c>include</c> /
    ///     <c>exclude</c>: every entry must be a non-empty, non-whitespace
    ///     string. Empty / null lists are allowed and treated as "no
    ///     constraint" by the mirror.
    /// </summary>
    private static void ValidateFilterPatterns(IReadOnlyList<string>? patterns, string prefix, List<string> errors)
    {
        if (patterns is null || patterns.Count == 0)
        {
            return;
        }

        for (var i = 0; i < patterns.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(patterns[i]))
            {
                errors.Add($"{prefix}[{i}] must be a non-empty glob pattern.");
            }
        }
    }

    /// <summary>
    ///     Strategy-aware cross-entry collision check. Calls each entry's
    ///     strategy.<see cref="IPlanStrategy.EnumerateStaticDestinations"/>
    ///     with a synthetic <see cref="StaticPlanContext"/> using a path
    ///     resolver that simply passes raw input through. The comparison is
    ///     therefore intentionally lexical (matches the pre-strategy
    ///     behaviour); the real path-resolved comparison happens in
    ///     <see cref="ResolvedDestinationValidator"/>.
    /// </summary>
    private static void ValidateCrossEntryDestinations(
        ConduitManifest manifest,
        IPlanStrategyRegistry registry,
        List<string> errors)
    {
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        var snapshot = Strategies.StrategyConfigSnapshotBuilder.Build(manifest);
        var passthroughResolver = new PassthroughPathResolver();

        for (var i = 0; i < manifest.Entries.Count; i++)
        {
            var entry = manifest.Entries[i];
            if (string.IsNullOrWhiteSpace(entry.Name) || entry.Targets is null || entry.Targets.Count == 0)
            {
                continue;
            }

            IPlanStrategy strategy;
            try
            {
                strategy = registry.Resolve(entry.Strategy);
            }
            catch (UnknownStrategyException)
            {
                // Already reported by per-entry validation.
                continue;
            }

            var ctx = new StaticPlanContext(entry, manifestDirectory: ".", passthroughResolver, snapshot);
            IReadOnlyList<string> staticDests;
            try
            {
                staticDests = strategy.EnumerateStaticDestinations(ctx);
            }
            catch
            {
                // Strategies that can't enumerate statically (e.g. throw
                // because they need fetched content) are skipped here. Their
                // collisions surface at sync time instead.
                continue;
            }

            foreach (var dest in staticDests)
            {
                var key = NormalizeTargetPath(dest);
                if (seen.TryGetValue(key, out var owner))
                {
                    if (!string.Equals(owner, entry.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        errors.Add(
                            $"entries[{i}].targets: destination '{dest}' is already produced by entry '{owner}'. " +
                            "Two entries cannot write into the same directory inside the same target; rename one (set 'name' or 'as').");
                    }
                }
                else
                {
                    seen[key] = entry.Name!;
                }
            }
        }
    }

    /// <summary>
    ///     The pre-strategy collision check, kept for callers that don't
    ///     supply a <see cref="IPlanStrategyRegistry"/>. Mirrors the
    ///     original wrap-only enumeration verbatim.
    /// </summary>
    private static void ValidateLegacyCrossEntryDestinations(ConduitManifest manifest, List<string> errors)
    {
        var seen = new Dictionary<DestinationKey, string>();

        for (var i = 0; i < manifest.Entries.Count; i++)
        {
            var entry = manifest.Entries[i];
            if (string.IsNullOrWhiteSpace(entry.Name) || entry.Targets is null || entry.Targets.Count == 0)
            {
                continue;
            }

            foreach (var destination in EnumerateLegacyDestinations(entry))
            {
                if (seen.TryGetValue(destination, out var owner))
                {
                    errors.Add(
                        $"entries[{i}].targets: destination '{destination.TargetPath}/{destination.DestName}' " +
                        $"is already produced by entry '{owner}'. Two entries cannot write into the same " +
                        "directory inside the same target; rename one (set 'name' or 'as').");
                }
                else
                {
                    seen[destination] = entry.Name!;
                }
            }
        }
    }

    private static IEnumerable<DestinationKey> EnumerateLegacyDestinations(ConduitEntry entry)
    {
        var paths = entry.Source switch
        {
            GitHubSource gh => gh.EffectivePaths,
            LocalDirectorySource local => local.EffectivePaths,
            AzdoSource azdo => azdo.EffectivePaths,
            _ => Array.Empty<PathSpec>(),
        };

        var isMultiUnit = paths.Count > 1;

        foreach (var target in entry.Targets)
        {
            if (target is null || string.IsNullOrWhiteSpace(target.Path))
            {
                continue;
            }

            var normalizedTarget = NormalizeTargetPath(target.Path);

            if (isMultiUnit)
            {
                foreach (var spec in paths)
                {
                    var destName = spec.ResolvedBasename.Trim();
                    if (!string.IsNullOrEmpty(destName))
                    {
                        yield return new DestinationKey(normalizedTarget, destName);
                    }
                }
            }
            else
            {
                var destName = string.IsNullOrWhiteSpace(target.As) ? entry.Name : target.As;
                if (!string.IsNullOrWhiteSpace(destName))
                {
                    yield return new DestinationKey(normalizedTarget, destName!);
                }
            }
        }
    }

    private static string NormalizeTargetPath(string path)
    {
        var normalized = path.Trim().Replace('\\', '/');
        if (normalized.Length > 1 && normalized.EndsWith('/'))
        {
            normalized = normalized.TrimEnd('/');
        }

        return normalized;
    }

    private readonly record struct DestinationKey(string TargetPath, string DestName);

    /// <summary>
    ///     A no-op <see cref="Paths.IPathResolver"/> used during static
    ///     validation: it returns the input unchanged. Strategies that
    ///     enumerate static destinations get the raw target strings back,
    ///     which preserves the legacy lexical collision behaviour.
    /// </summary>
    private sealed class PassthroughPathResolver : Paths.IPathResolver
    {
        public string Resolve(string value, string basePath) => value;
    }
}
