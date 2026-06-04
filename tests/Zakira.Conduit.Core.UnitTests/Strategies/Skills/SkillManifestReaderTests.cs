using Zakira.Conduit.Core.UnitTests.TestHelpers;
using Zakira.Conduit.Strategies.Skills;

namespace Zakira.Conduit.Core.UnitTests.Strategies.Skills;

public sealed class SkillManifestReaderTests
{
    [Fact]
    public void Returns_null_when_file_has_no_frontmatter_block()
    {
        using var tmp = new TempDir();
        var path = tmp.Combine("SKILL.md");
        File.WriteAllText(path, "# just a heading\nfoo bar baz\n");

        SkillManifestReader.Read(path).Should().BeNull();
    }

    [Fact]
    public void Extracts_name_and_description_from_frontmatter()
    {
        using var tmp = new TempDir();
        var path = tmp.Combine("SKILL.md");
        File.WriteAllText(path, "---\nname: my-skill\ndescription: does a thing\n---\nbody\n");

        var fm = SkillManifestReader.Read(path);
        fm.Should().NotBeNull();
        fm!.Name.Should().Be("my-skill");
        fm.Description.Should().Be("does a thing");
    }

    [Fact]
    public void Unquotes_single_and_double_quoted_scalars()
    {
        using var tmp = new TempDir();
        var path = tmp.Combine("SKILL.md");
        File.WriteAllText(path, "---\nname: \"quoted-name\"\ndescription: 'single quoted'\n---\n");

        var fm = SkillManifestReader.Read(path);
        fm!.Name.Should().Be("quoted-name");
        fm.Description.Should().Be("single quoted");
    }

    [Fact]
    public void Skips_nested_yaml_keys()
    {
        using var tmp = new TempDir();
        var path = tmp.Combine("SKILL.md");
        File.WriteAllText(path,
            "---\nname: outer\nmetadata:\n  inner: value\ndescription: hi\n---\n");

        var fm = SkillManifestReader.Read(path);
        fm!.Name.Should().Be("outer");
        fm.Description.Should().Be("hi");
    }

    [Fact]
    public void Strips_trailing_comment_after_whitespace()
    {
        using var tmp = new TempDir();
        var path = tmp.Combine("SKILL.md");
        File.WriteAllText(path, "---\nname: foo # inline comment\n---\n");

        SkillManifestReader.Read(path)!.Name.Should().Be("foo");
    }

    [Fact]
    public void Preserves_hash_inside_url_value()
    {
        using var tmp = new TempDir();
        var path = tmp.Combine("SKILL.md");
        File.WriteAllText(path, "---\nname: x\ndescription: see https://example/page#anchor\n---\n");

        SkillManifestReader.Read(path)!.Description.Should().Be("see https://example/page#anchor");
    }
}
