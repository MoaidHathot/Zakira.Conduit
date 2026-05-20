using System.Text.Json;
using Zakira.Conduit.Manifest;

namespace Zakira.Conduit.Core.UnitTests.Manifest;

public sealed class PathSpecTests
{
    [Fact]
    public void Implicit_conversion_from_string_creates_a_PathSpec_with_no_alias()
    {
        PathSpec spec = "skills/code-review";
        spec.Path.Should().Be("skills/code-review");
        spec.As.Should().BeNull();
    }

    [Fact]
    public void ResolvedBasename_uses_alias_when_set()
    {
        new PathSpec("skills/code-review", As: "review").ResolvedBasename.Should().Be("review");
    }

    [Fact]
    public void ResolvedBasename_falls_back_to_basename_when_alias_is_missing()
    {
        new PathSpec("skills/code-review").ResolvedBasename.Should().Be("code-review");
        new PathSpec("flat-name").ResolvedBasename.Should().Be("flat-name");
        new PathSpec("with/trailing/").ResolvedBasename.Should().Be("trailing");
        new PathSpec(@"win\style\path").ResolvedBasename.Should().Be("path");
    }

    [Fact]
    public void Reads_compact_string_form()
    {
        var spec = JsonSerializer.Deserialize<PathSpec>("\"hello/world\"");
        spec.Should().BeEquivalentTo(new PathSpec("hello/world"));
    }

    [Theory]
    [InlineData("{\"path\":\"hello/world\",\"as\":\"hw\"}", "hello/world", "hw")]
    [InlineData("{\"from\":\"alt-key\",\"alias\":\"alias-key\"}", "alt-key", "alias-key")]
    [InlineData("{\"path\":\"no-alias\"}", "no-alias", null)]
    [InlineData("{\"path\":\"empty-alias\",\"as\":\"\"}", "empty-alias", null)]
    public void Reads_object_form_with_accepted_property_names(string json, string expectedPath, string? expectedAs)
    {
        var spec = JsonSerializer.Deserialize<PathSpec>(json);
        spec!.Path.Should().Be(expectedPath);
        spec.As.Should().Be(expectedAs);
    }

    [Fact]
    public void Empty_string_is_rejected_at_deserialization_time()
    {
        Action act = () => JsonSerializer.Deserialize<PathSpec>("\"\"");
        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Object_without_path_is_rejected()
    {
        Action act = () => JsonSerializer.Deserialize<PathSpec>("{\"as\":\"orphan\"}");
        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Round_trips_compact_form_when_no_alias()
    {
        var spec = new PathSpec("hello/world");
        var json = JsonSerializer.Serialize(spec);
        json.Should().Be("\"hello/world\"");
    }

    [Fact]
    public void Round_trips_object_form_when_aliased()
    {
        var spec = new PathSpec("hello/world", As: "hw");
        var json = JsonSerializer.Serialize(spec);
        json.Should().Contain("\"path\":\"hello/world\"");
        json.Should().Contain("\"as\":\"hw\"");
    }
}
