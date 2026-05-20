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

        return errors;
    }

    private static void ValidateEntry(ConduitEntry entry, int index, HashSet<string> seenNames, List<string> errors)
    {
        var prefix = $"entries[{index}]";

        if (string.IsNullOrWhiteSpace(entry.Name))
        {
            errors.Add($"{prefix}.name must be a non-empty string.");
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
                GitHubSkillSource gh => gh.EffectivePaths.Count > 1,
                LocalDirectorySkillSource local => local.EffectivePaths.Count > 1,
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

            case GitHubSkillSource gh:
                ValidateGitHubSource(gh, prefix, errors);
                break;

            case LocalDirectorySkillSource local:
                ValidateLocalSource(local, prefix, errors);
                break;

            default:
                errors.Add($"{prefix}.source has unsupported kind '{entry.Source.Kind}'.");
                break;
        }
    }

    private static void ValidateGitHubSource(GitHubSkillSource source, string prefix, List<string> errors)
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

        ValidateSubPaths(source.EffectivePaths, $"{prefix}.source", requireRepoRelative: true, errors);
    }

    private static void ValidateLocalSource(LocalDirectorySkillSource source, string prefix, List<string> errors)
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
}
