using System.Text.Json;
using Zakira.Conduit.Manifest;
using Zakira.Conduit.Strategies;

namespace Zakira.Conduit.Core.UnitTests.Strategies;

public sealed class EntryJsonConvertersTests
{
    private static readonly string[] ExpectedHarnessFilter = ["claude", "opencode"];
    private static T Roundtrip<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, ManifestJson.ReadOptions)!;

    [Fact]
    public void GroupBy_reads_none_and_source()
    {
        Roundtrip<GroupByHolder>("""{"value":"none"}""").Value.Should().Be(GroupBy.None);
        Roundtrip<GroupByHolder>("""{"value":"source"}""").Value.Should().Be(GroupBy.Source);
        Roundtrip<GroupByHolder>("""{"value":"SOURCE"}""").Value.Should().Be(GroupBy.Source);
    }

    [Fact]
    public void GroupBy_throws_on_unknown_value()
    {
        var act = () => Roundtrip<GroupByHolder>("""{"value":"weird"}""");
        act.Should().Throw<JsonException>().WithMessage("*'weird'*");
    }

    [Fact]
    public void OnCollision_reads_all_three_modes()
    {
        Roundtrip<OnCollisionHolder>("""{"value":"error"}""").Value.Should().Be(OnCollisionPolicy.Error);
        Roundtrip<OnCollisionHolder>("""{"value":"skip"}""").Value.Should().Be(OnCollisionPolicy.Skip);
        Roundtrip<OnCollisionHolder>("""{"value":"last-wins"}""").Value.Should().Be(OnCollisionPolicy.LastWins);
        Roundtrip<OnCollisionHolder>("""{"value":"lastwins"}""").Value.Should().Be(OnCollisionPolicy.LastWins);
        Roundtrip<OnCollisionHolder>("""{"value":null}""").Value.Should().BeNull();
    }

    [Fact]
    public void Harness_accepts_boolean_form()
    {
        var trueValue = JsonSerializer.Deserialize<ConduitEntryHarnessOnly>("""{"harness":true}""", ManifestJson.ReadOptions)!;
        trueValue.Harness!.Enabled.Should().BeTrue();
        trueValue.Harness.Filter.Should().BeNull();

        var falseValue = JsonSerializer.Deserialize<ConduitEntryHarnessOnly>("""{"harness":false}""", ManifestJson.ReadOptions)!;
        falseValue.Harness!.Enabled.Should().BeFalse();
    }

    [Fact]
    public void Harness_accepts_string_array_form()
    {
        var entry = JsonSerializer.Deserialize<ConduitEntryHarnessOnly>(
            """{"harness":["claude","opencode"]}""",
            ManifestJson.ReadOptions)!;
        entry.Harness!.Enabled.Should().BeTrue();
        entry.Harness.Filter.Should().BeEquivalentTo(ExpectedHarnessFilter);
    }

    [Fact]
    public void Harness_array_rejects_empty()
    {
        var act = () => JsonSerializer.Deserialize<ConduitEntryHarnessOnly>("""{"harness":[]}""", ManifestJson.ReadOptions);
        act.Should().Throw<JsonException>();
    }

    private sealed record GroupByHolder
    {
        [System.Text.Json.Serialization.JsonPropertyName("value")]
        [System.Text.Json.Serialization.JsonConverter(typeof(GroupByJsonConverter))]
        public GroupBy Value { get; init; }
    }

    private sealed record OnCollisionHolder
    {
        [System.Text.Json.Serialization.JsonPropertyName("value")]
        [System.Text.Json.Serialization.JsonConverter(typeof(NullableOnCollisionJsonConverter))]
        public OnCollisionPolicy? Value { get; init; }
    }

    private sealed record ConduitEntryHarnessOnly
    {
        [System.Text.Json.Serialization.JsonPropertyName("harness")]
        [System.Text.Json.Serialization.JsonConverter(typeof(HarnessSelectorJsonConverter))]
        public HarnessSelector? Harness { get; init; }
    }
}

public sealed class HarnessRegistryConverterTests
{
    private static readonly string[] OpenCodePathOneElement = ["skills/"];
    private static readonly string[] OpenCodePathsMultiple = [".opencode/skills", ".config/opencode/skills"];

    [Fact]
    public void Accepts_string_value_promoted_to_one_element_list()
    {
        var json = """{"harnessRegistry":{"opencode":"skills/"}}""";
        var config = JsonSerializer.Deserialize<SkillsStrategyConfig>(json, ManifestJson.ReadOptions)!;
        config.HarnessRegistry!["opencode"].Should().BeEquivalentTo(OpenCodePathOneElement);
    }

    [Fact]
    public void Accepts_array_value()
    {
        var json = """{"harnessRegistry":{".opencode":[".opencode/skills",".config/opencode/skills"]}}""";
        var config = JsonSerializer.Deserialize<SkillsStrategyConfig>(json, ManifestJson.ReadOptions)!;
        config.HarnessRegistry![".opencode"].Should().BeEquivalentTo(OpenCodePathsMultiple);
    }

    [Fact]
    public void Accepts_null_value_to_remove_builtin()
    {
        var json = """{"harnessRegistry":{".claude":null}}""";
        var config = JsonSerializer.Deserialize<SkillsStrategyConfig>(json, ManifestJson.ReadOptions)!;
        config.HarnessRegistry![".claude"].Should().BeNull();
    }

    [Fact]
    public void Rejects_empty_array()
    {
        var json = """{"harnessRegistry":{".x":[]}}""";
        var act = () => JsonSerializer.Deserialize<SkillsStrategyConfig>(json, ManifestJson.ReadOptions);
        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Rejects_empty_string()
    {
        var json = """{"harnessRegistry":{".x":""}}""";
        var act = () => JsonSerializer.Deserialize<SkillsStrategyConfig>(json, ManifestJson.ReadOptions);
        act.Should().Throw<JsonException>();
    }
}
