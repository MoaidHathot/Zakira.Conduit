using Zakira.Conduit.Manifest;

namespace Zakira.Conduit.Strategies;

/// <summary>
///     Walks every fetched content unit's top-level child directories and
///     mirrors each into a same-named sub-directory at the target. Files at
///     the unit root are silently skipped (they have no natural per-child
///     destination).
/// </summary>
/// <remarks>
///     <para>
///         Useful when the source repo contains a folder of independent
///         child items (e.g. "skills/" laid out as several sub-folders) and
///         you want each child to land directly under the target without an
///         intermediate wrap dir.
///     </para>
///     <para>
///         Per-target <c>as</c> aliases are <i>ignored</i> for the same
///         reason as flat: there's no single sub-directory to alias.
///     </para>
///     <para>
///         <see cref="GroupBy.Source"/> inserts a source-named layer before
///         the expanded children: <c>&lt;target&gt;/&lt;source&gt;/&lt;child&gt;/...</c>.
///     </para>
///     <para>
///         Expand is content-dependent: <see cref="EnumerateStaticDestinations"/>
///         returns an empty list, so cross-entry static collision checks
///         skip this strategy and rely on state-recorded destinations.
///     </para>
/// </remarks>
public sealed class ExpandPlanStrategy : IPlanStrategy
{
    /// <inheritdoc />
    public string Name => StrategyNames.Expand;

    /// <inheritdoc />
    public IReadOnlyList<string> ValidateEntry(ConduitEntry entry, int entryIndex)
    {
        if (entry.Targets is { Count: > 0 } &&
            entry.Targets.Any(t => t is not null && !string.IsNullOrWhiteSpace(t.As)))
        {
            return new[]
            {
                $"entries[{entryIndex}].targets: per-target 'as' aliases are not supported by strategy 'expand'; remove the alias or switch to 'wrap'.",
            };
        }

        return Array.Empty<string>();
    }

    /// <inheritdoc />
    public IReadOnlyList<PlannedDestination> Plan(PlanContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var entry = context.Entry;
        var destinations = new List<PlannedDestination>();

        foreach (var unit in context.Fetched.Contents)
        {
            var contentFull = Path.GetFullPath(unit.ContentDirectory);

            if (!Directory.Exists(contentFull))
            {
                continue;
            }

            foreach (var childDir in Directory.EnumerateDirectories(contentFull))
            {
                var childName = Path.GetFileName(childDir);
                if (string.IsNullOrWhiteSpace(childName))
                {
                    continue;
                }

                foreach (var targetSpec in entry.Targets)
                {
                    var resolved = context.PathResolver.Resolve(targetSpec.Path, context.ManifestDirectory);
                    resolved = StrategyHelpers.ApplyGroupBy(resolved, entry);
                    var dest = Path.Combine(resolved, childName);

                    destinations.Add(new PlannedDestination(
                        SourceDirectory: childDir,
                        TargetDirectory: dest,
                        Filter: context.Filter,
                        OriginEntry: entry,
                        OriginContentDirectory: contentFull));
                }
            }
        }

        var policy = StrategyHelpers.EffectiveOnCollision(entry, manifestGlobal: null, fallback: OnCollisionPolicy.Error);
        return StrategyHelpers.ResolveCollisions(destinations, policy, Name);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> EnumerateStaticDestinations(StaticPlanContext context)
    {
        // Content-dependent: we don't know which child dirs exist without
        // fetching. Validator and orphan cleaner fall back to state-recorded
        // destinations.
        return Array.Empty<string>();
    }
}
