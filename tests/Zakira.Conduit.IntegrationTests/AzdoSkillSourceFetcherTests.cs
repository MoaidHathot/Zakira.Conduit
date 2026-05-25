using Microsoft.Extensions.DependencyInjection;
using Zakira.Conduit.DependencyInjection;
using Zakira.Conduit.IntegrationTests.TestHelpers;
using Zakira.Conduit.Manifest;
using Zakira.Conduit.Sources;
using Zakira.Conduit.Sources.Azdo;

namespace Zakira.Conduit.IntegrationTests;

public sealed class AzdoSkillSourceFetcherTests
{
    private static readonly FetchContext DefaultContext = new(Path.Combine(Path.GetTempPath(), "conduit-itests-azdo-ctx.json"));

    [Fact]
    public async Task End_to_end_fetch_extracts_repository_content_locally()
    {
        await using var server = new MockAzdoServer();
        server.MapBranchSha("contoso", "Conduit", "agent-skills", "main", "deadbeefcafef00d0000000000000000aabbccdd");
        var zip = AzdoZipPayload.Build(new Dictionary<string, string>
        {
            ["code-review/SKILL.md"] = "# review",
            ["code-review/checks.json"] = "[]",
        });
        server.MapItemsZip("contoso", "Conduit", "agent-skills", zip);

        var fetcher = BuildFetcher(server);
        var source = new AzdoSkillSource
        {
            Organization = "contoso",
            Project = "Conduit",
            Repo = "agent-skills",
            BaseUrl = server.BaseAddress.AbsoluteUri,
            Branch = "main",
            Path = "code-review",
        };

        await using var fetched = await fetcher.FetchAsync(source, DefaultContext);

        fetched.Contents.Should().HaveCount(1);
        var dir = fetched.Contents[0].ContentDirectory;
        File.Exists(Path.Combine(dir, "SKILL.md")).Should().BeTrue();
        File.Exists(Path.Combine(dir, "checks.json")).Should().BeTrue();
        fetched.ResolvedRef.Should().Be("deadbeefcafef00d0000000000000000aabbccdd");
        fetched.Etag.Should().Be(fetched.ResolvedRef);
    }

    [Fact]
    public async Task Multi_path_fetch_makes_one_request_per_path()
    {
        await using var server = new MockAzdoServer();
        server.MapBranchSha("contoso", "Conduit", "agent-skills", "main", "abc1230000000000000000000000000000000000");

        var zip = AzdoZipPayload.Build(new Dictionary<string, string>
        {
            ["x/SKILL.md"] = "x",
        });
        server.MapItemsZip("contoso", "Conduit", "agent-skills", zip);

        var fetcher = BuildFetcher(server);
        var source = new AzdoSkillSource
        {
            Url = new Uri(server.BaseAddress, "contoso/Conduit/_git/agent-skills").AbsoluteUri,
            Branch = "main",
            Paths = new PathSpec[] { new("a"), new("b"), new("c") },
        };

        // url-form points at the mock server's host, so the parser will derive baseUrl from it.

        await using var _ = await fetcher.FetchAsync(source, DefaultContext);

        var itemsRequests = server.Requests.Count(r => r.Path.EndsWith("/items", StringComparison.Ordinal));
        itemsRequests.Should().Be(3, because: "one items request per scopePath");
    }

    [Fact]
    public async Task Commit_pin_skips_branch_resolution()
    {
        await using var server = new MockAzdoServer();
        var zip = AzdoZipPayload.Build(new Dictionary<string, string> { ["a.txt"] = "x" });
        server.MapItemsZip("contoso", "Conduit", "agent-skills", zip);

        var fetcher = BuildFetcher(server);
        var source = new AzdoSkillSource
        {
            Organization = "contoso",
            Project = "Conduit",
            Repo = "agent-skills",
            BaseUrl = server.BaseAddress.AbsoluteUri,
            Commit = "abc1230000000000000000000000000000000000",
        };

        await using var _ = await fetcher.FetchAsync(source, DefaultContext);

        server.Requests.Should().NotContain(r => r.Path.Contains("/stats/branches", StringComparison.Ordinal));
    }

    [Fact]
    public async Task NotModified_when_resolved_sha_matches_previous_etag()
    {
        await using var server = new MockAzdoServer();
        const string sha = "abc1230000000000000000000000000000000000";
        server.MapBranchSha("contoso", "Conduit", "agent-skills", "main", sha);
        server.MapItemsZip("contoso", "Conduit", "agent-skills", AzdoZipPayload.Build(new Dictionary<string, string> { ["a.txt"] = "x" }));

        var fetcher = BuildFetcher(server);
        var source = new AzdoSkillSource
        {
            Organization = "contoso",
            Project = "Conduit",
            Repo = "agent-skills",
            BaseUrl = server.BaseAddress.AbsoluteUri,
            Branch = "main",
        };

        var ctx = new FetchContext(DefaultContext.ManifestPath) { PreviousEtag = sha };
        await using var fetched = await fetcher.FetchAsync(source, ctx);

        fetched.NotModified.Should().BeTrue();
        server.Requests.Should().NotContain(r => r.Path.EndsWith("/items", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Authorization_header_uses_basic_when_env_token_present()
    {
        var prevToken = Environment.GetEnvironmentVariable("CONDUIT_AZDO_TOKEN");
        Environment.SetEnvironmentVariable("CONDUIT_AZDO_TOKEN", "test-pat");
        try
        {
            await using var server = new MockAzdoServer();
            server.MapBranchSha("contoso", "Conduit", "agent-skills", "main", "abc1230000000000000000000000000000000000");
            server.MapItemsZip("contoso", "Conduit", "agent-skills", AzdoZipPayload.Build(new Dictionary<string, string> { ["a.txt"] = "x" }));

            var fetcher = BuildFetcher(server);
            var source = new AzdoSkillSource
            {
                Organization = "contoso",
                Project = "Conduit",
                Repo = "agent-skills",
                BaseUrl = server.BaseAddress.AbsoluteUri,
                Branch = "main",
                Auth = new[] { "env" },
            };

            await using var _ = await fetcher.FetchAsync(source, DefaultContext);

            server.Requests.Should().NotBeEmpty();
            server.Requests.Should().OnlyContain(r => r.AuthorizationHeader!.StartsWith("Basic ", StringComparison.Ordinal));
        }
        finally
        {
            Environment.SetEnvironmentVariable("CONDUIT_AZDO_TOKEN", prevToken);
        }
    }

    private static ISkillSourceFetcher BuildFetcher(MockAzdoServer server)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddConduitCore();
        services.Configure<AzdoFetcherOptions>(opts =>
        {
            opts.DefaultBaseAddress = server.BaseAddress;
        });

        var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<ISkillSourceFetcherRegistry>();
        return registry.GetFetcher(new AzdoSkillSource { Organization = "x", Project = "y", Repo = "z" });
    }
}
