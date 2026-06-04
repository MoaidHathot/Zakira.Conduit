using Zakira.Conduit.Manifest;

namespace Zakira.Conduit.Strategies;

/// <summary>
///     Default strategy. Reproduces the pre-strategy behaviour:
///     <list type="bullet">
///         <item><description>
///             Single-unit entries: each target receives a sub-directory
///             named after the entry (or the per-target alias).
///         </description></item>
///         <item><description>
///             Multi-unit entries: each unit lands in a sub-directory named
///             after its
///             <see cref="Sources.FetchedContent.SuggestedDestinationName"/>.
///             Per-target aliases are rejected by the validator in this case.
///         </description></item>
///         <item><description>
///             <see cref="GroupBy.Source"/> inserts an extra source-named
///             layer between each target and the wrap sub-directory.
///         </description></item>
///     </list>
/// </summary>
public sealed class WrapPlanStrategy : IPlanStrategy
{
    /// <inheritdoc />
    public string Name => StrategyNames.Wrap;

    /// <inheritdoc />
    public IReadOnlyList<string> ValidateEntry(ConduitEntry entry, int entryIndex)
    {
        // Wrap is the legacy default; nothing strategy-specific to validate
        // beyond what the global validator already enforces.
        return Array.Empty<string>();
    }

    /// <inheritdoc />
    public IReadOnlyList<PlannedDestination> Plan(PlanContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var entry = context.Entry;
        var fetched = context.Fetched;

        var singleUnit = fetched.Contents.Count == 1;
        var destinations = new List<PlannedDestination>(fetched.Contents.Count * entry.Targets.Count);

        foreach (var unit in fetched.Contents)
        {
            var fetchedFull = Path.GetFullPath(unit.ContentDirectory);

            foreach (var targetSpec in entry.Targets)
            {
                string destName;
                if (singleUnit)
                {
                    destName = targetSpec.As ?? entry.ResolvedName;
                }
                else
                {
                    destName = unit.SuggestedDestinationName
                        ?? throw new StrategyPlanException(
                            $"Source '{entry.Source.Kind}' returned multiple content units but failed to suggest a destination name for one of them.");
                }

                var resolvedParent = context.PathResolver.Resolve(targetSpec.Path, context.ManifestDirectory);
                resolvedParent = StrategyHelpers.ApplyGroupBy(resolvedParent, entry);
                var resolvedTarget = Path.Combine(resolvedParent, destName);

                destinations.Add(new PlannedDestination(
                    SourceDirectory: fetchedFull,
                    TargetDirectory: resolvedTarget,
                    Filter: context.Filter,
                    OriginEntry: entry,
                    OriginContentDirectory: fetchedFull));
            }
        }

        // Wrap doesn't normally produce collisions (each unit gets its own
        // dest name), but with groupBy:source a misconfigured entry could.
        // Honour the configured policy uniformly.
        var policy = StrategyHelpers.EffectiveOnCollision(entry, manifestGlobal: null, fallback: OnCollisionPolicy.Error);
        return StrategyHelpers.ResolveCollisions(destinations, policy, Name);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> EnumerateStaticDestinations(StaticPlanContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var entry = context.Entry;
        if (entry.Targets is null || entry.Targets.Count == 0)
        {
            return Array.Empty<string>();
        }

        // Mirror the synchronizer's single-vs-multi heuristic for predictable
        // (non-content-dependent) cases. Multi-unit basenames come from the
        // source's EffectivePaths.
        var multiUnitDestNames = entry.Source switch
        {
            GitHubSource gh when gh.EffectivePaths.Count > 1 => gh.EffectivePaths.Select(p => p.ResolvedBasename).ToArray(),
            AzdoSource azdo when azdo.EffectivePaths.Count > 1 => azdo.EffectivePaths.Select(p => p.ResolvedBasename).ToArray(),
            LocalDirectorySource local when local.EffectivePaths.Count > 1 => local.EffectivePaths.Select(p => p.ResolvedBasename).ToArray(),
            _ => null,
        };

        var dests = new List<string>();

        foreach (var target in entry.Targets)
        {
            if (target is null || string.IsNullOrWhiteSpace(target.Path))
            {
                continue;
            }

            string resolvedParent;
            try
            {
                resolvedParent = context.PathResolver.Resolve(target.Path, context.ManifestDirectory);
            }
            catch
            {
                continue;
            }

            resolvedParent = StrategyHelpers.ApplyGroupBy(resolvedParent, entry);

            if (multiUnitDestNames is not null)
            {
                foreach (var unitName in multiUnitDestNames)
                {
                    if (!string.IsNullOrWhiteSpace(unitName))
                    {
                        dests.Add(Path.Combine(resolvedParent, unitName));
                    }
                }
            }
            else
            {
                var destName = string.IsNullOrWhiteSpace(target.As) ? entry.Name : target.As;
                if (!string.IsNullOrWhiteSpace(destName))
                {
                    dests.Add(Path.Combine(resolvedParent, destName!));
                }
            }
        }

        return dests;
    }
}
