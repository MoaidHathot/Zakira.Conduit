using Zakira.Conduit.Core.UnitTests.TestHelpers;
using Zakira.Conduit.Manifest;
using Zakira.Conduit.Sources.Inference;

namespace Zakira.Conduit.Core.UnitTests.Manifest;

public sealed class JsonManifestLoaderInferenceTests
{
    private static readonly string[] AzAuth = { "az" };

    private static JsonManifestLoader BuildLoader() =>
        new(new SkillSourceInferenceCoordinator(new ISkillSourceInferrer[]
        {
            new LocalDirectorySkillSourceInferrer(),
            new GitHubSkillSourceInferrer(),
            new AzdoSkillSourceInferrer(),
        }));

    [Fact]
    public async Task Uri_source_is_resolved_to_concrete_kind_at_load()
    {
        const string json = """
            {
              "version": 1,
              "entries": [
                {
                  "name": "gh",
                  "source": { "type": "uri", "uri": "https://github.com/anthropics/skills", "path": "code-review", "branch": "main" },
                  "targets": [ "./out" ]
                },
                {
                  "name": "azdo",
                  "source": { "type": "uri", "uri": "https://dev.azure.com/contoso/Conduit/_git/agent-skills", "branch": "main", "auth": "az" },
                  "targets": [ "./out" ]
                },
                {
                  "name": "local",
                  "source": { "type": "uri", "uri": "./local-skill-sample" },
                  "targets": [ "./out" ]
                }
              ]
            }
            """;

        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Path, "conduit.json");
        await File.WriteAllTextAsync(path, json);

        var loader = BuildLoader();
        var manifest = await loader.LoadAsync(path);

        manifest.Entries[0].Source.Should().BeOfType<GitHubSkillSource>();
        manifest.Entries[1].Source.Should().BeOfType<AzdoSkillSource>();
        manifest.Entries[2].Source.Should().BeOfType<LocalDirectorySkillSource>();

