using Zakira.Conduit.Manifest;
using Zakira.Conduit.Sources.Inference;

namespace Zakira.Conduit.Core.UnitTests.Manifest;

public sealed class IncludeExcludeForwardingTests
{
    private static readonly string[] MdGlob = { "**/*.md" };
    private static readonly string[] BinGlob = { "bin/**" };

    private static SourceInferenceCoordinator BuildCoordinator() =>
        new(new ISourceInferrer[]
        {
            new LocalDirectorySourceInferrer(),
            new GitHubSourceInferrer(),
            new AzdoSourceInferrer(),
        });

    private static ConduitManifest Rewrite(string json) =>
        BuildCoordinator().Rewrite(
            System.Text.Json.JsonSerializer.Deserialize<ConduitManifest>(json, ManifestJson.ReadOptions)
            ?? throw new InvalidOperationException("manifest null"));

    [Fact]
    public void Uri_source_forwards_include_exclude_to_github()
    {
        const string json = """
            {
              "entries": [
                {
                  "name": "x",
                  "source": {
                    "type": "uri",
                    "uri": "https://github.com/owner/repo",
                    "include": [ "**/*.md" ],
                    "exclude": [ "bin/**" ]
                  },
                  "targets": [ "./out" ]
                }
              ]
            }
            """;

        var manifest = Rewrite(json);
        var gh = manifest.Entries[0].Source.Should().BeOfType<GitHubSource>().Subject;
        gh.Include.Should().BeEquivalentTo(MdGlob);
        gh.Exclude.Should().BeEquivalentTo(BinGlob);
    }

    [Fact]
    public void Uri_source_forwards_include_exclude_to_azdo()
    {
        const string json = """
            {
              "entries": [
                {
                  "name": "x",
                  "source": {
                    "type": "uri",
                    "uri": "https://dev.azure.com/contoso/Conduit/_git/agent-skills",
                    "include": [ "**/*.md" ],
                    "exclude": [ "bin/**" ]
                  },
                  "targets": [ "./out" ]
                }
              ]
            }
            """;

        var manifest = Rewrite(json);
        var azdo = manifest.Entries[0].Source.Should().BeOfType<AzdoSource>().Subject;
        azdo.Include.Should().BeEquivalentTo(MdGlob);
        azdo.Exclude.Should().BeEquivalentTo(BinGlob);
    }

    [Fact]
    public void Uri_source_forwards_include_exclude_to_local()
    {
        const string json = """
            {
              "entries": [
                {
                  "name": "x",
                  "source": {
                    "type": "uri",
                    "uri": "./skills",
                    "include": [ "**/*.md" ],
                    "exclude": [ "bin/**" ]
                  },
                  "targets": [ "./out" ]
                }
              ]
            }
            """;

        var manifest = Rewrite(json);
        var local = manifest.Entries[0].Source.Should().BeOfType<LocalDirectorySource>().Subject;
        local.Include.Should().BeEquivalentTo(MdGlob);
        local.Exclude.Should().BeEquivalentTo(BinGlob);
    }

    [Fact]
    public void Blank_glob_pattern_is_rejected_by_validator()
    {
        var manifest = new ConduitManifest
        {
            Version = 1,
            Entries =
            [
                new ConduitEntry
                {
                    Name = "x",
                    Source = new GitHubSource
                    {
                        Repo = "owner/repo",
                        Include = new[] { "**/*.md", "   " },
                    },
                    Targets = [new PathSpec("./out")],
                },
            ],
        };

        var errors = ManifestValidator.Validate(manifest);
        errors.Should().Contain(e => e.Contains(".source.include[1]", StringComparison.Ordinal));
    }
}
