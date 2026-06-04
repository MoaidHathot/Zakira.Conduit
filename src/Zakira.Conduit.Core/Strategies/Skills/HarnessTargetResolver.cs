using Zakira.Conduit.Manifest;
using Zakira.Conduit.Paths;

namespace Zakira.Conduit.Strategies.Skills;

/// <summary>
///     One resolved write location produced by
///     <see cref="HarnessTargetResolver"/>: the directory the skill should be
///     mirrored into. Carries provenance fields so messaging can explain
///     <i>why</i> the destination was chosen.
/// </summary>
public sealed record SkillsTarget(
    string ResolvedDirectory,
    SkillsTargetKind Kind,
    string? HarnessName,
    string? MatchedRelativePath,
    PathSpec OriginTarget);

/// <summary>How a <see cref="SkillsTarget"/> was produced.</summary>
public enum SkillsTargetKind
{
    /// <summary>The user's target was treated literally (no harness scan).</summary>
    LiteralTarget = 0,

    /// <summary>The user's target was scanned and a harness layout matched.</summary>
    HarnessMatch = 1,
}

/// <summary>
///     Decides, for each <see cref="ConduitEntry.Targets"/> path the user
///     wrote, where the skills strategy should actually write. Combines:
///     <list type="bullet">
///         <item><description>auto-detection of literal "already a skills dir" targets,</description></item>
///         <item><description>the harness scan against the registry,</description></item>
///         <item><description>per-entry <see cref="ConduitEntry.Harness"/> filters.</description></item>
///     </list>
/// </summary>
public sealed class HarnessTargetResolver
{
    private readonly IPathResolver _pathResolver;

    public HarnessTargetResolver(IPathResolver pathResolver)
    {
        ArgumentNullException.ThrowIfNull(pathResolver);
        _pathResolver = pathResolver;
    }

    /// <summary>
    ///     Resolves every <see cref="SkillsTarget"/> the entry's targets
    ///     produce. The harness scan honours the per-entry
    ///     <see cref="ConduitEntry.Harness"/> selector (when set) plus the
    ///     supplied registry.
    /// </summary>
    /// <exception cref="StrategyPlanException">
    ///     When the entry forces harness discovery
    ///     (<see cref="HarnessSelector.Enabled"/> = <c>true</c>) but no
    ///     registered harness matches any of the targets.
    /// </exception>
    public IReadOnlyList<SkillsTarget> Resolve(
        ConduitEntry entry,
        string manifestDirectory,
        IReadOnlyDictionary<string, IReadOnlyList<string>> registry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestDirectory);
        ArgumentNullException.ThrowIfNull(registry);

        var selector = entry.Harness ?? HarnessSelector.EnabledAll;
        var scanner = new HarnessScanner(registry);
        var results = new List<SkillsTarget>();

        foreach (var targetSpec in entry.Targets)
        {
            if (targetSpec is null || string.IsNullOrWhiteSpace(targetSpec.Path))
            {
                continue;
            }

            var resolved = _pathResolver.Resolve(targetSpec.Path, manifestDirectory);

            if (!selector.Enabled || LooksLikeLiteralSkillsTarget(resolved, registry))
            {
                results.Add(new SkillsTarget(
                    ResolvedDirectory: Path.GetFullPath(resolved),
                    Kind: SkillsTargetKind.LiteralTarget,
                    HarnessName: null,
                    MatchedRelativePath: null,
                    OriginTarget: targetSpec));
                continue;
            }

            var matches = scanner.Scan(resolved, selector.Filter);

            if (matches.Count == 0)
            {
                // No harness matched. Defer error-vs-degrade to the caller:
                // strict mode (harness:true with explicit filter or
                // selector.Enabled) becomes a hard error; in lenient mode
                // we fall back to treating the target as literal.
                if (selector.Filter is { Count: > 0 })
                {
                    throw new StrategyPlanException(
                        $"Entry '{entry.ResolvedName}' requires harness(es) [{string.Join(", ", selector.Filter)}] " +
                        $"under target '{resolved}' but none were found. Either add those harnesses' skills directories, " +
                        "drop the 'harness' filter, or remove the entry.");
                }

                // No explicit filter, no matches: fall back to literal.
                results.Add(new SkillsTarget(
                    ResolvedDirectory: Path.GetFullPath(resolved),
                    Kind: SkillsTargetKind.LiteralTarget,
                    HarnessName: null,
                    MatchedRelativePath: null,
                    OriginTarget: targetSpec));
                continue;
            }

            foreach (var match in matches)
            {
                results.Add(new SkillsTarget(
                    ResolvedDirectory: match.ResolvedSkillsDirectory,
                    Kind: SkillsTargetKind.HarnessMatch,
                    HarnessName: match.HarnessName,
                    MatchedRelativePath: match.MatchedRelativePath,
                    OriginTarget: targetSpec));
            }
        }

        return results;
    }

    /// <summary>
    ///     Returns <see langword="true"/> when <paramref name="resolved"/>
    ///     looks like it's already inside (or is) an agent harness' skills
    ///     dir, so a harness scan would be redundant. Matches:
    ///     <list type="bullet">
    ///         <item><description>any registered search-path suffix (case-insensitive),</description></item>
    ///         <item><description>any directory whose trailing segment is <c>skills</c>.</description></item>
    ///     </list>
    /// </summary>
    private static bool LooksLikeLiteralSkillsTarget(
        string resolved,
        IReadOnlyDictionary<string, IReadOnlyList<string>> registry)
    {
        var normalised = resolved.Replace('\\', '/').TrimEnd('/');
        var last = normalised.LastIndexOf('/');
        var trailing = last < 0 ? normalised : normalised[(last + 1)..];

        if (string.Equals(trailing, "skills", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (var paths in registry.Values)
        {
            foreach (var rel in paths)
            {
                var relNormalised = rel.Replace('\\', '/').TrimEnd('/');
                if (normalised.EndsWith('/' + relNormalised, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(normalised, relNormalised, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
