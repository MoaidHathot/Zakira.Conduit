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
    public static IReadOnlyList<string> Validate(ConduitManifest? manifest)
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
            ValidateEntry(manifest.Entries[i], i, seenNames, errors);
        }

        // Cross-entry destination collision: two entries (or two
        // array-expanded sub-entries) must not produce the same destination
        // directory inside the same target. Today's per-entry checks only
        // catch within-entry collisions; this catches the case where two
        // independent entries pick the same source-derived name (e.g. both
        // resolve to a repo called "skills") into the same target.
        ValidateCrossEntryDestinations(manifest, errors);

        return errors;
    }

    private static void ValidateEntry(ConduitEntry entry, int index, HashSet<string> seenNames, List<string> errors)
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
            // exactly one content unit; otherwise basename-derived destinations
            // would silently override the alias.
            var aliasedTargets = entry.Targets.Count(t => t is not null && !string.IsNullOrWhiteSpace(t.As));
            var sourceProducesMultiple = entry.Source switch
            {
                GitHubSource gh => gh.EffectivePaths.Count > 1,
                LocalDirectorySource local => local.EffectivePaths.Count > 1,
                AzdoSource azdo => azdo.EffectivePaths.Count > 1,
                _ => false,
            };

            if (aliasedTargets > 0 && sourceProducesMultiple)
            {
                errors.Add($"{prefix}.targets: per-target 'as' aliases are not allowed on multi-path entries (the source produces multiple destinations).");
            }
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
    ///     Detects two entries that would write into the same destination
    ///     directory (<c>&lt;targetPath&gt;/&lt;destName&gt;/</c>). The check
    ///     is conservative: it compares target path <i>strings</i> as-written
    ///     (after light normalisation), without resolving <c>~</c>,
    ///     environment variables, or symlinks. So
    ///     <c>~/skills</c> vs <c>$HOME/skills</c> won't be flagged even when
    ///     they resolve to the same directory; that's acceptable because the
    ///     common collision case (two entries literally targeting the same
    ///     directory string with the same dest name) is what bites in practice.
    /// </summary>
    private static void ValidateCrossEntryDestinations(ConduitManifest manifest, List<string> errors)
    {
        // Map: (targetPath, destName) -> first-seen owning entry name.
        var seen = new Dictionary<DestinationKey, string>();

        for (var i = 0; i < manifest.Entries.Count; i++)
        {
            var entry = manifest.Entries[i];
            if (string.IsNullOrWhiteSpace(entry.Name) || entry.Targets is null || entry.Targets.Count == 0)
            {
                // Per-entry validation already errored; skip to avoid noise.
                continue;
            }

            foreach (var destination in EnumerateDestinations(entry))
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

    /// <summary>
    ///     Enumerates every <c>(targetPath, destName)</c> tuple the entry
    ///     would produce. Single-unit sources use <c>target.As ?? entry.Name</c>
    ///     per target; multi-unit sources use each unit's resolved basename.
    /// </summary>
    private static IEnumerable<DestinationKey> EnumerateDestinations(ConduitEntry entry)
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
        // Normalise separators and trim trailing slash so equivalent
        // string-forms collapse to one key. Don't resolve env vars / ~ here —
        // the collision check is best-effort and only catches literal matches.
        var normalized = path.Trim().Replace('\\', '/');
        if (normalized.Length > 1 && normalized.EndsWith('/'))
        {
            normalized = normalized.TrimEnd('/');
        }

        return normalized;
    }

    private readonly record struct DestinationKey(string TargetPath, string DestName);
}
