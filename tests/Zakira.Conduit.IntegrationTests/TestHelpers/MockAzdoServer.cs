using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Zakira.Conduit.IntegrationTests.TestHelpers;

/// <summary>
///     In-process HTTP server emulating the slice of the Azure DevOps REST API
///     used by the AzDO fetcher. Routes are registered per test.
/// </summary>
internal sealed class MockAzdoServer : IAsyncDisposable
{
    private readonly HttpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loop;
    private readonly List<(string PathPrefix, Func<HttpListenerContext, Task> Handler)> _routes = [];

    public Uri BaseAddress { get; }

    /// <summary>All requests the server has observed, in order.</summary>
    public List<RecordedAzdoRequest> Requests { get; } = [];

    public MockAzdoServer()
    {
        var port = GetFreePort();
        BaseAddress = new Uri($"http://127.0.0.1:{port}/");
        _listener = new HttpListener();
        _listener.Prefixes.Add(BaseAddress.AbsoluteUri);
        _listener.Start();
        _loop = Task.Run(ListenLoopAsync);
    }

    public MockAzdoServer Map(string pathStartsWith, Func<HttpListenerContext, Task> handler)
    {
        _routes.Add((pathStartsWith, handler));
        return this;
    }

    public MockAzdoServer MapBranchSha(string org, string project, string repo, string branch, string commitSha)
    {
        var prefix = $"/{org}/{project}/_apis/git/repositories/{repo}/stats/branches";
        return Map(prefix, async ctx =>
        {
            ctx.Response.StatusCode = (int)HttpStatusCode.OK;
            ctx.Response.ContentType = "application/json";
            var body = Encoding.UTF8.GetBytes("{\"name\":\"" + branch + "\",\"commit\":{\"commitId\":\"" + commitSha + "\"}}");
            ctx.Response.ContentLength64 = body.LongLength;
            await ctx.Response.OutputStream.WriteAsync(body).ConfigureAwait(false);
            ctx.Response.Close();
        });
    }

    public MockAzdoServer MapItemsZip(string org, string project, string repo, byte[] zip)
    {
        var prefix = $"/{org}/{project}/_apis/git/repositories/{repo}/items";
        return Map(prefix, async ctx =>
        {
            ctx.Response.StatusCode = (int)HttpStatusCode.OK;
            ctx.Response.ContentType = "application/zip";
            ctx.Response.ContentLength64 = zip.LongLength;
            await ctx.Response.OutputStream.WriteAsync(zip).ConfigureAwait(false);
            ctx.Response.Close();
        });
    }

    private async Task ListenLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (HttpListenerException) { break; }
            catch (ObjectDisposedException) { break; }

            _ = Task.Run(async () =>
            {
                var path = context.Request.Url?.AbsolutePath ?? string.Empty;
                var query = context.Request.Url?.Query ?? string.Empty;
                Requests.Add(new RecordedAzdoRequest(
                    Method: context.Request.HttpMethod,
                    Path: path,
                    Query: query,
                    AuthorizationHeader: context.Request.Headers["Authorization"],
                    UserAgent: context.Request.UserAgent));

                foreach (var (prefix, handler) in _routes)
                {
                    if (path.StartsWith(prefix, StringComparison.Ordinal))
                    {
                        try
                        {
                            await handler(context).ConfigureAwait(false);
                        }
                        catch
                        {
                            try { context.Response.StatusCode = 500; context.Response.Close(); } catch { }
                        }

                        return;
                    }
                }

                context.Response.StatusCode = 404;
                context.Response.Close();
            });
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try { _listener.Stop(); _listener.Close(); } catch { }
        try { await _loop.ConfigureAwait(false); } catch { }
    }

    private static int GetFreePort()
    {
        using var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }
}

internal sealed record RecordedAzdoRequest(string Method, string Path, string Query, string? AuthorizationHeader, string? UserAgent);

/// <summary>
///     Helpers to build AzDO-style zip payloads in memory. Unlike GitHub
///     zipballs there is no <c>owner-repo-sha/</c> wrapper folder &mdash;
///     entries are rooted at the requested <c>scopePath</c>'s basename.
/// </summary>
internal static class AzdoZipPayload
{
    public static byte[] Build(IReadOnlyDictionary<string, string> files)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (rel, content) in files)
            {
                var normalized = rel.Replace('\\', '/').TrimStart('/');
                var e = zip.CreateEntry(normalized);
                using var s = e.Open();
                using var sw = new StreamWriter(s);
                sw.Write(content);
            }
        }

        return ms.ToArray();
    }
}