        ((GitHubSkillSource)manifest.Entries[0].Source).Branch.Should().Be("main");
        ((AzdoSkillSource)manifest.Entries[1].Source).Auth.Should().BeEquivalentTo(AzAuth);
        ((LocalDirectorySkillSource)manifest.Entries[2].Source).Path.Should().Be("./local-skill-sample");
    }

    [Fact]
    public async Task Unrecognised_uri_surfaces_as_ManifestException_with_entry_name()
    {
        const string json = """
            {
              "version": 1,
              "entries": [
                {
                  "name": "broken",
                  "source": { "type": "uri", "uri": "owner/repo" },
                  "targets": [ "./out" ]
                }
              ]
            }
            """;

        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Path, "conduit.json");
        await File.WriteAllTextAsync(path, json);

        var loader = BuildLoader();
        var act = async () => await loader.LoadAsync(path);
        var ex = await act.Should().ThrowAsync<ManifestException>();
        ex.Which.Message.Should().Contain("broken");
        ex.Which.Message.Should().Contain("owner/repo");
    }

    [Fact]
    public async Task Loader_without_coordinator_still_works_for_explicit_sources()
    {
        const string json = """
            {
              "version": 1,
              "entries": [
                {
                  "name": "gh",
                  "source": { "type": "github", "repo": "anthropics/skills" },
                  "targets": [ "./out" ]
                }
              ]
            }
            """;

        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Path, "conduit.json");
        await File.WriteAllTextAsync(path, json);

        var loader = new JsonManifestLoader();
        var manifest = await loader.LoadAsync(path);
        manifest.Entries[0].Source.Should().BeOfType<GitHubSkillSource>();
    }

    [Fact]
    public async Task Bare_string_source_shorthand_is_accepted_and_inferred()
    {
        const string json = """
            {
              "version": 1,
              "entries": [
                { "name": "gh",    "source": "https://github.com/anthropics/skills", "targets": [ "./out" ] },
                { "name": "azdo",  "source": "https://dev.azure.com/contoso/Conduit/_git/agent-skills", "targets": [ "./out" ] },
                { "name": "local", "source": "./local-skill-sample", "targets": [ "./out" ] }
              ]
            }
            """;

        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Path, "conduit.json");
        await File.WriteAllTextAsync(path, json);

        var loader = BuildLoader();
        var manifest = await loader.LoadAsync(path);

        manifest.Entries[0].Source.Should().BeOfType<GitHubSkillSource>();
        manifest.Entries[1].Source.Should().BeOfType<AzdoSkillSource>();
        manifest.Entries[2].Source.Should().BeOfType<LocalDirectorySkillSource>();
    }

    [Fact]
    public async Task Bare_string_source_rejects_unknown_uri()
    {
        const string json = """
            {
              "version": 1,
              "entries": [
                { "name": "bad", "source": "owner/repo", "targets": [ "./out" ] }
              ]
            }
            """;

        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Path, "conduit.json");
        await File.WriteAllTextAsync(path, json);

        var loader = BuildLoader();
        var act = async () => await loader.LoadAsync(path);
        await act.Should().ThrowAsync<ManifestException>();
    }

    [Fact]
    public async Task Bare_empty_string_source_is_rejected_with_a_clear_error()
    {
        const string json = """
            {
              "version": 1,
              "entries": [
                { "name": "bad", "source": "", "targets": [ "./out" ] }
              ]
            }
            """;

        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Path, "conduit.json");
        await File.WriteAllTextAsync(path, json);

        var loader = BuildLoader();
        var act = async () => await loader.LoadAsync(path);
        await act.Should().ThrowAsync<ManifestException>();
    }

    [Fact]
    public async Task Source_array_of_uris_expands_into_one_entry_per_element()
    {
        const string json = """
            {
              "version": 1,
              "entries": [
                {
                  "name": "bundle",
                  "source": [
                    "https://github.com/foo/bar/skills",
                    "https://github.com/foo/bar2/other",
                    "./vendor/skill"
                  ],
                  "targets": [ "~/.config/claude/skills" ]
                }
              ]
            }
            """;

        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Path, "conduit.json");
        await File.WriteAllTextAsync(path, json);

        var loader = BuildLoader();
        var manifest = await loader.LoadAsync(path);

        manifest.Entries.Should().HaveCount(3);
        manifest.Entries.Select(e => e.Name).Should().OnlyHaveUniqueItems();
        manifest.Entries.Should().AllSatisfy(e => e.Name.Should().StartWith("bundle-"));

        manifest.Entries[0].Source.Should().BeOfType<GitHubSkillSource>();
        ((GitHubSkillSource)manifest.Entries[0].Source).Path.Should().Be("skills");
        ((GitHubSkillSource)manifest.Entries[0].Source).Repo.Should().Be("foo/bar");

        manifest.Entries[1].Source.Should().BeOfType<GitHubSkillSource>();
        manifest.Entries[2].Source.Should().BeOfType<LocalDirectorySkillSource>();
    }

    [Fact]
    public async Task Source_array_with_mixed_inline_object_and_string_works()
    {
        const string json = """
            {
              "version": 1,
              "entries": [
                {
                  "name": "bundle",
                  "source": [
                    "https://github.com/foo/bar/skills",
                    { "type": "github", "repo": "baz/qux", "path": "other", "branch": "main" }
                  ],
                  "targets": [ "./out" ]
                }
              ]
            }
            """;

        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Path, "conduit.json");
        await File.WriteAllTextAsync(path, json);

        var loader = BuildLoader();
        var manifest = await loader.LoadAsync(path);

        manifest.Entries.Should().HaveCount(2);
        manifest.Entries.Should().AllSatisfy(e => e.Source.Should().BeOfType<GitHubSkillSource>());
        ((GitHubSkillSource)manifest.Entries[1].Source).Branch.Should().Be("main");
    }

    [Fact]
    public async Task Empty_source_array_is_rejected()
    {
        const string json = """
            {
              "version": 1,
              "entries": [
                { "name": "bad", "source": [], "targets": [ "./out" ] }
              ]
            }
            """;

        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Path, "conduit.json");
        await File.WriteAllTextAsync(path, json);

        var loader = BuildLoader();
        var act = async () => await loader.LoadAsync(path);
        await act.Should().ThrowAsync<ManifestException>();
    }
}
