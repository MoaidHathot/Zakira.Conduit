using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Zakira.Conduit.Core.UnitTests.TestHelpers;
using Zakira.Conduit.DependencyInjection;
using Zakira.Conduit.Manifest;

namespace Zakira.Conduit.Core.UnitTests.Strategies;

/// <summary>
///     Verifies that a manifest carrying the new strategies/groupBy/onCollision/
///     skills/harness fields round-trips through the full loader pipeline
///     (inference -> validator -> resolved-destination validator) without
///     errors and surfaces the parsed values to downstream code.
/// </summary>
public sealed class StrategiesManifestLoadTests
{
    private static readonly string[] ExpectedSkillsFilter = ["one", "two"];
    private static readonly string[] ExpectedHarnessFilter = ["opencode"];
    private static readonly string[] ExpectedClaudeFilter = ["claude"];
    [Fact]
    public async Task Manifest_with_strategies_section_loads_and_exposes_resolved_config()
    {
        using var tmp = new TempDir();
        var path = tmp.Combine("conduit.json");

        var json = """
        {
          "version": 1,
          "strategies": {
            "skills": {
              "harnessRegistry": {
                ".my-tool": "skills/",
                ".claude": null,
                ".other": ["a/skills", "b/skills"]
              },
              "onCollision": "skip"
            }
          },
          "entries": [
            {
              "name": "alpha",
              "strategy": "skills",
              "groupBy": "source",
              "skills": ["one", "two"],
              "harness": ["opencode"],
              "source": "https://github.com/owner/repo",
              "targets": ["~"]
            }
          ]
        }
        """;

        await File.WriteAllTextAsync(path, json);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddConduitCore();
        var sp = services.BuildServiceProvider();

        var loader = sp.GetRequiredService<IManifestLoader>();
        var manifest = await loader.LoadAsync(path);

        manifest.Strategies.Should().NotBeNull();
        manifest.Strategies!.Skills.Should().NotBeNull();
        manifest.Strategies.Skills!.OnCollision.Should().Be(Conduit.Strategies.OnCollisionPolicy.Skip);
        manifest.Strategies.Skills.HarnessRegistry.Should().ContainKey(".my-tool");
        manifest.Strategies.Skills.HarnessRegistry![".claude"].Should().BeNull();

        var entry = manifest.Entries.Single();
        entry.Strategy.Should().Be("skills");
        entry.GroupBy.Should().Be(Conduit.Strategies.GroupBy.Source);
        entry.Skills.Should().BeEquivalentTo(ExpectedSkillsFilter);
        entry.Harness.Should().NotBeNull();
        entry.Harness!.Filter.Should().BeEquivalentTo(ExpectedHarnessFilter);
    }

    [Fact]
    public async Task Array_source_propagates_strategy_groupBy_harness_to_expanded_entries()
    {
        using var tmp = new TempDir();
        var path = tmp.Combine("conduit.json");

        var json = """
        {
          "version": 1,
          "entries": [
            {
              "name": "parent",
              "strategy": "skills",
              "groupBy": "source",
              "harness": ["claude"],
              "source": [
                "https://github.com/foo/aaa",
                "https://github.com/bar/bbb"
              ],
              "targets": ["~"]
            }
          ]
        }
        """;

        await File.WriteAllTextAsync(path, json);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddConduitCore();
        var sp = services.BuildServiceProvider();

        var loader = sp.GetRequiredService<IManifestLoader>();
        var manifest = await loader.LoadAsync(path);

        // Array source expanded to 2 entries; both should carry the parent's
        // strategy/groupBy/harness.
        manifest.Entries.Should().HaveCount(2);
        foreach (var entry in manifest.Entries)
        {
            entry.Strategy.Should().Be("skills");
            entry.GroupBy.Should().Be(Conduit.Strategies.GroupBy.Source);
            entry.Harness.Should().NotBeNull();
            entry.Harness!.Filter.Should().BeEquivalentTo(ExpectedClaudeFilter);
        }
    }
}
