using Microsoft.Extensions.Logging.Abstractions;
using Zakira.Conduit.Core.UnitTests.TestHelpers;
using Zakira.Conduit.Manifest;
using Zakira.Conduit.Paths;
using Zakira.Conduit.Sources;
using Zakira.Conduit.Sources.Local;

namespace Zakira.Conduit.Core.UnitTests.Sources;

public sealed class LocalDirectorySkillSourceFetcherTests
{
    [Fact]
    public async Task Returns_a_fetched_source_pointing_at_the_local_directory()
    {
        using var tmp = new TempDir();
        var sourceDir = tmp.Combine("my-skill");
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "SKILL.md"), "hello");

        var fetcher = BuildFetcher();
        var context = new FetchContext(tmp.Combine("conduit.json"));
        var source = new LocalDirectorySkillSource { Path = sourceDir };

        await using var fetched = await fetcher.FetchAsync(source, context);

        fetched.Contents[0].ContentDirectory.Should().Be(Path.GetFullPath(sourceDir));
        fetched.ResolvedRef.Should().BeNull();
        File.Exists(Path.Combine(fetched.Contents[0].ContentDirectory, "SKILL.md")).Should().BeTrue();
    }

    [Fact]
    public async Task Dispose_does_not_delete_the_user_directory()
    {
        using var tmp = new TempDir();
        var sourceDir = tmp.Combine("my-skill");
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "SKILL.md"), "hello");

        var fetcher = BuildFetcher();
        var context = new FetchContext(tmp.Combine("conduit.json"));
        var source = new LocalDirectorySkillSource { Path = sourceDir };

        await using (var _ = await fetcher.FetchAsync(source, context)) { }

        Directory.Exists(sourceDir).Should().BeTrue();
        File.Exists(Path.Combine(sourceDir, "SKILL.md")).Should().BeTrue();
    }

    [Fact]
    public async Task Relative_path_is_resolved_against_manifest_directory()
    {
        using var tmp = new TempDir();
        var skillDir = tmp.Combine("skills", "alpha");
        Directory.CreateDirectory(skillDir);
        await File.WriteAllTextAsync(Path.Combine(skillDir, "SKILL.md"), "alpha");

        var fetcher = BuildFetcher();
        var context = new FetchContext(tmp.Combine("conduit.json"));
        var source = new LocalDirectorySkillSource { Path = "./skills/alpha" };

        await using var fetched = await fetcher.FetchAsync(source, context);

        fetched.Contents[0].ContentDirectory.Should().Be(Path.GetFullPath(skillDir));
    }

    [Fact]
    public async Task Tilde_is_expanded_to_the_home_directory()
    {
        using var tmp = new TempDir();
        var fakeHome = tmp.Combine("home");
        var skill = Path.Combine(fakeHome, "skills");
        Directory.CreateDirectory(skill);
        await File.WriteAllTextAsync(Path.Combine(skill, "SKILL.md"), "x");

        var env = new FakeEnvironment
        {
            HomeDirectory = fakeHome,
            CurrentDirectory = tmp.Path,
            IsWindows = OperatingSystem.IsWindows(),
        };
        var resolver = new DefaultPathResolver(env);
        var fetcher = new LocalDirectorySkillSourceFetcher(resolver, NullLogger<LocalDirectorySkillSourceFetcher>.Instance);

        var context = new FetchContext(tmp.Combine("conduit.json"));
        var source = new LocalDirectorySkillSource { Path = "~/skills" };

        await using var fetched = await fetcher.FetchAsync(source, context);

        fetched.Contents[0].ContentDirectory.Should().Be(Path.GetFullPath(skill));
    }

    [Fact]
    public async Task Missing_directory_throws_LocalSourceNotFound()
    {
        using var tmp = new TempDir();
        var fetcher = BuildFetcher();
        var context = new FetchContext(tmp.Combine("conduit.json"));
        var source = new LocalDirectorySkillSource { Path = tmp.Combine("does-not-exist") };

        var act = () => fetcher.FetchAsync(source, context);
        var ex = await act.Should().ThrowAsync<LocalSourceNotFoundException>();
        ex.Which.ResolvedPath.Should().Be(Path.GetFullPath(source.Path));
    }

    [Fact]
    public async Task Wrong_source_type_throws()
    {
        var fetcher = BuildFetcher();
        var context = new FetchContext(Path.Combine(Path.GetTempPath(), "x.json"));
        var act = () => fetcher.FetchAsync(new GitHubSkillSource { Repo = "o/r" }, context);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    private static LocalDirectorySkillSourceFetcher BuildFetcher()
    {
        var env = new FakeEnvironment
        {
            HomeDirectory = OperatingSystem.IsWindows() ? @"C:\Users\me" : "/home/me",
            CurrentDirectory = OperatingSystem.IsWindows() ? @"C:\work" : "/work",
            IsWindows = OperatingSystem.IsWindows(),
        };
        return new LocalDirectorySkillSourceFetcher(new DefaultPathResolver(env), NullLogger<LocalDirectorySkillSourceFetcher>.Instance);
    }
}
