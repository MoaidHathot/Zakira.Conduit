using Zakira.Conduit.Core.UnitTests.TestHelpers;
using Zakira.Conduit.Manifest;
using Zakira.Conduit.Strategies;

namespace Zakira.Conduit.Core.UnitTests.Strategies;

public sealed class StrategyValidatorTests
{
    private static ConduitEntry BasicEntry(string? strategy = null) => new()
    {
        Name = "x",
        Source = new GitHubSource { Repo = "o/r" },
        Targets = [new PathSpec("/tmp/t")],
        Strategy = strategy,
    };

    [Fact]
    public void Unknown_strategy_fails_validation_with_available_list()
    {
        var registry = StrategyTestHelper.BuildDefaultRegistry();
        var manifest = new ConduitManifest { Entries = [BasicEntry("bogus")] };

        var errors = ManifestValidator.Validate(manifest, registry);
        errors.Should().Contain(e => e.Contains("strategy 'bogus' is not a registered strategy"));
        errors.Should().Contain(e => e.Contains("Available: expand, flat, skills, wrap"));
    }

    [Fact]
    public void Skills_field_on_non_skills_strategy_errors()
    {
        var registry = StrategyTestHelper.BuildDefaultRegistry();
        var entry = BasicEntry("wrap") with { Skills = new[] { "alpha" } };
        var manifest = new ConduitManifest { Entries = [entry] };

        var errors = ManifestValidator.Validate(manifest, registry);
        errors.Should().Contain(e => e.Contains(".skills is only valid with strategy 'skills'"));
    }

    [Fact]
    public void Harness_field_on_non_skills_strategy_errors()
    {
        var registry = StrategyTestHelper.BuildDefaultRegistry();
        var entry = BasicEntry("wrap") with { Harness = HarnessSelector.EnabledAll };
        var manifest = new ConduitManifest { Entries = [entry] };

        var errors = ManifestValidator.Validate(manifest, registry);
        errors.Should().Contain(e => e.Contains(".harness is only valid with strategy 'skills'"));
    }

    [Fact]
    public void Empty_skill_name_in_filter_errors()
    {
        var registry = StrategyTestHelper.BuildDefaultRegistry();
        var entry = BasicEntry("skills") with { Skills = new[] { "ok", "  " } };
        var manifest = new ConduitManifest { Entries = [entry] };

        var errors = ManifestValidator.Validate(manifest, registry);
        errors.Should().Contain(e => e.Contains("skills[1]"));
    }

    [Fact]
    public void Manifest_with_strategy_wrap_explicit_is_equivalent_to_default()
    {
        var registry = StrategyTestHelper.BuildDefaultRegistry();
        var manifest = new ConduitManifest { Entries = [BasicEntry("wrap")] };
        var errors = ManifestValidator.Validate(manifest, registry);
        errors.Should().BeEmpty();
    }

    [Fact]
    public void Manifest_with_no_strategy_passes_with_legacy_validator()
    {
        // Calling the older overload (no registry) must keep working for
        // library consumers that don't wire up strategies.
        var manifest = new ConduitManifest { Entries = [BasicEntry()] };
        var errors = ManifestValidator.Validate(manifest);
        errors.Should().BeEmpty();
    }
}
