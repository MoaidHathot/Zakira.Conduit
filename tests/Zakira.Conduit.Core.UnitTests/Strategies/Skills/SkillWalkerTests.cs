using Zakira.Conduit.Core.UnitTests.TestHelpers;
using Zakira.Conduit.Strategies.Skills;

namespace Zakira.Conduit.Core.UnitTests.Strategies.Skills;

public sealed class SkillWalkerTests
{
    private static readonly string[] BetaGammaFilter = ["beta", "gamma"];
    [Fact]
    public void Returns_empty_when_root_missing()
    {
        SkillWalker.Walk("/no/such/path", explicitNames: null, originContentDirectory: "/no/such/path")
            .Should().BeEmpty();
    }

    [Fact]
    public void Discovers_skills_at_any_depth()
    {
        using var tmp = new TempDir();
        WriteSkill(tmp.Combine("alpha"), "alpha");
        WriteSkill(tmp.Combine("nested", "beta"), "beta");

        var skills = SkillWalker.Walk(tmp.Path, explicitNames: null, originContentDirectory: tmp.Path).ToList();
        skills.Should().HaveCount(2);
        skills.Should().Contain(s => s.Name == "alpha");
        skills.Should().Contain(s => s.Name == "beta");
    }

    [Fact]
    public void Does_not_descend_into_a_skill_folder()
    {
        using var tmp = new TempDir();
        WriteSkill(tmp.Combine("outer"), "outer");
        WriteSkill(tmp.Combine("outer", "inner"), "inner");

        var skills = SkillWalker.Walk(tmp.Path, explicitNames: null, originContentDirectory: tmp.Path).ToList();
        skills.Should().ContainSingle().Which.Name.Should().Be("outer");
    }

    [Fact]
    public void Explicit_filter_restricts_to_named_skills()
    {
        using var tmp = new TempDir();
        WriteSkill(tmp.Combine("alpha"), "alpha");
        WriteSkill(tmp.Combine("beta"), "beta");
        WriteSkill(tmp.Combine("gamma"), "gamma");

        var skills = SkillWalker.Walk(tmp.Path, explicitNames: BetaGammaFilter, originContentDirectory: tmp.Path)
            .Select(s => s.Name)
            .OrderBy(n => n)
            .ToArray();

        skills.Should().Equal("beta", "gamma");
    }

    [Fact]
    public void Skips_hidden_and_well_known_scaffolding_directories()
    {
        using var tmp = new TempDir();
        WriteSkill(tmp.Combine("ok"), "ok");
        WriteSkill(tmp.Combine(".hidden", "skipme"), "skipme");
        WriteSkill(tmp.Combine("node_modules", "x"), "x");

        var skills = SkillWalker.Walk(tmp.Path, explicitNames: null, originContentDirectory: tmp.Path)
            .Select(s => s.Name).ToList();

        skills.Should().ContainSingle().Which.Should().Be("ok");
    }

    [Fact]
    public void Sets_origin_content_directory_on_every_descriptor()
    {
        using var tmp = new TempDir();
        WriteSkill(tmp.Combine("alpha"), "alpha");

        var origin = tmp.Combine("alternate", "origin");
        Directory.CreateDirectory(origin);

        var skills = SkillWalker.Walk(tmp.Path, explicitNames: null, originContentDirectory: origin).ToList();
        skills.Should().ContainSingle()
            .Which.OriginContentDirectory.Should().Be(Path.GetFullPath(origin));
    }

    [Fact]
    public void Reads_frontmatter_when_present()
    {
        using var tmp = new TempDir();
        var dir = tmp.Combine("alpha");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "SKILL.md"),
            "---\nname: alpha\ndescription: hello world\n---\n\n# body\n");

        var skills = SkillWalker.Walk(tmp.Path, explicitNames: null, originContentDirectory: tmp.Path).ToList();
        skills.Single().Frontmatter.Should().NotBeNull();
        skills.Single().Frontmatter!.Name.Should().Be("alpha");
        skills.Single().Frontmatter!.Description.Should().Be("hello world");
    }

    private static void WriteSkill(string dir, string name)
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "SKILL.md"), $"---\nname: {name}\n---\n");
    }
}
