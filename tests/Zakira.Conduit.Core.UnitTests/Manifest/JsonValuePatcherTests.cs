using System.Text;
using Zakira.Conduit.Manifest;

namespace Zakira.Conduit.Core.UnitTests.Manifest;

public sealed class JsonValuePatcherTests
{
    [Fact]
    public void Patches_a_single_string_leaf_and_preserves_comments_and_trailing_commas()
    {
        const string input = """
            {
              // top-level comment preserved
              "version": 1,
              "entries": [
                {
                  "name": "alpha",
                  "source": {
                    "type": "github",
                    "repo": "owner/repo",
                    "branch": "main",
                    "commit": "old-sha", // inline preserved
                  },
                  "targets": [ "./out" ],
                },
              ]
            }
            """;

        var bytes = Encoding.UTF8.GetBytes(input);
        var edits = new[] { new JsonValuePatcher.StringEdit("entries[0].source.commit", "new-sha") };

        var patched = JsonValuePatcher.TryPatch(bytes, edits);

        patched.Should().NotBeNull();
        var result = Encoding.UTF8.GetString(patched!);
        result.Should().Contain("\"commit\": \"new-sha\"");
        result.Should().Contain("// top-level comment preserved");
        result.Should().Contain("// inline preserved");
        result.Should().Contain("[ \"./out\" ],"); // trailing comma kept inside arrays
        result.Should().NotContain("old-sha");
    }

    [Fact]
    public void Returns_null_when_a_target_path_does_not_exist()
    {
        const string input = """
            {
              "entries": [
                { "name": "x", "source": { "type": "github", "repo": "o/r", "branch": "main" }, "targets": ["./o"] }
              ]
            }
            """;
        var bytes = Encoding.UTF8.GetBytes(input);
        var edits = new[] { new JsonValuePatcher.StringEdit("entries[0].source.commit", "abc") };

        var patched = JsonValuePatcher.TryPatch(bytes, edits);

        patched.Should().BeNull("source.commit does not exist; patcher refuses to insert");
    }

    [Fact]
    public void Patches_multiple_targets_independently()
    {
        const string input = """
            {
              "entries": [
                { "name": "a", "source": { "type": "github", "repo": "o/r", "commit": "AAA" }, "targets": ["./a"] },
                { "name": "b", "source": { "type": "github", "repo": "o/r", "commit": "BBB" }, "targets": ["./b"] }
              ]
            }
            """;

        var bytes = Encoding.UTF8.GetBytes(input);
        var edits = new[]
        {
            new JsonValuePatcher.StringEdit("entries[0].source.commit", "aaa-new"),
            new JsonValuePatcher.StringEdit("entries[1].source.commit", "bbb-new"),
        };

        var patched = JsonValuePatcher.TryPatch(bytes, edits);
        patched.Should().NotBeNull();
        var result = Encoding.UTF8.GetString(patched!);
        result.Should().Contain("\"commit\": \"aaa-new\"");
        result.Should().Contain("\"commit\": \"bbb-new\"");
        result.Should().NotContain("AAA");
        result.Should().NotContain("BBB");
    }

    [Fact]
    public void No_op_when_new_value_equals_old_value()
    {
        const string input = """
            { "entries": [ { "name": "x", "source": { "commit": "same" } } ] }
            """;
        var bytes = Encoding.UTF8.GetBytes(input);
        var edits = new[] { new JsonValuePatcher.StringEdit("entries[0].source.commit", "same") };

        var patched = JsonValuePatcher.TryPatch(bytes, edits);
        patched.Should().NotBeNull();
        patched!.Should().Equal(bytes);
    }
}
