using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Zakira.Conduit.DependencyInjection;
using Zakira.Conduit.IntegrationTests.TestHelpers;
using Zakira.Conduit.Manifest;
using Zakira.Conduit.Sources;
using Zakira.Conduit.Sources.GitHub;

namespace Zakira.Conduit.IntegrationTests;

public sealed class GitHubArchiveDownloaderTests
{
    [Fact]
    public async Task Downloads_zipball_for_default_ref()
    {
        await using var server = new MockGitHubServer();
        var payload = ZipballPayload.Build("owner-repo-abc1234", new Dictionary<string, string>
        {
            ["README.md"] = "hi",
        });
        server.MapZipball("owner", "repo", gitRef: null, payload);

        var downloader = BuildDownloader(server);
        using var ms = new MemoryStream();

        await downloader.DownloadAsync("owner", "repo", null, ms);

        ms.Length.Should().Be(payload.LongLength);
        server.Requests.Should().ContainSingle();
        server.Requests[0].Path.Should().Be("/repos/owner/repo/zipball");
        server.Requests[0].UserAgentHeader.Should().Be("Zakira.Conduit");
        server.Requests[0].AuthorizationHeader.Should().BeNull();
    }

    [Fact]
    public async Task Includes_ref_in_url_when_specified()
    {
        await using var server = new MockGitHubServer();
        var payload = ZipballPayload.Build("owner-repo-deadbeef", new Dictionary<string, string> { ["x"] = "y" });
        server.MapZipball("owner", "repo", gitRef: "v1.2.3", payload);

        var downloader = BuildDownloader(server);
        using var ms = new MemoryStream();

        await downloader.DownloadAsync("owner", "repo", "v1.2.3", ms);

        server.Requests[0].Path.Should().Be("/repos/owner/repo/zipball/v1.2.3");
    }

    [Fact]
    public async Task Sends_bearer_token_when_configured()
    {
        await using var server = new MockGitHubServer();
        var payload = ZipballPayload.Build("o-r-c", new Dictionary<string, string> { ["a"] = "b" });
        server.MapZipball("o", "r", gitRef: null, payload);

        var downloader = BuildDownloader(server, token: "fake-token-xyz");
        using var ms = new MemoryStream();

        await downloader.DownloadAsync("o", "r", null, ms);

        server.Requests[0].AuthorizationHeader.Should().Be("Bearer fake-token-xyz");
    }

    [Fact]
    public async Task Throws_with_status_code_on_404()
    {
        await using var server = new MockGitHubServer();
        server.MapZipball("o", "r", gitRef: "missing", payload: [], status: HttpStatusCode.NotFound);

        var downloader = BuildDownloader(server);
        using var ms = new MemoryStream();

        var act = () => downloader.DownloadAsync("o", "r", "missing", ms);
        var ex = await act.Should().ThrowAsync<GitHubDownloadException>();
        ex.Which.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Throws_when_response_exceeds_max_size()
    {
        await using var server = new MockGitHubServer();
        var payload = new byte[1024];
        server.MapZipball("o", "r", gitRef: null, payload);

        var downloader = BuildDownloader(server, maxSizeBytes: 100);
        using var ms = new MemoryStream();

        var act = () => downloader.DownloadAsync("o", "r", null, ms);
        await act.Should().ThrowAsync<GitHubDownloadException>();
    }

    private static IGitHubArchiveDownloader BuildDownloader(MockGitHubServer server, string? token = null, long maxSizeBytes = 256 * 1024 * 1024)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddGitHubSkillSource(opts =>
        {
            opts.BaseAddress = server.BaseAddress;
            opts.Token = token;
            opts.MaxArchiveSizeBytes = maxSizeBytes;
        });
        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IGitHubArchiveDownloader>();
    }
}
