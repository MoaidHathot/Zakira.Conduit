namespace Zakira.Conduit.Strategies.Skills;

/// <summary>
///     One match found by <see cref="HarnessScanner"/>: which harness was
///     detected, the relative path that matched (so the user can see which
///     of multiple search paths fired), and the absolute resolved directory
///     the skill should be mirrored into.
/// </summary>
public sealed record HarnessMatch(
    string HarnessName,
    string MatchedRelativePath,
    string ResolvedSkillsDirectory);

/// <summary>
///     Scans a resolved target directory for known harness layouts. The scan
///     descends only one level: for each harness in the registry it probes
///     <c>&lt;target&gt;/&lt;path&gt;</c> for every search path declared by
///     that harness, and records each one that exists as a directory.
/// </summary>
public sealed class HarnessScanner
{
    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _registry;

    public HarnessScanner(IReadOnlyDictionary<string, IReadOnlyList<string>> registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _registry = registry;
    }

    /// <summary>
    ///     Scans <paramref name="targetDirectory"/> against every harness in
    ///     the registry, optionally restricted to
    ///     <paramref name="filter"/>. Returns one match per existing path
    ///     (a single harness with multiple matching paths yields multiple
    ///     entries).
    /// </summary>
    /// <param name="targetDirectory">Absolute path to the user's chosen target.</param>
    /// <param name="filter">
    ///     Optional case-insensitive set of harness names to restrict the
    ///     scan to. Names are matched against the registry's normalised keys.
    /// </param>
    public IReadOnlyList<HarnessMatch> Scan(string targetDirectory, IReadOnlyCollection<string>? filter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDirectory);

        var normalisedFilter = filter is { Count: > 0 }
            ? new HashSet<string>(filter.Select(HarnessRegistry.NormaliseKey), StringComparer.OrdinalIgnoreCase)
            : null;

        var fullTarget = Path.GetFullPath(targetDirectory);
        if (!Directory.Exists(fullTarget))
        {
            return Array.Empty<HarnessMatch>();
        }

        var matches = new List<HarnessMatch>();

        foreach (var (harness, paths) in _registry)
        {
            var key = HarnessRegistry.NormaliseKey(harness);
            if (normalisedFilter is not null && !normalisedFilter.Contains(key))
            {
                continue;
            }

            foreach (var rel in paths)
            {
                if (string.IsNullOrWhiteSpace(rel))
                {
                    continue;
                }

                var combined = Path.Combine(fullTarget, NormaliseSeparators(rel));
                if (Directory.Exists(combined))
                {
                    matches.Add(new HarnessMatch(
                        HarnessName: key,
                        MatchedRelativePath: rel,
                        ResolvedSkillsDirectory: Path.GetFullPath(combined)));
                }
            }
        }

        return matches;
    }

    private static string NormaliseSeparators(string path) =>
        path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
}
