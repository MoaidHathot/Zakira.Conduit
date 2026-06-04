using Microsoft.Extensions.Logging.Abstractions;
using Zakira.Conduit.Manifest;
using Zakira.Conduit.Paths;
using Zakira.Conduit.Strategies;
using Zakira.Conduit.Strategies.Skills;

namespace Zakira.Conduit.Core.UnitTests.TestHelpers;

/// <summary>
///     Shared factory for the strategy registry tests need. Builds the
///     full default set (wrap, flat, expand, skills) wired through a
///     real <see cref="HarnessTargetResolver"/> so per-strategy plan tests
///     can opt into harness-aware behaviour without scaffolding boilerplate.
/// </summary>
internal static class StrategyTestHelper
{
    public static IPlanStrategyRegistry BuildDefaultRegistry(IPathResolver? pathResolver = null)
    {
        var resolver = pathResolver ?? new DefaultPathResolver(new FakeEnvironment());
        var harnessResolver = new HarnessTargetResolver(resolver);

        IPlanStrategy[] strategies =
        [
            new WrapPlanStrategy(),
            new FlatPlanStrategy(),
            new ExpandPlanStrategy(),
            new SkillsPlanStrategy(harnessResolver),
        ];

        return new PlanStrategyRegistry(strategies);
    }
}
