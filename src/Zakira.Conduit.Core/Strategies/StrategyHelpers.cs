using Zakira.Conduit.Manifest;
using Zakira.Conduit.Sources.Inference;

namespace Zakira.Conduit.Strategies;

/// <summary>
///     Shared helpers used across strategies. Currently:
///     <list type="bullet">
///         <item><description>
///             <see cref="ApplyGroupBy"/> &mdash; wraps a resolved target
///             directory in an additional source-named layer when
///             <see cref="ConduitEntry.GroupBy"/> is <see cref="GroupBy.Source"/>.
///         </description></item>
///         <item><description>
///             <see cref="EffectiveOnCollision"/> &mdash; entry override
///             beats manifest-global beats hard-coded default.
///         </description></item>
///         <item><description>
///             <see cref="ResolveCollisions"/> &mdash; applies a collision
///             policy to a list of <see cref="PlannedDestination"/>s.
///         </description></item>
///     </list>
/// </summary>
internal static class StrategyHelpers
{
    /// <summary>
    ///     Returns <paramref name="target"/> wrapped in a source-name
    ///     sub-directory when <paramref name="entry"/> opts into
    ///     <see cref="GroupBy.Source"/>. Returns the unchanged path when no
    ///     grouping applies, or when no source name can be derived (in which
    ///     case the strategy proceeds as if grouping were off).
    /// </summary>
    public static string ApplyGroupBy(string target, ConduitEntry entry)
    {
        if (entry.GroupBy != GroupBy.Source)
        {
            return target;
        }

        var sourceName = DefaultSourceNameDeriver.Derive(entry.Source);
        if (string.IsNullOrWhiteSpace(sourceName))
        {
            // No identity to wrap with; degrade gracefully. The validator
            // surfaces this case ahead of time so users see the issue at
            // load time, not at plan time.
            return target;
        }

        return Path.Combine(target, sourceName);
    }

    /// <summary>
    ///     Resolves the effective collision policy for an entry: per-entry
    ///     override beats manifest-global beats <paramref name="fallback"/>.
    /// </summary>
    public static OnCollisionPolicy EffectiveOnCollision(
        ConduitEntry entry,
        OnCollisionPolicy? manifestGlobal,
        OnCollisionPolicy fallback)
    {
        if (entry.OnCollision is { } perEntry)
        {
            return perEntry;
        }

        if (manifestGlobal is { } global)
        {
            return global;
        }

        return fallback;
    }

    /// <summary>
    ///     Applies a collision policy to <paramref name="destinations"/>.
    ///     Duplicates are detected by <see cref="PlannedDestination.TargetDirectory"/>
    ///     using the OS-appropriate path comparer.
    /// </summary>
    /// <exception cref="StrategyPlanException">
    ///     When <paramref name="policy"/> is <see cref="OnCollisionPolicy.Error"/>
    ///     and two destinations write to the same target.
    /// </exception>
    public static IReadOnlyList<PlannedDestination> ResolveCollisions(
        IReadOnlyList<PlannedDestination> destinations,
        OnCollisionPolicy policy,
        string strategyName)
    {
        if (destinations.Count <= 1)
        {
            return destinations;
        }

        var comparer = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

        var byTarget = new Dictionary<string, PlannedDestination>(comparer);

        foreach (var dest in destinations)
        {
            var key = NormalisePath(dest.TargetDirectory);
            if (!byTarget.TryGetValue(key, out var existing))
            {
                byTarget[key] = dest;
                continue;
            }

            switch (policy)
            {
                case OnCollisionPolicy.Error:
                    throw new StrategyPlanException(
                        $"Strategy '{strategyName}' produced two destinations that write to '{dest.TargetDirectory}'. " +
                        $"Sources: '{existing.SourceDirectory}' and '{dest.SourceDirectory}'. " +
                        $"Set onCollision to 'skip' or 'last-wins' (entry-level or under strategies.skills) to allow the merge, or rename one of the sources.");

                case OnCollisionPolicy.Skip:
                    // Keep the first-seen entry.
                    break;

                case OnCollisionPolicy.LastWins:
                    byTarget[key] = dest;
                    break;
            }
        }

        return byTarget.Values.ToArray();
    }

    private static string NormalisePath(string path)
    {
        var trimmed = path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.IsNullOrEmpty(trimmed) ? trimmed : Path.GetFullPath(trimmed);
    }
}
