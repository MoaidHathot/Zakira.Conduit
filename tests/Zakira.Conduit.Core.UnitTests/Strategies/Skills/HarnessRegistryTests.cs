using Zakira.Conduit.Strategies.Skills;

namespace Zakira.Conduit.Core.UnitTests.Strategies.Skills;

public sealed class HarnessRegistryTests
{
    private static readonly string[] OpenCodeDefaultPaths = [".opencode/skills", ".config/opencode/skills"];
    private static readonly string[] MyToolPaths = ["skills/"];
    private static readonly string[] OpenCodeOverride = ["custom/path"];
    private static readonly string[] OpenCodeAlternate = ["alt/skills"];

    [Fact]
    public void NormaliseKey_strips_leading_dot_and_lowercases()
    {
        HarnessRegistry.NormaliseKey(".OpenCode").Should().Be("opencode");
        HarnessRegistry.NormaliseKey("OpenCode").Should().Be("opencode");
        HarnessRegistry.NormaliseKey("  .CLAUDE  ").Should().Be("claude");
    }

    [Fact]
    public void Merge_returns_builtins_when_no_overrides()
    {
        var merged = HarnessRegistry.Merge(null);
        merged.Should().ContainKeys("opencode", "claude", "codex", "agents");
        merged["opencode"].Should().BeEquivalentTo(OpenCodeDefaultPaths);
    }

    [Fact]
    public void Merge_adds_custom_entries()
    {
        var overrides = new Dictionary<string, IReadOnlyList<string>?>(StringComparer.OrdinalIgnoreCase)
        {
            [".my-tool"] = MyToolPaths,
        };

        var merged = HarnessRegistry.Merge(overrides);
        merged.Should().ContainKey("my-tool");
        merged["my-tool"].Should().BeEquivalentTo(MyToolPaths);
    }

    [Fact]
    public void Merge_with_null_value_removes_builtin()
    {
        var overrides = new Dictionary<string, IReadOnlyList<string>?>(StringComparer.OrdinalIgnoreCase)
        {
            [".claude"] = null,
        };

        var merged = HarnessRegistry.Merge(overrides);
        merged.Should().NotContainKey("claude");
        merged.Should().ContainKey("opencode"); // others survive
    }

    [Fact]
    public void Merge_with_replacement_overrides_builtin_paths()
    {
        var overrides = new Dictionary<string, IReadOnlyList<string>?>(StringComparer.OrdinalIgnoreCase)
        {
            ["opencode"] = OpenCodeOverride,
        };

        var merged = HarnessRegistry.Merge(overrides);
        merged["opencode"].Should().BeEquivalentTo(OpenCodeOverride);
    }

    [Fact]
    public void Merge_normalises_keys_so_dot_prefix_is_irrelevant()
    {
        var overrides = new Dictionary<string, IReadOnlyList<string>?>(StringComparer.OrdinalIgnoreCase)
        {
            [".opencode"] = OpenCodeAlternate,
        };

        var merged = HarnessRegistry.Merge(overrides);
        merged["opencode"].Should().BeEquivalentTo(OpenCodeAlternate);
    }
}
