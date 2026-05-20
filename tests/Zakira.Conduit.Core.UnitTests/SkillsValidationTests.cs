using System.Text.RegularExpressions;

namespace Zakira.Conduit.Core.UnitTests;

/// <summary>
///     Validates every <c>SKILL.md</c> under <c>skills/</c> against the
///     <a href="https://agentskills.io/specification">Agent Skills</a> format:
///     a YAML frontmatter block with at least <c>name</c> + <c>description</c>,
///     where <c>name</c> matches the parent directory and conforms to the
///     spec's character rules.
/// </summary>
public sealed class SkillsValidationTests
{
    private static readonly string SkillsRoot = LocateSkillsRoot();

    public static TheoryData<string> AllSkillFiles
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var file in Directory.EnumerateFiles(SkillsRoot, "SKILL.md", SearchOption.AllDirectories))
            {
                data.Add(file);
            }

            return data;
        }
    }

    [Fact]
    public void At_least_one_skill_is_bundled()
    {
        Directory.Exists(SkillsRoot).Should().BeTrue();
        Directory.EnumerateFiles(SkillsRoot, "SKILL.md", SearchOption.AllDirectories).Any()
            .Should().BeTrue(because: "the repo should ship at least one agent skill");
    }

    [Theory]
    [MemberData(nameof(AllSkillFiles))]
    public void Skill_has_valid_frontmatter_and_name_matches_directory(string skillPath)
    {
        var raw = File.ReadAllText(skillPath);

        var (frontmatter, body) = SplitFrontmatter(raw);
        frontmatter.Should().NotBeNullOrWhiteSpace(because: $"'{skillPath}' must begin with a YAML frontmatter block");
        body.Should().NotBeNullOrWhiteSpace(because: $"'{skillPath}' must have a body after the frontmatter");

        var fields = ParseFlatYaml(frontmatter);

        fields.Should().ContainKey("name", because: "the spec requires a 'name' field");
        fields.Should().ContainKey("description", because: "the spec requires a 'description' field");

        var name = fields["name"];
        name.Length.Should().BeInRange(1, 64);
        Regex.IsMatch(name, "^[a-z0-9]+(-[a-z0-9]+)*$").Should().BeTrue(
            because: "'name' must be lowercase alphanumeric with single hyphens, not starting or ending with a hyphen (got '" + name + "')");

        var description = fields["description"];
        description.Length.Should().BeInRange(1, 1024);

        var parentDir = Path.GetFileName(Path.GetDirectoryName(skillPath))!;
        name.Should().Be(parentDir, because: "'name' must match the parent directory name");
    }

    /// <summary>
    ///     Splits "---\nyaml\n---\nbody" into (yaml, body). Returns (null, raw)
    ///     if the file doesn't begin with a fence.
    /// </summary>
    private static (string? Frontmatter, string Body) SplitFrontmatter(string raw)
    {
        // The first line must be exactly "---".
        if (!raw.StartsWith("---", StringComparison.Ordinal))
        {
            return (null, raw);
        }

        // Find the closing fence on its own line.
        var match = Regex.Match(raw, @"^---\s*\r?\n(?<fm>.*?)\r?\n---\s*\r?\n?(?<body>.*)$",
            RegexOptions.Singleline);

        if (!match.Success)
        {
            return (null, raw);
        }

        return (match.Groups["fm"].Value, match.Groups["body"].Value);
    }

    /// <summary>
    ///     Parses the (very narrow) shape of YAML our spec asserts on: flat
    ///     <c>key: value</c> pairs at the top level. Nested mappings (the
    ///     optional <c>metadata:</c> field) are skipped over rather than parsed
    ///     so we don't pull in a YAML library just for this canary.
    /// </summary>
    private static Dictionary<string, string> ParseFlatYaml(string yaml)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var lines = yaml.Split('\n');
        var insideNested = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            // Indented lines belong to the previous key's nested mapping; skip.
            if (line[0] == ' ' || line[0] == '\t')
            {
                continue;
            }

            insideNested = false;

            var colonIndex = line.IndexOf(':', StringComparison.Ordinal);
            if (colonIndex <= 0)
            {
                continue;
            }

            var key = line[..colonIndex].Trim();
            var value = line[(colonIndex + 1)..].Trim();

            if (string.IsNullOrEmpty(value))
            {
                // key with no inline value -> nested mapping follows
                insideNested = true;
                continue;
            }

            // Strip surrounding quotes if any.
            if (value.Length >= 2 &&
                ((value[0] == '"' && value[^1] == '"') ||
                 (value[0] == '\'' && value[^1] == '\'')))
            {
                value = value[1..^1];
            }

            result[key] = value;
        }

        // Suppress unused-variable warning for the bookkeeping flag.
        _ = insideNested;

        return result;
    }

    private static string LocateSkillsRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "skills");
            if (Directory.Exists(candidate) && Directory.GetFiles(candidate, "SKILL.md", SearchOption.AllDirectories).Length > 0)
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Cannot locate the skills/ directory by walking up from the test assembly output.");
    }
}
