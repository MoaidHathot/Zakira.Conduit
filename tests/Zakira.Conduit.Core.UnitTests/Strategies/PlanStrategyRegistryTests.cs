using Zakira.Conduit.Core.UnitTests.TestHelpers;
using Zakira.Conduit.Strategies;

namespace Zakira.Conduit.Core.UnitTests.Strategies;

public sealed class PlanStrategyRegistryTests
{
    private static readonly string[] ExpectedStrategyNames = ["expand", "flat", "skills", "wrap"];

    [Fact]
    public void Default_registry_includes_wrap_flat_expand_skills()
    {
        var registry = StrategyTestHelper.BuildDefaultRegistry();
        registry.Names.Should().BeEquivalentTo(ExpectedStrategyNames);
    }

    [Fact]
    public void Resolve_with_null_returns_wrap_strategy()
    {
        var registry = StrategyTestHelper.BuildDefaultRegistry();
        registry.Resolve(null).Name.Should().Be(StrategyNames.Wrap);
    }

    [Fact]
    public void Resolve_with_empty_returns_wrap_strategy()
    {
        var registry = StrategyTestHelper.BuildDefaultRegistry();
        registry.Resolve("   ").Name.Should().Be(StrategyNames.Wrap);
    }

    [Fact]
    public void Resolve_is_case_insensitive()
    {
        var registry = StrategyTestHelper.BuildDefaultRegistry();
        registry.Resolve("WRAP").Name.Should().Be(StrategyNames.Wrap);
        registry.Resolve("Flat").Name.Should().Be(StrategyNames.Flat);
        registry.Resolve("expand").Name.Should().Be(StrategyNames.Expand);
        registry.Resolve("Skills").Name.Should().Be(StrategyNames.Skills);
    }

    [Fact]
    public void Resolve_throws_for_unknown_strategy_with_available_list()
    {
        var registry = StrategyTestHelper.BuildDefaultRegistry();
        var act = () => registry.Resolve("bogus");
        act.Should()
            .Throw<UnknownStrategyException>()
            .Where(e => e.Requested == "bogus" && e.Available.Contains("wrap"));
    }

    [Fact]
    public void IsRegistered_returns_true_for_known_strategies()
    {
        var registry = StrategyTestHelper.BuildDefaultRegistry();
        registry.IsRegistered("wrap").Should().BeTrue();
        registry.IsRegistered("FLAT").Should().BeTrue();
        registry.IsRegistered("nope").Should().BeFalse();
        registry.IsRegistered("").Should().BeFalse();
        registry.IsRegistered("   ").Should().BeFalse();
    }

    [Fact]
    public void Duplicate_strategy_names_throw_at_construction()
    {
        var dupes = new IPlanStrategy[]
        {
            new WrapPlanStrategy(),
            new WrapPlanStrategy(),
        };

        var act = () => new PlanStrategyRegistry(dupes);

        act.Should().Throw<InvalidOperationException>().WithMessage("*'wrap'*");
    }
}
