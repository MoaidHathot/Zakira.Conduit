using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Zakira.Conduit.DependencyInjection;
using Zakira.Conduit.IntegrationTests.TestHelpers;
using Zakira.Conduit.Sources.GitHub;

namespace Zakira.Conduit.IntegrationTests;

public sealed class GitHubRefResolverTests
{
    [Fact]
    public async Task Returns_sha_from_commits_endpoint_response()
    {
        await using var server = new MockGitHubServer();
        const string sha = "1a2b3c4d5e6f7890abcdef1234567890abcdef12";

        server.Map("/repos/acme/skills/commits/main", async ctx =>
        {
            ctx.Response.StatusCode = (int)HttpStatusCode.OK;
            ctx.Response.ContentType = "application/json";
            var body = Encoding.UTF8.GetBytes($"{{\"sha\":\"{sha}\",\"commit\":{{\"message\":\"x\"}}}}");
            ctx.Response.ContentLength64 = body.LongLength;
            await ctx.Response.OutputStream.WriteAsync(body).ConfigureAwait(false);
            ctx.Response.Close();
        });

        var resolver = BuildResolver(server);
        var resolved = await resolver.ResolveAsync("acme", "skills", "main");

        resolved.Should().Be(sha);
    }

    [Fact]
    public async Task Throws_on_404()
    {
        await using var server = new MockGitHubServer();
        server.Map("/repos/acme/missing/commits/main", ctx =>
        {
            ctx.Response.StatusCode = 404;
            ctx.Response.Close();
            return Task.CompletedTask;
        });

        var resolver = BuildResolver(server);
        var act = () => resolver.ResolveAsync("acme", "missing", "main");

        var ex = await act.Should().ThrowAsync<GitHubDownloadException>();
        ex.Which.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Throws_when_response_has_no_sha()
    {
        await using var server = new MockGitHubServer();
        server.Map("/repos/acme/skills/commits/weird", async ctx =>
        {
            ctx.Response.StatusCode = (int)HttpStatusCode.OK;
            ctx.Response.ContentType = "application/json";
            var body = Encoding.UTF8.GetBytes("{\"commit\":{\"message\":\"no sha here\"}}");
            ctx.Response.ContentLength64 = body.LongLength;
            await ctx.Response.OutputStream.WriteAsync(body).ConfigureAwait(false);
            ctx.Response.Close();
        });

        var resolver = BuildResolver(server);
        var act = () => resolver.ResolveAsync("acme", "skills", "weird");

        await act.Should().ThrowAsync<GitHubDownloadException>();
    }

    private static IGitHubRefResolver BuildResolver(MockGitHubServer server)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddConduitCore(opts => opts.BaseAddress = server.BaseAddress);
        return services.BuildServiceProvider().GetRequiredService<IGitHubRefResolver>();
    }
}
