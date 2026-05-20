using System.Text.Json;

namespace Zakira.Conduit.Core.UnitTests;

/// <summary>
///     Canary test: makes sure the runtime <c>ManifestValidator</c> and the
///     editor-facing JSON Schema at <c>schemas/conduit.schema.json</c> don't
///     drift away from each other in obvious ways. This test does not prove
///     full equivalence (the validator is richer than the schema can express),
///     but it catches the most likely regressions: missing source kinds,
///     missing fields, and the renamed/added properties added by recent work.
/// </summary>
public sealed class SchemaDriftCanaryTests
{
    private static readonly string SchemaPath = LocateSchema();

    [Fact]
    public void Schema_file_exists_and_parses()
    {
        File.Exists(SchemaPath).Should().BeTrue();
        using var doc = JsonDocument.Parse(File.ReadAllText(SchemaPath));
        doc.RootElement.GetProperty("$id").GetString().Should().Contain("conduit.schema.json");
    }

    [Fact]
    public void Schema_lists_every_known_source_kind()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(SchemaPath));
        var sourceVariants = doc.RootElement
            .GetProperty("$defs")
            .GetProperty("source")
            .GetProperty("oneOf")
            .EnumerateArray()
            .Select(v => v.GetProperty("$ref").GetString())
            .Where(s => s is not null)
            .Cast<string>()
            .ToArray();

        sourceVariants.Should().Contain("#/$defs/githubSource");
        sourceVariants.Should().Contain("#/$defs/localSource");
    }

    [Fact]
    public void Schema_github_source_advertises_path_paths_branch_commit_repo()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(SchemaPath));
        var props = doc.RootElement
            .GetProperty("$defs")
            .GetProperty("githubSource")
            .GetProperty("properties");

        foreach (var expected in new[] { "type", "repo", "path", "paths", "branch", "commit" })
        {
            var found = props.TryGetProperty(expected, out _);
            found.Should().BeTrue(because: "github source should declare every documented field, but '" + expected + "' is missing");
        }
    }

    [Fact]
    public void Schema_supports_path_or_aliased_for_targets_and_paths()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(SchemaPath));
        doc.RootElement.GetProperty("$defs").TryGetProperty("pathOrAliased", out _).Should().BeTrue(
            because: "we extended the schema with a string-or-object union called 'pathOrAliased'");
    }

    [Fact]
    public void Schema_no_longer_forbids_branch_plus_commit()
    {
        // Pin/update made these coexist; the schema's `not.anyOf` previously
        // listed { required: [branch, commit] } as forbidden. This test
        // guards against regressing that relaxation.
        using var doc = JsonDocument.Parse(File.ReadAllText(SchemaPath));
        var ghSource = doc.RootElement.GetProperty("$defs").GetProperty("githubSource");

        if (ghSource.TryGetProperty("not", out var notElement))
        {
            var notJson = notElement.GetRawText();
            notJson.Should().NotContain("\"branch\"", because: "branch+commit must be allowed together");
        }
    }

    private static string LocateSchema()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "schemas", "conduit.schema.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException("Cannot locate schemas/conduit.schema.json by walking up from the test output directory.");
    }
}
