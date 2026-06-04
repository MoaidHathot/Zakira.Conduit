using Zakira.Conduit.Core.UnitTests.TestHelpers;
using Zakira.Conduit.Strategies.Skills;

namespace Zakira.Conduit.Core.UnitTests.Strategies.Skills;

public sealed class HarnessScannerTests
{
    private static readonly string[] ClaudeFilter = ["claude"];
    private static readonly string[] DotClaudeFilter = [".CLAUDE"];
    private static readonly string[] MyToolPaths = [".my-tool/skills"];
    [Fact]
    public void Returns_empty_when_target_does_not_exist()
    {
        var registry = HarnessRegistry.Merge(null);
        var scanner = new HarnessScanner(registry);
        scanner.Scan(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()), filter: null)
            .Should().BeEmpty();
    }

    [Fact]
    public void Finds_each_existing_search_path_as_its_own_match()
    {
        using var tmp = new TempDir();
        Directory.CreateDirectory(tmp.Combine(".opencode", "skills"));
        Directory.CreateDirectory(tmp.Combine(".config", "opencode", "skills"));
        Directory.CreateDirectory(tmp.Combine(".claude", "skills"));

        var scanner = new HarnessScanner(HarnessRegistry.Merge(null));
        var matches = scanner.Scan(tmp.Path, filter: null);

        // Two opencode hits (project + global) + one claude.
        matches.Should().HaveCount(3);
        matches.Where(m => m.HarnessName == "opencode").Should().HaveCount(2);
        matches.Where(m => m.HarnessName == "claude").Should().HaveCount(1);
    }

    [Fact]
    public void Skips_harnesses_whose_dirs_do_not_exist()
    {
        using var tmp = new TempDir();
        Directory.CreateDirectory(tmp.Combine(".claude", "skills"));

        var scanner = new HarnessScanner(HarnessRegistry.Merge(null));
        var matches = scanner.Scan(tmp.Path, filter: null);

        matches.Should().ContainSingle().Which.HarnessName.Should().Be("claude");
    }

    [Fact]
    public void Filter_restricts_to_named_harnesses()
    {
        using var tmp = new TempDir();
        Directory.CreateDirectory(tmp.Combine(".opencode", "skills"));
        Directory.CreateDirectory(tmp.Combine(".claude", "skills"));

        var scanner = new HarnessScanner(HarnessRegistry.Merge(null));
        var matches = scanner.Scan(tmp.Path, filter: ClaudeFilter);

        matches.Should().ContainSingle().Which.HarnessName.Should().Be("claude");
    }

    [Fact]
    public void Filter_normalises_dot_prefix_and_case()
    {
        using var tmp = new TempDir();
        Directory.CreateDirectory(tmp.Combine(".claude", "skills"));

        var scanner = new HarnessScanner(HarnessRegistry.Merge(null));
        scanner.Scan(tmp.Path, filter: DotClaudeFilter)
            .Should().ContainSingle();
    }

    [Fact]
    public void Custom_harness_in_merged_registry_is_picked_up()
    {
        using var tmp = new TempDir();
        Directory.CreateDirectory(tmp.Combine(".my-tool", "skills"));

        var registry = HarnessRegistry.Merge(new Dictionary<string, IReadOnlyList<string>?>
        {
            [".my-tool"] = MyToolPaths,
        });

        var scanner = new HarnessScanner(registry);
        scanner.Scan(tmp.Path, filter: null).Should().Contain(m => m.HarnessName == "my-tool");
    }
}
