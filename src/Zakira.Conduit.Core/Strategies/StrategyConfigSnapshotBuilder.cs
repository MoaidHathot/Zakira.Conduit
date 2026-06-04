using Zakira.Conduit.Manifest;
using Zakira.Conduit.Strategies.Skills;

namespace Zakira.Conduit.Strategies;

/// <summary>
///     Builds <see cref="StrategyConfigSnapshot"/> instances from a
///     manifest. Encapsulates the "merge built-ins + manifest overrides"
///     logic for the skills harness registry so both the synchronizer and
///     the validator can produce identical snapshots.
/// </summary>
public static class StrategyConfigSnapshotBuilder
{
    /// <summary>
    ///     Builds a snapshot from <paramref name="manifest"/>'s optional
    ///     <c>strategies</c> section. When the section is absent or empty,
    ///     the returned snapshot exposes the built-in harness registry and
    ///     no manifest-level collision default.
    /// </summary>
    public static StrategyConfigSnapshot Build(ConduitManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var skillsConfig = manifest.Strategies?.Skills;
        var merged = HarnessRegistry.Merge(skillsConfig?.HarnessRegistry);

        return new StrategyConfigSnapshot(
            skillsHarnessRegistry: merged,
            skillsOnCollision: skillsConfig?.OnCollision);
    }
}
