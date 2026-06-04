using Zakira.Conduit.Manifest;

namespace Zakira.Conduit.Strategies;

/// <summary>
///     Mirrors each fetched content unit's contents <i>directly</i> into the
///     target directory, with no per-entry wrapping sub-directory. Multi-unit
///     entries merge their content into the same target; collisions are
///     resolved per <see cref="ConduitEntry.OnCollision"/>.
/// </summary>
/// <remarks>
///     <para>
///         Targets' per-target <c>as</c> aliases are <i>ignored</i> in flat
///         mode &mdash; there's no sub-directory to alias. Use <c>wrap</c>
///         if you want per-target rename behaviour.
///     </para>
///     <para>
///         <see cref="GroupBy.Source"/> re-introduces a single wrapping
///         layer, named after the source: each source's flat output lands in
///         <c>&lt;target&gt;/&lt;source-name&gt;/...</c>.
///     </para>
/// </remarks>
public sealed class FlatPlanStrategy : IPlanStrategy
{
    /// <inheritdoc />
    public string Name => StrategyNames.Flat;

    /// <inheritdoc />
    public IReadOnlyList<string> ValidateEntry(ConduitEntry entry, int entryIndex)
    {
        // Per-target aliases don't apply to flat: there's no sub-directory
        // to alias. Surface that as a hard error so users don't get silent
        // behaviour drift.
        if (entry.Targets is { Count: > 0 } &&
            entry.Targets.Any(t => t is not null && !string.IsNullOrWhiteSpace(t.As)))
        {
            return new[]
            {
                $"entries[{entryIndex}].targets: per-target 'as' aliases are not supported by strategy 'flat'; remove the alias or switch to 'wrap'.",
            };
        }

        return Array.Empty<string>();
    }

    /// <inheritdoc />
    public IReadOnlyList<PlannedDestination> Plan(PlanContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var entry = context.Entry;
        var destinations = new List<PlannedDestination>(context.Fetched.Contents.Count * entry.Targets.Count);

        foreach (var unit in context.Fetched.Contents)
        {
            var fetchedFull = Path.GetFullPath(unit.ContentDirectory);

            foreach (var targetSpec in entry.Targets)
            {
                var resolved = context.PathResolver.Resolve(targetSpec.Path, context.ManifestDirectory);
                resolved = StrategyHelpers.ApplyGroupBy(resolved, entry);

                destinations.Add(new PlannedDestination(
                    SourceDirectory: fetchedFull,
                    TargetDirectory: resolved,
                    Filter: context.Filter,
                    OriginEntry: entry,
                    OriginContentDirectory: fetchedFull));
            }
        }

        // For multi-unit entries (or multiple targets sharing the same
        // resolved path), the same target receives content from multiple
        // sources. Honour the collision policy so the user controls merge
        // behaviour.
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

        var dests = new List<string>(entry.Targets.Count);

        foreach (var target in entry.Targets)
        {
            if (target is null || string.IsNullOrWhiteSpace(target.Path))
            {
                continue;
            }

            string resolved;
            try
            {
                resolved = context.PathResolver.Resolve(target.Path, context.ManifestDirectory);
            }
            catch
            {
                continue;
            }

            dests.Add(StrategyHelpers.ApplyGroupBy(resolved, entry));
        }

        return dests;
    }
}
