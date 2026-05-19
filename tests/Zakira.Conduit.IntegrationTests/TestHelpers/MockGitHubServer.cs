using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Zakira.Conduit.IntegrationTests.TestHelpers;

/// <summary>
///     An in-process HTTP server that emulates the slice of the GitHub REST API
///     used by <c>GitHubArchiveDownloader</c>. Routes are registered per test.
/// </summary>
internal sealed class MockGitHubServer : IAsyncDisposable
{
    private readonly HttpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loop;
    private readonly List<(string PathPattern, Func<HttpListenerContext, Task> Handler)> _routes = [];

    public Uri BaseAddress { get; }

    /// <summary>All requests the server has observed, in order.</summary>
    public List<RecordedRequest> Requests { get; } = [];

    public MockGitHubServer()
    {
        var port = GetFreePort();
        BaseAddress = new Uri($"http://127.0.0.1:{port}/");
        _listener = new HttpListener();
        _listener.Prefixes.Add(BaseAddress.AbsoluteUri);
        _listener.Start();
        _loop = Task.Run(ListenLoopAsync);
    }

    public MockGitHubServer Map(string pathStartsWith, Func<HttpListenerContext, Task> handler)
    {
        _routes.Add((pathStartsWith, handler));
        return this;
    }

    /// <summary>Convenience helper: serve a fixed byte payload with the given status code.</summary>
    public MockGitHubServer MapZipball(string owner, string repo, string? gitRef, byte[] payload, HttpStatusCode status = HttpStatusCode.OK)
    {
        var path = "/repos/" + owner + "/" + repo + "/zipball" + (gitRef is null ? string.Empty : "/" + Uri.EscapeDataString(gitRef));
        return Map(path, async ctx =>
        {
            ctx.Response.StatusCode = (int)status;
            if (status == HttpStatusCode.OK)
            {
                ctx.Response.ContentType = "application/zip";
                ctx.Response.ContentLength64 = payload.LongLength;
                await ctx.Response.OutputStream.WriteAsync(payload).ConfigureAwait(false);
            }
            else
            {
                var body = Encoding.UTF8.GetBytes("{\"message\":\"mock error\"}");
                ctx.Response.ContentType = "application/json";
                ctx.Response.ContentLength64 = body.LongLength;
                await ctx.Response.OutputStream.WriteAsync(body).ConfigureAwait(false);
            }

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
            catch (HttpListenerException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }

            _ = Task.Run(async () =>
            {
                var path = context.Request.Url?.AbsolutePath ?? string.Empty;
                Requests.Add(new RecordedRequest(
                    Method: context.Request.HttpMethod,
                    Path: path,
                    AuthorizationHeader: context.Request.Headers["Authorization"],
                    UserAgentHeader: context.Request.UserAgent,
                    AcceptHeader: context.Request.Headers["Accept"]));

                foreach (var (pattern, handler) in _routes)
                {
                    if (path.StartsWith(pattern, StringComparison.Ordinal))
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
        try
        {
            _listener.Stop();
            _listener.Close();
        }
        catch
        {
            // best-effort
        }

        try
        {
            await _loop.ConfigureAwait(false);
        }
        catch
        {
            // expected on shutdown
        }
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

internal sealed record RecordedRequest(string Method, string Path, string? AuthorizationHeader, string? UserAgentHeader, string? AcceptHeader);

/// <summary>
///     Helpers to build a GitHub-style zipball in memory.
/// </summary>
internal static class ZipballPayload
{
    public static byte[] Build(string topFolder, IReadOnlyDictionary<string, string> files)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            zip.CreateEntry(topFolder + "/");
            foreach (var (rel, content) in files)
            {
                var normalized = rel.Replace('\\', '/').TrimStart('/');
                var e = zip.CreateEntry($"{topFolder}/{normalized}");
                using var s = e.Open();
                using var sw = new StreamWriter(s);
                sw.Write(content);
            }
        }

        return ms.ToArray();
    }
}
