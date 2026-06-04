namespace Zakira.Conduit.Strategies.Skills;

/// <summary>
///     A single harness in the registry: a canonical name plus one or more
///     search paths (relative to the user's chosen target) at which the
///     harness' <c>skills/</c> directory may live.
/// </summary>
public sealed record HarnessEntry(string Name, IReadOnlyList<string> SearchPaths);

/// <summary>
///     Helpers for normalising and merging harness-registry shapes from
///     <see cref="BuiltInHarnesses"/> and the manifest's
///     <c>strategies.skills.harnessRegistry</c> section.
/// </summary>
public static class HarnessRegistry
{
    /// <summary>
    ///     Removes the leading <c>.</c> (if any), trims whitespace, and
    ///     lowercases. Used so that both <c>".opencode"</c> and
    ///     <c>"opencode"</c> map to the same registry slot.
    /// </summary>
    public static string NormaliseKey(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var trimmed = key.Trim();
        return trimmed.StartsWith('.') ? trimmed[1..].ToLowerInvariant() : trimmed.ToLowerInvariant();
    }

    /// <summary>
    ///     Builds the effective harness map by merging the
    ///     <see cref="BuiltInHarnesses.Default"/> entries with
    ///     <paramref name="overrides"/>. A null override value <i>removes</i>
    ///     the built-in of the same (normalised) name; a non-null value
    ///     <i>replaces</i> the built-in.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Merge(
        IReadOnlyDictionary<string, IReadOnlyList<string>?>? overrides)
    {
        var merged = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var (name, paths) in BuiltInHarnesses.Default)
        {
            merged[NormaliseKey(name)] = paths;
        }

        if (overrides is not null)
        {
            foreach (var (key, paths) in overrides)
            {
                var normalised = NormaliseKey(key);

                if (paths is null)
                {
                    merged.Remove(normalised);
                    continue;
                }

                merged[normalised] = paths;
            }
        }

        return merged;
    }
}
