using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Zakira.Conduit.Sources.GitHub;

/// <summary>
///     Default <see cref="IGitHubRefResolver"/> using GitHub's
///     <c>GET /repos/{owner}/{repo}/commits/{ref}</c> endpoint, which returns
///     the commit SHA at the tip of a branch / tag / commit ref.
/// </summary>
public sealed class GitHubRefResolver : IGitHubRefResolver
{
    private readonly HttpClient _httpClient;
    private readonly GitHubFetcherOptions _options;
    private readonly ILogger<GitHubRefResolver> _logger;

    public GitHubRefResolver(HttpClient httpClient, IOptions<GitHubFetcherOptions> options, ILogger<GitHubRefResolver> logger)
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
    public async Task<string> ResolveAsync(string owner, string repo, string gitRef, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(repo);
        ArgumentException.ThrowIfNullOrWhiteSpace(gitRef);

        var requestUri = $"repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}/commits/{Uri.EscapeDataString(gitRef)}";

        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.UserAgent.ParseAdd(_options.UserAgent);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

        if (!string.IsNullOrWhiteSpace(_options.Token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.Token);
        }

        _logger.LogDebug("GET {Uri}", requestUri);

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new GitHubDownloadException(
                $"GitHub returned {(int)response.StatusCode} {response.StatusCode} when resolving ref '{gitRef}' for '{owner}/{repo}'.{(string.IsNullOrEmpty(body) ? string.Empty : " Body: " + body)}",
                response.StatusCode);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!doc.RootElement.TryGetProperty("sha", out var shaElement) || shaElement.ValueKind != JsonValueKind.String)
        {
            throw new GitHubDownloadException(
                $"GitHub response for '{owner}/{repo}@{gitRef}' did not include a 'sha' field.",
                HttpStatusCode.OK);
        }

        var sha = shaElement.GetString();
        if (string.IsNullOrEmpty(sha))
        {
            throw new GitHubDownloadException(
                $"GitHub returned an empty SHA for '{owner}/{repo}@{gitRef}'.",
                HttpStatusCode.OK);
        }

        return sha;
    }
}
