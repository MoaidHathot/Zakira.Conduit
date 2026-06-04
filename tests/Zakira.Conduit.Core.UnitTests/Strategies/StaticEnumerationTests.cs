using Zakira.Conduit.Core.UnitTests.TestHelpers;
using Zakira.Conduit.Manifest;
using Zakira.Conduit.Paths;
using Zakira.Conduit.Strategies;

namespace Zakira.Conduit.Core.UnitTests.Strategies;

public sealed class WrapPlanStrategyStaticEnumerationTests
{
    private static StaticPlanContext BuildCtx(ConduitEntry entry, string manifestDir) =>
        new(entry, manifestDir, new DefaultPathResolver(new FakeEnvironment()), StrategyConfigSnapshot.Empty);

    [Fact]
    public void Single_unit_single_target_uses_entry_name()
    {
        using var tmp = new TempDir();
        var entry = new ConduitEntry
        {
            Name = "myskill",
            Source = new GitHubSource { Repo = "o/r" },
            Targets = [tmp.Combine("target")],
        };

        var strategy = new WrapPlanStrategy();
        var dests = strategy.EnumerateStaticDestinations(BuildCtx(entry, tmp.Path));
        dests.Should().ContainSingle().Which.Should().EndWith("myskill");
    }

    [Fact]
    public void Multi_path_uses_path_basenames()
    {
        using var tmp = new TempDir();
        var entry = new ConduitEntry
        {
            Name = "metadata-only",
            Source = new GitHubSource { Repo = "o/r", Paths = new[] { new PathSpec("foo/a"), new PathSpec("bar/b") } },
            Targets = [tmp.Combine("target")],
        };

        var strategy = new WrapPlanStrategy();
        var dests = strategy.EnumerateStaticDestinations(BuildCtx(entry, tmp.Path)).ToArray();

        dests.Should().HaveCount(2);
        dests.Should().Contain(d => d.EndsWith('a'));
        dests.Should().Contain(d => d.EndsWith('b'));
    }

    [Fact]
    public void GroupBy_source_wraps_in_source_name()
    {
        using var tmp = new TempDir();
        var entry = new ConduitEntry
        {
            Name = "custom",
            GroupBy = GroupBy.Source,
            Source = new GitHubSource { Repo = "org/cool-repo" },
            Targets = [tmp.Combine("target")],
        };

        var strategy = new WrapPlanStrategy();
        var dest = strategy.EnumerateStaticDestinations(BuildCtx(entry, tmp.Path)).Single();
        dest.Should().Contain("cool-repo").And.EndWith("custom");
    }

    [Fact]
    public void Per_target_alias_overrides_entry_name_for_single_unit()
    {
        using var tmp = new TempDir();
        var entry = new ConduitEntry
        {
            Name = "entry-name",
            Source = new GitHubSource { Repo = "o/r" },
            Targets = [new PathSpec(tmp.Combine("target"), "renamed")],
        };

        var strategy = new WrapPlanStrategy();
        var dest = strategy.EnumerateStaticDestinations(BuildCtx(entry, tmp.Path)).Single();
        dest.Should().EndWith("renamed");
    }
}

public sealed class FlatPlanStrategyStaticEnumerationTests
{
    [Fact]
    public void Returns_one_resolved_target_per_target_path()
    {
        using var tmp = new TempDir();
        var entry = new ConduitEntry
        {
            Name = "flat",
            Strategy = "flat",
            Source = new GitHubSource { Repo = "o/r" },
            Targets = [tmp.Combine("a"), tmp.Combine("b")],
        };

        var strategy = new FlatPlanStrategy();
        var ctx = new StaticPlanContext(entry, tmp.Path, new DefaultPathResolver(new FakeEnvironment()), StrategyConfigSnapshot.Empty);
        var dests = strategy.EnumerateStaticDestinations(ctx).ToArray();
        dests.Should().HaveCount(2);
    }

    [Fact]
    public void GroupBy_source_adds_source_name_layer()
    {
        using var tmp = new TempDir();
        var entry = new ConduitEntry
        {
            Name = "ignored",
            Strategy = "flat",
            GroupBy = GroupBy.Source,
            Source = new GitHubSource { Repo = "org/important" },
            Targets = [tmp.Combine("base")],
        };

        var strategy = new FlatPlanStrategy();
        var ctx = new StaticPlanContext(entry, tmp.Path, new DefaultPathResolver(new FakeEnvironment()), StrategyConfigSnapshot.Empty);
        var dest = strategy.EnumerateStaticDestinations(ctx).Single();
        dest.Should().EndWith("important");
    }
}

public sealed class ExpandPlanStrategyStaticEnumerationTests
{
    [Fact]
    public void Expand_returns_empty_static_list_so_collision_checks_skip_it()
    {
        using var tmp = new TempDir();
        var entry = new ConduitEntry
        {
            Name = "expand",
            Strategy = "expand",
            Source = new GitHubSource { Repo = "o/r" },
            Targets = [tmp.Combine("dest")],
        };

        var strategy = new ExpandPlanStrategy();
        var ctx = new StaticPlanContext(entry, tmp.Path, new DefaultPathResolver(new FakeEnvironment()), StrategyConfigSnapshot.Empty);
        strategy.EnumerateStaticDestinations(ctx).Should().BeEmpty();
    }
}
