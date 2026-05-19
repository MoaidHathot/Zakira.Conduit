using Zakira.Conduit.Core.UnitTests.TestHelpers;
using Zakira.Conduit.Sources.GitHub;

namespace Zakira.Conduit.Core.UnitTests.Sources;

public sealed class ZipballExtractorTests
{
    [Fact]
    public void Strips_github_top_level_folder()
    {
        var bytes = ZipballFactory.CreateZipball(
            topFolder: "owner-repo-abc1234",
            new Dictionary<string, string>
            {
                ["README.md"] = "hello",
                ["src/file.cs"] = "code",
            });

        using var tmp = new TempDir();
        using var ms = new MemoryStream(bytes);

        var count = InvokeExtract(ms, tmp.Path, subPath: null);

        count.Should().Be(2);
        File.Exists(Path.Combine(tmp.Path, "README.md")).Should().BeTrue();
        File.Exists(Path.Combine(tmp.Path, "src", "file.cs")).Should().BeTrue();
        File.ReadAllText(Path.Combine(tmp.Path, "README.md")).Should().Be("hello");
    }

    [Fact]
    public void Filters_to_subpath_and_strips_the_prefix()
    {
        var bytes = ZipballFactory.CreateZipball(
            topFolder: "owner-repo-abc",
            new Dictionary<string, string>
            {
                ["README.md"] = "ignore me",
                ["skills/code-review/SKILL.md"] = "skill",
                ["skills/code-review/data.json"] = "{}",
                ["skills/other-skill/SKILL.md"] = "other",
            });

        using var tmp = new TempDir();
        using var ms = new MemoryStream(bytes);

        var count = InvokeExtract(ms, tmp.Path, subPath: "skills/code-review");

        count.Should().Be(2);
        File.Exists(Path.Combine(tmp.Path, "SKILL.md")).Should().BeTrue();
        File.Exists(Path.Combine(tmp.Path, "data.json")).Should().BeTrue();
        Directory.Exists(Path.Combine(tmp.Path, "skills")).Should().BeFalse();
        Directory.Exists(Path.Combine(tmp.Path, "other-skill")).Should().BeFalse();
    }

    [Fact]
    public void Empty_subpath_means_whole_archive()
    {
        var bytes = ZipballFactory.CreateZipball(
            topFolder: "a-b-c",
            new Dictionary<string, string> { ["x.txt"] = "y" });

        using var tmp = new TempDir();
        using var ms = new MemoryStream(bytes);

        InvokeExtract(ms, tmp.Path, subPath: "").Should().Be(1);
        File.Exists(Path.Combine(tmp.Path, "x.txt")).Should().BeTrue();
    }

    [Fact]
    public void Rejects_path_traversal_entries()
    {
        // Manually build a zip where the inner entry escapes via "..".
        using var ms = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            zip.CreateEntry("owner-repo-abc/");
            var entry = zip.CreateEntry("owner-repo-abc/../escaped.txt");
            using var s = entry.Open();
            s.Write("danger"u8);
        }

        ms.Position = 0;
        using var tmp = new TempDir();

        var act = () => InvokeExtract(ms, tmp.Path, subPath: null);
        act.Should().Throw<InvalidDataException>().WithMessage("*escape extraction root*");
    }

    [Fact]
    public void Subpath_that_does_not_exist_produces_zero_files()
    {
        var bytes = ZipballFactory.CreateZipball("o-r-c", new Dictionary<string, string> { ["a.txt"] = "x" });
        using var tmp = new TempDir();
        using var ms = new MemoryStream(bytes);

        InvokeExtract(ms, tmp.Path, subPath: "does/not/exist").Should().Be(0);
    }

    private static int InvokeExtract(Stream archive, string dest, string? subPath) =>
        ZipballExtractor.Extract(archive, dest, subPath);
}
