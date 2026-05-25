using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Zakira.Conduit.Manifest;
using Zakira.Conduit.Sources.Azdo.Credentials;

namespace Zakira.Conduit.Sources.Azdo;

/// <summary>
///     Default <see cref="IAzdoRefResolver"/>.
///     <list type="bullet">
///         <item><description>Branches: <c>GET /{org}/{project}/_apis/git/repositories/{repo}/stats/branches?name={branch}</c> (returns <c>commit.commitId</c>).</description></item>
///         <item><description>Tags: <c>GET /{org}/{project}/_apis/git/repositories/{repo}/refs?filter=tags/{tag}</c> (returns the first matching <c>objectId</c>).</description></item>
///         <item><description>Commits: returned verbatim (no network round-trip needed).</description></item>
///     </list>
/// </summary>
public sealed class AzdoRefResolver : IAzdoRefResolver
{
    private readonly HttpClient _httpClient;
    private readonly AzdoFetcherOptions _options;
    private readonly ChainedAzdoCredentialProvider _credentials;
    private readonly ILogger<AzdoRefResolver> _logger;

    public AzdoRefResolver(
        HttpClient httpClient,
        IOptions<AzdoFetcherOptions> options,
        ChainedAzdoCredentialProvider credentials,
        ILogger<AzdoRefResolver> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentNullException.ThrowIfNull(logger);

        _httpClient = httpClient;
        _options = options.Value;
        _credentials = credentials;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string> ResolveAsync(AzdoSkillSource source, string refValue, string refKind, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(refValue);
        ArgumentException.ThrowIfNullOrWhiteSpace(refKind);

        if (string.Equals(refKind, "commit", StringComparison.OrdinalIgnoreCase))
        {
            return refValue;
        }

        var components = source.ResolvedComponents;
        var baseUrl = components.BaseUrl;
        var (path, expectShaProperty) = refKind.ToLowerInvariant() switch
        {
            "branch" => (
                $"{Uri.EscapeDataString(components.Organization)}/{Uri.EscapeDataString(components.Project)}/_apis/git/repositories/{Uri.EscapeDataString(components.Repo)}/stats/branches?name={Uri.EscapeDataString(refValue)}&api-version={_options.ApiVersion}",
                "branch"),
            "tag" => (
                $"{Uri.EscapeDataString(components.Organization)}/{Uri.EscapeDataString(components.Project)}/_apis/git/repositories/{Uri.EscapeDataString(components.Repo)}/refs?filter=tags/{Uri.EscapeDataString(refValue)}&api-version={_options.ApiVersion}",
                "tag"),
            _ => throw new ArgumentException($"Unknown ref kind '{refKind}'. Expected 'branch', 'tag' or 'commit'.", nameof(refKind)),
        };

        var requestUri = new Uri(baseUrl, path);

        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.UserAgent.ParseAdd(_options.UserAgent);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Authorization = await _credentials.TryGetAsync(source, cancellationToken).ConfigureAwait(false);

        _logger.LogDebug("GET {Uri}", requestUri);

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var body = await SafeReadBodyAsync(response, cancellationToken).ConfigureAwait(false);
            throw new AzdoApiException(
                $"Azure DevOps returned {(int)response.StatusCode} {response.StatusCode} when resolving {refKind} '{refValue}' for '{source.Slug}'.{(string.IsNullOrEmpty(body) ? string.Empty : " Body: " + body)}",
                response.StatusCode);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        if (expectShaProperty == "branch")
        {
            if (doc.RootElement.TryGetProperty("commit", out var commit) &&
                commit.TryGetProperty("commitId", out var sha) &&
                sha.ValueKind == JsonValueKind.String)
            {
                var s = sha.GetString();
                if (!string.IsNullOrEmpty(s))
                {
                    return s;
                }
            }

            throw new AzdoApiException(
                $"Azure DevOps response for branch '{refValue}' on '{source.Slug}' did not include commit.commitId.",
                HttpStatusCode.OK);
        }

        // tag: refs?filter=tags/{tag} returns { count, value: [{ objectId, peeledObjectId? }] }
        if (doc.RootElement.TryGetProperty("value", out var values) && values.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in values.EnumerateArray())
            {
                // Prefer peeledObjectId (the commit a tag points at) if present;
                // fall back to objectId (which is the commit for lightweight tags).
                if (entry.TryGetProperty("peeledObjectId", out var peeled) && peeled.ValueKind == JsonValueKind.String)
                {
                    var s = peeled.GetString();
                    if (!string.IsNullOrEmpty(s))
                    {
                        return s;
                    }
                }

                if (entry.TryGetProperty("objectId", out var obj) && obj.ValueKind == JsonValueKind.String)
                {
                    var s = obj.GetString();
                    if (!string.IsNullOrEmpty(s))
                    {
                        return s;
                    }
                }
            }
        }

        throw new AzdoApiException(
            $"Azure DevOps response for tag '{refValue}' on '{source.Slug}' did not include any matching ref objectId.",
            HttpStatusCode.OK);
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
