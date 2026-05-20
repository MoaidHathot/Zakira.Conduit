using System.Text.Json.Nodes;
using Zakira.Conduit.Core.UnitTests.TestHelpers;
using Zakira.Conduit.Manifest;

namespace Zakira.Conduit.Core.UnitTests.Manifest;

public sealed class JsonNodeManifestWriterTests
{
    private const string SourceManifest = """
        {
          "$schema": "../schemas/conduit.schema.json",
          "version": 1,
          "entries": [
            {
              "name": "alpha",
              "description": "first",
              "source": {
                "type": "github",
                "repo": "anthropics/skills",
                "branch": "main",
                "commit": "0000000000000000000000000000000000000000"
              },
              "targets": [ "./out/alpha" ]
            },
            {
              "name": "beta",
              "source": {
                "type": "local",
                "path": "./skills/beta"
              },
              "targets": [ "./out/beta" ]
            }
          ]
        }
        """;

    [Fact]
    public async Task Rewrite_updates_only_the_requested_field_and_keeps_everything_else()
    {
        using var tmp = new TempDir();
        var manifestPath = tmp.Combine("conduit.json");
        await File.WriteAllTextAsync(manifestPath, SourceManifest);

        var writer = new JsonNodeManifestWriter();
        const string newSha = "abcdef0123456789abcdef0123456789abcdef01";

        var backupPath = await writer.RewriteAsync(manifestPath, root =>
        {
            var entries = (JsonArray)root["entries"]!;
            var alpha = entries.OfType<JsonObject>().Single(e => (string?)e["name"] == "alpha");
            ((JsonObject)alpha["source"]!)["commit"] = newSha;
        });

        backupPath.Should().Be(manifestPath + JsonNodeManifestWriter.BackupSuffix);
        File.Exists(backupPath).Should().BeTrue();
        (await File.ReadAllTextAsync(backupPath)).Should().Be(SourceManifest);

        var written = await File.ReadAllTextAsync(manifestPath);
        using var doc = System.Text.Json.JsonDocument.Parse(written);
        var rewrittenAlpha = doc.RootElement.GetProperty("entries")[0];

        rewrittenAlpha.GetProperty("name").GetString().Should().Be("alpha");
        rewrittenAlpha.GetProperty("description").GetString().Should().Be("first");
        rewrittenAlpha.GetProperty("source").GetProperty("commit").GetString().Should().Be(newSha);

        // branch + repo must still be there - i.e., we didn't lose any fields.
        rewrittenAlpha.GetProperty("source").GetProperty("repo").GetString().Should().Be("anthropics/skills");
        rewrittenAlpha.GetProperty("source").GetProperty("branch").GetString().Should().Be("main");

        // Top-level `$schema` must be preserved across the rewrite.
        doc.RootElement.GetProperty("$schema").GetString().Should().Be("../schemas/conduit.schema.json");

        // The 'beta' entry must be untouched.
        var beta = doc.RootElement.GetProperty("entries")[1];
        beta.GetProperty("name").GetString().Should().Be("beta");
        beta.GetProperty("source").GetProperty("type").GetString().Should().Be("local");
    }

    [Fact]
    public async Task Rewrite_accepts_comments_and_trailing_commas_in_the_source_file()
    {
        using var tmp = new TempDir();
        var manifestPath = tmp.Combine("conduit.json");
        const string withComments = """
            {
              // top-level comment
              "version": 1,
              "entries": [
                {
                  "name": "alpha",
                  /* block comment */
                  "source": { "type": "github", "repo": "o/r", "branch": "main" },
                  "targets": [ "./out" ],
                },
              ],
            }
            """;
        await File.WriteAllTextAsync(manifestPath, withComments);

        var writer = new JsonNodeManifestWriter();
        await writer.RewriteAsync(manifestPath, root =>
        {
            var entries = (JsonArray)root["entries"]!;
            var src = (JsonObject)((JsonObject)entries[0]!)["source"]!;
            src["commit"] = "abc";
        });

        var written = await File.ReadAllTextAsync(manifestPath);
        using var doc = System.Text.Json.JsonDocument.Parse(written);
        doc.RootElement.GetProperty("entries")[0].GetProperty("source").GetProperty("commit").GetString().Should().Be("abc");
    }

    [Fact]
    public async Task Rewrite_throws_when_the_manifest_is_missing()
    {
        using var tmp = new TempDir();
        var manifestPath = tmp.Combine("does-not-exist.json");

        var writer = new JsonNodeManifestWriter();
        var act = () => writer.RewriteAsync(manifestPath, _ => { });

        await act.Should().ThrowAsync<ManifestException>();
    }

    [Fact]
    public async Task Rewrite_throws_when_the_top_level_is_not_an_object()
    {
        using var tmp = new TempDir();
        var manifestPath = tmp.Combine("array.json");
        await File.WriteAllTextAsync(manifestPath, "[1, 2, 3]");

        var writer = new JsonNodeManifestWriter();
        var act = () => writer.RewriteAsync(manifestPath, _ => { });

        await act.Should().ThrowAsync<ManifestException>().WithMessage("*top-level JSON object*");
    }

    [Fact]
    public async Task Rewrite_does_not_leave_tmp_files_behind_on_success()
    {
        using var tmp = new TempDir();
        var manifestPath = tmp.Combine("conduit.json");
        await File.WriteAllTextAsync(manifestPath, SourceManifest);

        await new JsonNodeManifestWriter().RewriteAsync(manifestPath, root =>
        {
            var entries = (JsonArray)root["entries"]!;
            ((JsonObject)((JsonObject)entries[0]!)["source"]!)["commit"] = "abc";
        });

        Directory.EnumerateFiles(tmp.Path, "*.tmp-*").Should().BeEmpty();
    }
}
