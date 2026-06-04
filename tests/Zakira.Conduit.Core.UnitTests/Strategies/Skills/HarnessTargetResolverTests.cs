using Zakira.Conduit.Core.UnitTests.TestHelpers;
using Zakira.Conduit.Manifest;
using Zakira.Conduit.Paths;
using Zakira.Conduit.Strategies;
using Zakira.Conduit.Strategies.Skills;

namespace Zakira.Conduit.Core.UnitTests.Strategies.Skills;

public sealed class HarnessTargetResolverTests
{
    private static readonly string[] ClaudeFilter = ["claude"];
    private static HarnessTargetResolver Build() =>
        new(new DefaultPathResolver(new FakeEnvironment()));

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> DefaultRegistry =>
        HarnessRegistry.Merge(null);

    [Fact]
    public void Returns_literal_target_when_trailing_segment_is_skills()
    {
        using var tmp = new TempDir();
        var resolver = Build();
        var entry = new ConduitEntry
        {
            Name = "x",
            Source = new GitHubSource { Repo = "o/r" },
            Targets = [tmp.Combine("custom", "skills")],
        };

        var resolved = resolver.Resolve(entry, tmp.Path, DefaultRegistry);
        resolved.Should().ContainSingle()
            .Which.Kind.Should().Be(SkillsTargetKind.LiteralTarget);
    }

    [Fact]
    public void Returns_literal_target_when_path_matches_a_registered_harness_layout()
    {
        using var tmp = new TempDir();
        var resolver = Build();
        var entry = new ConduitEntry
        {
            Name = "x",
            Source = new GitHubSource { Repo = "o/r" },
            Targets = [tmp.Combine(".claude", "skills")],
        };

        var resolved = resolver.Resolve(entry, tmp.Path, DefaultRegistry);
        resolved.Single().Kind.Should().Be(SkillsTargetKind.LiteralTarget);
    }

    [Fact]
    public void Discovery_finds_multiple_harnesses_under_a_general_target()
    {
        using var tmp = new TempDir();
        Directory.CreateDirectory(tmp.Combine("home", ".claude", "skills"));
        Directory.CreateDirectory(tmp.Combine("home", ".opencode", "skills"));

        var resolver = Build();
        var entry = new ConduitEntry
        {
            Name = "x",
            Source = new GitHubSource { Repo = "o/r" },
            Targets = [tmp.Combine("home")],
        };

        var resolved = resolver.Resolve(entry, tmp.Path, DefaultRegistry);
        resolved.Should().HaveCount(2);
        resolved.Should().OnlyContain(r => r.Kind == SkillsTargetKind.HarnessMatch);
    }

    [Fact]
    public void Falls_back_to_literal_when_no_harness_matches_and_no_explicit_filter()
    {
        using var tmp = new TempDir();
        Directory.CreateDirectory(tmp.Combine("empty"));

        var resolver = Build();
        var entry = new ConduitEntry
        {
            Name = "x",
            Source = new GitHubSource { Repo = "o/r" },
            Targets = [tmp.Combine("empty")],
        };

        var resolved = resolver.Resolve(entry, tmp.Path, DefaultRegistry);
        resolved.Should().ContainSingle().Which.Kind.Should().Be(SkillsTargetKind.LiteralTarget);
    }

    [Fact]
    public void Explicit_harness_filter_with_no_matches_throws()
    {
        using var tmp = new TempDir();
        Directory.CreateDirectory(tmp.Combine("empty"));

        var resolver = Build();
        var entry = new ConduitEntry
        {
            Name = "x",
            Source = new GitHubSource { Repo = "o/r" },
            Harness = HarnessSelector.Named(ClaudeFilter),
            Targets = [tmp.Combine("empty")],
        };

        var act = () => resolver.Resolve(entry, tmp.Path, DefaultRegistry);
        act.Should().Throw<StrategyPlanException>().WithMessage("*requires harness*claude*");
    }

    [Fact]
    public void Harness_false_skips_scan_and_treats_target_literally()
    {
        using var tmp = new TempDir();
        Directory.CreateDirectory(tmp.Combine("home", ".claude", "skills"));

        var resolver = Build();
        var entry = new ConduitEntry
        {
            Name = "x",
            Source = new GitHubSource { Repo = "o/r" },
            Harness = HarnessSelector.Disabled,
            Targets = [tmp.Combine("home")],
        };

        var resolved = resolver.Resolve(entry, tmp.Path, DefaultRegistry);
        resolved.Should().ContainSingle().Which.Kind.Should().Be(SkillsTargetKind.LiteralTarget);
    }

    [Fact]
    public void Harness_filter_restricts_matches_to_named_harnesses()
    {
        using var tmp = new TempDir();
        Directory.CreateDirectory(tmp.Combine("home", ".claude", "skills"));
        Directory.CreateDirectory(tmp.Combine("home", ".opencode", "skills"));

        var resolver = Build();
        var entry = new ConduitEntry
        {
            Name = "x",
            Source = new GitHubSource { Repo = "o/r" },
            Harness = HarnessSelector.Named(ClaudeFilter),
            Targets = [tmp.Combine("home")],
        };

        var resolved = resolver.Resolve(entry, tmp.Path, DefaultRegistry);
        resolved.Should().ContainSingle().Which.HarnessName.Should().Be("claude");
    }
}
