using System.Text.Json;
using Zakira.Conduit.Core.UnitTests.TestHelpers;
using Zakira.Conduit.Manifest;

namespace Zakira.Conduit.Core.UnitTests.Manifest;

public sealed class JsonManifestLoaderTests
{
    private const string ValidJson = """
        {
          "version": 1,
          "entries": [
            {
              "name": "code-review",
              "source": {
                "type": "github",
                "repo": "anthropics/skills",
                "path": "code-review",
                "branch": "main"
              },
              "targets": [ "~/.config/agents/skills" ]
            }
          ]
        }
        """;

    [Fact]
    public async Task LoadAsync_parses_a_valid_manifest()
    {
        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Path, "conduit.json");
        await File.WriteAllTextAsync(path, ValidJson);

        var loader = new JsonManifestLoader();
        var manifest = await loader.LoadAsync(path);

        manifest.Version.Should().Be(1);
        manifest.Entries.Should().HaveCount(1);

        var entry = manifest.Entries[0];
        entry.Name.Should().Be("code-review");
        entry.Targets.Should().ContainSingle().Which.Should().Be("~/.config/agents/skills");

        var github = entry.Source.Should().BeOfType<GitHubSkillSource>().Subject;
        github.Repo.Should().Be("anthropics/skills");
        github.Owner.Should().Be("anthropics");
        github.RepoName.Should().Be("skills");
        github.Slug.Should().Be("anthropics/skills");
        github.Path.Should().Be("code-review");
        github.Branch.Should().Be("main");
        github.ResolvedRef.Should().Be("main");
    }

    [Fact]
    public async Task LoadAsync_supports_polymorphic_discriminator()
    {
        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Path, "conduit.json");
        await File.WriteAllTextAsync(path, ValidJson);

        var manifest = await new JsonManifestLoader().LoadAsync(path);

        manifest.Entries[0].Source.Kind.Should().Be("github");
    }

    [Fact]
    public async Task LoadAsync_throws_with_path_when_missing()
    {
        var loader = new JsonManifestLoader();

        var act = () => loader.LoadAsync("does-not-exist.json");

        var ex = await act.Should().ThrowAsync<ManifestException>();
        ex.Which.ManifestPath.Should().Be("does-not-exist.json");
    }

    [Fact]
    public async Task LoadAsync_reports_unknown_source_type()
    {
        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Path, "conduit.json");
        const string badJson = """
            {
              "version": 1,
              "entries": [
                {
                  "name": "x",
                  "source": { "type": "totally-not-real", "url": "..." },
                  "targets": ["./out"]
                }
              ]
            }
            """;

        await File.WriteAllTextAsync(path, badJson);

        var act = () => new JsonManifestLoader().LoadAsync(path);
        await act.Should().ThrowAsync<ManifestException>();
    }

    [Fact]
    public async Task LoadAsync_reports_invalid_json()
    {
        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Path, "conduit.json");
        await File.WriteAllTextAsync(path, "{ this is not json ");

        var act = () => new JsonManifestLoader().LoadAsync(path);
        var ex = await act.Should().ThrowAsync<ManifestException>();
        ex.Which.InnerException.Should().BeOfType<JsonException>();
    }

    [Fact]
    public async Task LoadAsync_validation_errors_are_surfaced()
    {
        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Path, "conduit.json");
        const string badJson = """
            {
              "version": 1,
              "entries": [
                {
                  "name": "",
                  "source": { "type": "github", "repo": "o/r", "commit": "abc", "branch": "main" },
                  "targets": []
                }
              ]
            }
            """;
        await File.WriteAllTextAsync(path, badJson);

        var act = () => new JsonManifestLoader().LoadAsync(path);
        var ex = await act.Should().ThrowAsync<ManifestException>();
        ex.Which.Errors.Should().NotBeEmpty();
        ex.Which.Errors.Should().Contain(e => e.Contains("name", StringComparison.Ordinal));
        ex.Which.Errors.Should().Contain(e => e.Contains("targets", StringComparison.Ordinal));
        ex.Which.Errors.Should().Contain(e => e.Contains("mutually exclusive", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LoadAsync_accepts_comments_and_trailing_commas()
    {
        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Path, "conduit.json");
        const string jsonWithComments = """
            {
              // this is allowed
              "version": 1,
              "entries": [
                {
                  "name": "x",
                  "source": { "type": "github", "repo": "o/r" },
                  "targets": ["./out"],
                }
              ],
            }
            """;
        await File.WriteAllTextAsync(path, jsonWithComments);

        var manifest = await new JsonManifestLoader().LoadAsync(path);
        manifest.Entries.Should().ContainSingle();
    }
}
