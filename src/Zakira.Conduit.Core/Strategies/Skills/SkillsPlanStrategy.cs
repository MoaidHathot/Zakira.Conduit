using Zakira.Conduit.Manifest;

namespace Zakira.Conduit.Strategies.Skills;

/// <summary>
///     Plan strategy that mirrors discovered <c>SKILL.md</c>-bearing folders
///     from each fetched content unit into every harness layout found under
///     the user's target.
/// </summary>
/// <remarks>
///     <para>
///         <b>Source side</b>: <see cref="SkillWalker"/> finds every
///         <c>SKILL.md</c> in the fetched content. When
///         <see cref="ConduitEntry.Skills"/> is set, the walk is restricted
///         to skills whose folder basename matches one of the listed names.
///     </para>
///     <para>
///         <b>Target side</b>: <see cref="HarnessTargetResolver"/> turns
///         each <see cref="ConduitEntry.Targets"/> path into one or more
///         resolved write locations &mdash; one per matched harness, or
///         a single literal location when the target is already a skills
///         directory or harness discovery is suppressed.
///     </para>
///     <para>
///         <b>GroupBy</b>: <see cref="GroupBy.Source"/> wraps every emitted
///         destination in a sub-directory named after the entry's source.
///     </para>
///     <para>
///         <b>Collisions</b>: when two sources contribute a skill of the
///         same name to the same write location, the entry's
///         <see cref="ConduitEntry.OnCollision"/> (or the manifest-global
///         <c>strategies.skills.onCollision</c>) decides whether to error,
///         skip, or last-wins.
///     </para>
/// </remarks>
public sealed class SkillsPlanStrategy : IPlanStrategy
{
    private readonly HarnessTargetResolver _harnessResolver;

    public SkillsPlanStrategy(HarnessTargetResolver harnessResolver)
    {
        ArgumentNullException.ThrowIfNull(harnessResolver);
        _harnessResolver = harnessResolver;
    }

    /// <inheritdoc />
    public string Name => StrategyNames.Skills;

    /// <inheritdoc />
    public IReadOnlyList<string> ValidateEntry(ConduitEntry entry, int entryIndex)
    {
        var errors = new List<string>();

        if (entry.Skills is { Count: > 0 })
        {
            for (var i = 0; i < entry.Skills.Count; i++)
            {
                var name = entry.Skills[i];
                if (string.IsNullOrWhiteSpace(name))
                {
                    errors.Add($"entries[{entryIndex}].skills[{i}] must be a non-empty skill name.");
                }
            }
        }

        if (entry.Harness is { Filter: { } filter })
        {
            for (var i = 0; i < filter.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(filter[i]))
                {
                    errors.Add($"entries[{entryIndex}].harness[{i}] must be a non-empty harness name.");
                }
            }
        }

        return errors;
    }

    /// <inheritdoc />
    public IReadOnlyList<PlannedDestination> Plan(PlanContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var entry = context.Entry;
        var registry = context.StrategiesConfig.SkillsHarnessRegistry;

        var skillsTargets = _harnessResolver.Resolve(entry, context.ManifestDirectory, registry);
        if (skillsTargets.Count == 0)
        {
            // No targets resolved (e.g. empty Targets list). Validator
            // catches that, but stay defensive.
            return Array.Empty<PlannedDestination>();
        }

        // Discover skills across every fetched content unit.
        var discovered = new List<SkillDescriptor>();
        foreach (var unit in context.Fetched.Contents)
        {
            var contentFull = Path.GetFullPath(unit.ContentDirectory);
            foreach (var descriptor in SkillWalker.Walk(contentFull, entry.Skills, contentFull))
            {
                discovered.Add(descriptor);
            }
        }

        if (discovered.Count == 0)
        {
            throw new StrategyPlanException(
                $"Entry '{entry.ResolvedName}' uses strategy 'skills' but no SKILL.md files were found in the fetched content. " +
                "Check the source path or the 'skills' selector.");
        }

        // Fan out: every discovered skill × every resolved write target.
        var destinations = new List<PlannedDestination>(discovered.Count * skillsTargets.Count);

        foreach (var skill in discovered)
        {
            foreach (var skillsTarget in skillsTargets)
            {
                var writeRoot = StrategyHelpers.ApplyGroupBy(skillsTarget.ResolvedDirectory, entry);
                var dest = Path.Combine(writeRoot, skill.Name);

                destinations.Add(new PlannedDestination(
                    SourceDirectory: skill.SkillDirectory,
                    TargetDirectory: dest,
                    Filter: context.Filter,
                    OriginEntry: entry,
                    OriginContentDirectory: skill.OriginContentDirectory));
            }
        }

        var policy = StrategyHelpers.EffectiveOnCollision(
            entry,
            manifestGlobal: context.StrategiesConfig.SkillsOnCollision,
            fallback: OnCollisionPolicy.Error);

        return StrategyHelpers.ResolveCollisions(destinations, policy, Name);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> EnumerateStaticDestinations(StaticPlanContext context)
    {
        // Skills is content-dependent: we don't know which SKILL.md files
        // exist (or which harnesses are installed under the target) without
        // fetching and probing.
        return Array.Empty<string>();
    }
}
