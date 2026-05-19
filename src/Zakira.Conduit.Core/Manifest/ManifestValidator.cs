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
                if (string.IsNullOrWhiteSpace(entry.Targets[t]))
                {
                    errors.Add($"{prefix}.targets[{t}] must be a non-empty string.");
                }
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

    private static void ValidateLocalSource(LocalDirectorySkillSource source, string prefix, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(source.Path))
        {
            errors.Add($"{prefix}.source.path must be a non-empty string for local sources.");
        }
    }

    private static void ValidateGitHubSource(GitHubSkillSource source, string prefix, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(source.Owner))
        {
            errors.Add($"{prefix}.source.owner must be a non-empty string for github sources.");
        }

        if (string.IsNullOrWhiteSpace(source.Repo))
        {
            errors.Add($"{prefix}.source.repo must be a non-empty string for github sources.");
        }

        if (!string.IsNullOrEmpty(source.Commit) && !string.IsNullOrEmpty(source.Branch))
        {
            errors.Add($"{prefix}.source: 'commit' and 'branch' are mutually exclusive.");
        }

        if (source.Path is not null && (source.Path.StartsWith('/') || source.Path.Contains("..", StringComparison.Ordinal)))
        {
            errors.Add($"{prefix}.source.path '{source.Path}' must be a repository-relative path without '..' segments.");
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
