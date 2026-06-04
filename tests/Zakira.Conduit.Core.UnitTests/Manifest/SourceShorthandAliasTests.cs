using Zakira.Conduit.Manifest;
using Zakira.Conduit.Sources.Inference;

namespace Zakira.Conduit.Core.UnitTests.Manifest;

public sealed class SourceShorthandAliasTests
{
    private static SourceInferenceCoordinator BuildCoordinator() =>
        new(new ISourceInferrer[]
        {
            new LocalDirectorySourceInferrer(),
            new GitHubSourceInferrer(),
            new AzdoSourceInferrer(),
        });

    private static ConduitManifest LoadFromJson(string json)
    {
        var manifest = System.Text.Json.JsonSerializer.Deserialize<ConduitManifest>(json, ManifestJson.ReadOptions)
            ?? throw new InvalidOperationException("manifest null");
        return BuildCoordinator().Rewrite(manifest);
    }

    // --- in-string ' -> Name' suffix on bare URI shorthand ---

    [Fact]
    public void Arrow_suffix_sets_entry_name_when_name_omitted()
    {
        const string json = """
            {
              "entries": [
                {
                  "source": "https://github.com/owner/repo -> MyAlias",
                  "targets": [ "./out" ]
                }
              ]
            }
            """;

        var manifest = LoadFromJson(json);

        manifest.Entries.Should().HaveCount(1);
        manifest.Entries[0].Name.Should().Be("MyAlias");
        manifest.Entries[0].Source.Should().BeOfType<GitHubSource>();
    }

    [Fact]
    public void Arrow_suffix_alias_is_overridden_by_explicit_entry_name()
    {
        const string json = """
            {
              "entries": [
                {
                  "name": "Explicit",
                  "source": "https://github.com/owner/repo -> Ignored",
                  "targets": [ "./out" ]
                }
              ]
            }
            """;

        var manifest = LoadFromJson(json);

        manifest.Entries[0].Name.Should().Be("Explicit");
    }

    [Theory]
    [InlineData("https://github.com/o/r -> Has Space")]      // invalid chars
    [InlineData("https://github.com/o/r -> bad/slash")]
    [InlineData("https://github.com/o/r -> a@b")]
    public void Arrow_suffix_with_invalid_alias_charset_is_treated_as_part_of_uri(string sourceString)
    {
        // If the tail after ' -> ' doesn't match [A-Za-z0-9._-]+, the whole
        // string is treated as one URI; the URI inferrer will then reject it.
        var json = $$"""
            {
              "entries": [
                {
                  "name": "x",
                  "source": "{{sourceString}}",
                  "targets": [ "./out" ]
                }
              ]
            }
            """;

        var act = () => LoadFromJson(json);
        // Either it survives parsing as a single URI and an inferrer rejects
        // it, or the path looks plausible enough to infer but with wrong
        // contents. Either way the result is NOT a clean MyAlias-named entry.
        act.Should().Throw<Exception>();
    }

    [Fact]
    public void Arrow_suffix_with_blank_uri_is_rejected_at_parse()
    {
        const string json = """
            {
              "entries": [
                {
                  "source": "   -> NameOnly",
                  "targets": [ "./out" ]
                }
              ]
            }
            """;

        var act = () => LoadFromJson(json);
        act.Should().Throw<System.Text.Json.JsonException>().WithMessage("*non-empty URI*");
    }

    [Fact]
    public void Arrow_suffix_only_splits_on_last_occurrence()
    {
        // Even if some part of the URI legitimately contains ' -> ', the
        // converter splits on the LAST ' -> ' and requires the tail to match
        // the alias charset. Here the URI itself has no ' -> ', so this is
        // just the canonical case.
        const string json = """
            {
              "entries": [
                {
                  "source": "https://github.com/owner/repo -> Tail",
                  "targets": [ "./out" ]
                }
              ]
            }
            """;

        var manifest = LoadFromJson(json);
        manifest.Entries[0].Name.Should().Be("Tail");
    }

    // --- object wrapper { "source": ..., "as": "..." } ---

    [Fact]
    public void Wrapper_object_around_bare_string_sets_entry_name()
    {
        const string json = """
            {
              "entries": [
                {
                  "source": {
                    "source": "https://github.com/owner/repo",
                    "as": "WrappedName"
                  },
                  "targets": [ "./out" ]
                }
              ]
            }
            """;

        var manifest = LoadFromJson(json);
        manifest.Entries[0].Name.Should().Be("WrappedName");
        manifest.Entries[0].Source.Should().BeOfType<GitHubSource>();
    }

    [Fact]
    public void Wrapper_object_around_concrete_source_sets_entry_name()
    {
        const string json = """
            {
              "entries": [
                {
                  "source": {
                    "source": { "type": "github", "repo": "owner/repo", "path": "skills", "branch": "main" },
                    "as": "FromObject"
                  },
                  "targets": [ "./out" ]
                }
              ]
            }
            """;

        var manifest = LoadFromJson(json);
        manifest.Entries[0].Name.Should().Be("FromObject");
        var gh = manifest.Entries[0].Source.Should().BeOfType<GitHubSource>().Subject;
        gh.Path.Should().Be("skills");
        gh.Branch.Should().Be("main");
    }

    [Fact]
    public void Wrapper_without_as_is_rejected()
    {
        const string json = """
            {
              "entries": [
                {
                  "source": { "source": "https://github.com/o/r" },
                  "targets": [ "./out" ]
                }
              ]
            }
            """;

        var act = () => LoadFromJson(json);
        act.Should().Throw<System.Text.Json.JsonException>().WithMessage("*must include 'as'*");
    }

    [Fact]
    public void Wrapper_with_invalid_alias_charset_is_rejected()
    {
        const string json = """
            {
              "entries": [
                {
                  "source": { "source": "https://github.com/o/r", "as": "bad/name" },
                  "targets": [ "./out" ]
                }
              ]
            }
            """;

        var act = () => LoadFromJson(json);
        act.Should().Throw<System.Text.Json.JsonException>().WithMessage("*invalid characters*");
    }

    [Fact]
    public void Wrapper_with_extra_properties_is_rejected()
    {
        const string json = """
            {
              "entries": [
                {
                  "source": {
                    "source": "https://github.com/o/r",
                    "as": "Name",
                    "junk": true
                  },
                  "targets": [ "./out" ]
                }
              ]
            }
            """;

        var act = () => LoadFromJson(json);
        act.Should().Throw<System.Text.Json.JsonException>().WithMessage("*Unexpected property 'junk'*");
    }

    [Fact]
    public void Nested_wrapper_inside_wrapper_is_rejected()
    {
        const string json = """
            {
              "entries": [
                {
                  "source": {
                    "source": { "source": "https://github.com/o/r", "as": "Inner" },
                    "as": "Outer"
                  },
                  "targets": [ "./out" ]
                }
              ]
            }
            """;

        var act = () => LoadFromJson(json);
        act.Should().Throw<System.Text.Json.JsonException>().WithMessage("*another aliased wrapper*");
    }

    [Fact]
    public void Wrapper_around_array_is_rejected_at_parse()
    {
        const string json = """
            {
              "entries": [
                {
                  "source": {
                    "source": [ "https://github.com/o/r" ],
                    "as": "Outer"
                  },
                  "targets": [ "./out" ]
                }
              ]
            }
            """;

        var act = () => LoadFromJson(json);
        act.Should().Throw<System.Text.Json.JsonException>().WithMessage("*must not be an array*");
    }
}
