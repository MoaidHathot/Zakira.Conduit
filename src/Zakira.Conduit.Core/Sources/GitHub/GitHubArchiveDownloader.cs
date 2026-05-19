using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Zakira.Conduit.Sources.GitHub;

/// <summary>
///     <see cref="IGitHubArchiveDownloader"/> using GitHub's REST zipball endpoint.
/// </summary>
public sealed class GitHubArchiveDownloader : IGitHubArchiveDownloader
{
    private const int BufferSize = 81920;

    private readonly HttpClient _httpClient;
    private readonly GitHubFetcherOptions _options;
    private readonly ILogger<GitHubArchiveDownloader> _logger;

    public GitHubArchiveDownloader(HttpClient httpClient, IOptions<GitHubFetcherOptions> options, ILogger<GitHubArchiveDownloader> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;

        if (_httpClient.BaseAddress is null)
        {
            _httpClient.BaseAddress = _options.BaseAddress;
        }
    }

    /// <inheritdoc />
    public async Task DownloadAsync(string owner, string repo, string? gitRef, Stream destinationStream, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(repo);
        ArgumentNullException.ThrowIfNull(destinationStream);

        var refSegment = string.IsNullOrWhiteSpace(gitRef) ? string.Empty : "/" + Uri.EscapeDataString(gitRef);
        var requestUri = $"repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}/zipball{refSegment}";

        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.UserAgent.ParseAdd(_options.UserAgent);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

        if (!string.IsNullOrWhiteSpace(_options.Token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.Token);
        }

        _logger.LogDebug("GET {Uri}", requestUri);

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var body = await SafeReadBodyAsync(response, cancellationToken).ConfigureAwait(false);
            throw new GitHubDownloadException(
                $"GitHub returned {(int)response.StatusCode} {response.StatusCode} for '{owner}/{repo}' (ref='{gitRef ?? "<default>"}').{(string.IsNullOrEmpty(body) ? string.Empty : " Body: " + body)}",
                response.StatusCode);
        }

        if (response.Content.Headers.ContentLength is { } length && length > _options.MaxArchiveSizeBytes)
        {
            throw new GitHubDownloadException(
                $"Archive size {length:N0} bytes exceeds the configured maximum of {_options.MaxArchiveSizeBytes:N0} bytes.",
                response.StatusCode);
        }

        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        var buffer = new byte[BufferSize];
        long totalRead = 0;
        int read;
        while ((read = await responseStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            totalRead += read;
            if (totalRead > _options.MaxArchiveSizeBytes)
            {
                throw new GitHubDownloadException(
                    $"Archive exceeded the configured maximum of {_options.MaxArchiveSizeBytes:N0} bytes while streaming.",
                    response.StatusCode);
            }

            await destinationStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<string> SafeReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return string.Empty;
        }
    }
}

/// <summary>
///     Raised when GitHub returns a non-success status code or otherwise
///     refuses a zipball download.
/// </summary>
public sealed class GitHubDownloadException : Exception
{
    /// <summary>The HTTP status code returned by GitHub.</summary>
    public HttpStatusCode StatusCode { get; }

    public GitHubDownloadException(string message, HttpStatusCode statusCode, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}
