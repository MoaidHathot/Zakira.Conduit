using Microsoft.Extensions.DependencyInjection;
using Zakira.Conduit.DependencyInjection;
using Zakira.Conduit.IntegrationTests.TestHelpers;
using Zakira.Conduit.Manifest;
using Zakira.Conduit.Sources;
using Zakira.Conduit.Sources.GitHub;

namespace Zakira.Conduit.IntegrationTests;

public sealed class GitHubSkillSourceFetcherTests
{
    private static readonly FetchContext DefaultContext = new(Path.Combine(Path.GetTempPath(), "conduit-itests-ctx.json"));

    [Fact]
    public async Task End_to_end_fetch_extracts_repository_content_locally()
    {
        await using var server = new MockGitHubServer();
        var payload = ZipballPayload.Build("anthropics-skills-abc", new Dictionary<string, string>
        {
            ["README.md"] = "hello",
            ["skills/code-review/SKILL.md"] = "# review",
            ["skills/code-review/checks.json"] = "[]",
            ["skills/other/SKILL.md"] = "# other",
        });
        server.MapZipball("anthropics", "skills", gitRef: "main", payload);

        var fetcher = BuildFetcher(server);
        var source = new GitHubSkillSource { Owner = "anthropics", Repo = "skills", Path = "skills/code-review", Branch = "main" };

        await using var fetched = await fetcher.FetchAsync(source, DefaultContext);

        fetched.ContentDirectory.Should().NotBeNullOrEmpty();
        Directory.Exists(fetched.ContentDirectory).Should().BeTrue();
        File.Exists(Path.Combine(fetched.ContentDirectory, "SKILL.md")).Should().BeTrue();
        File.Exists(Path.Combine(fetched.ContentDirectory, "checks.json")).Should().BeTrue();
        Directory.Exists(Path.Combine(fetched.ContentDirectory, "other")).Should().BeFalse();
        fetched.ResolvedRef.Should().Be("main");
    }

    [Fact]
    public async Task Disposing_fetched_source_removes_temp_directory()
    {
        await using var server = new MockGitHubServer();
        var payload = ZipballPayload.Build("o-r-c", new Dictionary<string, string> { ["a.txt"] = "x" });
        server.MapZipball("o", "r", gitRef: null, payload);

        var fetcher = BuildFetcher(server);
        var source = new GitHubSkillSource { Owner = "o", Repo = "r" };

        string contentDir;
        await using (var fetched = await fetcher.FetchAsync(source, DefaultContext))
        {
            contentDir = fetched.ContentDirectory;
            Directory.Exists(contentDir).Should().BeTrue();
        }

        // Walk up until we hit the conduit fetch root and verify it's gone.
        var parent = Directory.GetParent(contentDir)?.FullName;
        if (parent is not null)
        {
            Directory.Exists(parent).Should().BeFalse("the fetcher should clean up its temp root on dispose");
        }
    }

    [Fact]
    public async Task Subpath_that_does_not_exist_throws()
    {
        await using var server = new MockGitHubServer();
        var payload = ZipballPayload.Build("o-r-c", new Dictionary<string, string> { ["foo/bar.txt"] = "y" });
        server.MapZipball("o", "r", gitRef: null, payload);

        var fetcher = BuildFetcher(server);
        var source = new GitHubSkillSource { Owner = "o", Repo = "r", Path = "does/not/exist" };

        var act = () => fetcher.FetchAsync(source, DefaultContext);
        await act.Should().ThrowAsync<GitHubDownloadException>();
    }

    [Fact]
    public async Task Uses_commit_ref_when_set()
    {
        await using var server = new MockGitHubServer();
        var payload = ZipballPayload.Build("o-r-abc", new Dictionary<string, string> { ["a.txt"] = "x" });
        server.MapZipball("o", "r", gitRef: "abc123", payload);

        var fetcher = BuildFetcher(server);
        var source = new GitHubSkillSource { Owner = "o", Repo = "r", Commit = "abc123" };

        await using var fetched = await fetcher.FetchAsync(source, DefaultContext);

        fetched.ResolvedRef.Should().Be("abc123");
        server.Requests.Should().ContainSingle(r => r.Path.EndsWith("/zipball/abc123", StringComparison.Ordinal));
    }

    private static ISkillSourceFetcher BuildFetcher(MockGitHubServer server)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddConduitCore(opts =>
        {
            opts.BaseAddress = server.BaseAddress;
        });

        var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<ISkillSourceFetcherRegistry>();
        return registry.GetFetcher(new GitHubSkillSource { Owner = "x", Repo = "y" });
    }
}
