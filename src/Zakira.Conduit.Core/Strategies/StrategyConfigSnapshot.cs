using Zakira.Conduit.Manifest;

namespace Zakira.Conduit.Strategies;

/// <summary>
///     Immutable snapshot of the manifest-global <c>strategies</c> section,
///     resolved (built-ins + custom additions, minus null removals) and ready
///     for strategy code to consume. Carried on <see cref="PlanContext"/> and
///     <see cref="StaticPlanContext"/> so strategies have no ambient state.
/// </summary>
/// <remarks>
///     Phase 1 ships an empty default; Phase 3 fills in
///     <see cref="SkillsHarnessRegistry"/> with the merged harness map.
/// </remarks>
public sealed class StrategyConfigSnapshot
{
    /// <summary>The empty snapshot. Use when no manifest-global config exists.</summary>
    public static StrategyConfigSnapshot Empty { get; } = new(
        skillsHarnessRegistry: null,
        skillsOnCollision: null);

    public StrategyConfigSnapshot(
        IReadOnlyDictionary<string, IReadOnlyList<string>>? skillsHarnessRegistry,
        OnCollisionPolicy? skillsOnCollision)
    {
        SkillsHarnessRegistry = skillsHarnessRegistry
            ?? new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        SkillsOnCollision = skillsOnCollision;
    }

    /// <summary>
    ///     Resolved harness map for the skills strategy. Keys are harness
    ///     names (with or without the leading dot the user wrote &mdash; see
    ///     <c>HarnessRegistry.NormaliseKey</c>); values are one or more
    ///     directory paths (relative to the target) where the harness'
    ///     <c>skills/</c> folder may live.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> SkillsHarnessRegistry { get; }

    /// <summary>
    ///     Manifest-global default collision policy for the skills strategy.
    ///     Per-entry <see cref="ConduitEntry.OnCollision"/> overrides this.
    ///     <see langword="null"/> means "no manifest-level default; fall back
    ///     to the strategy's hard-coded default".
    /// </summary>
    public OnCollisionPolicy? SkillsOnCollision { get; }
}
