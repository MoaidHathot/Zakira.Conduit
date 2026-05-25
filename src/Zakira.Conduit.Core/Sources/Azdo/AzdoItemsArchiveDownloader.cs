using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Zakira.Conduit.Manifest;
using Zakira.Conduit.Sources.Azdo.Credentials;

namespace Zakira.Conduit.Sources.Azdo;

/// <summary>
///     Default <see cref="IAzdoItemsArchiveDownloader"/> using the Azure DevOps
///     Items REST endpoint with <c>$format=zip&amp;recursionLevel=full</c>.
/// </summary>
public sealed class AzdoItemsArchiveDownloader : IAzdoItemsArchiveDownloader
{
    private const int BufferSize = 81920;

    private readonly HttpClient _httpClient;
    private readonly AzdoFetcherOptions _options;
    private readonly ChainedAzdoCredentialProvider _credentials;
    private readonly ILogger<AzdoItemsArchiveDownloader> _logger;

    public AzdoItemsArchiveDownloader(
        HttpClient httpClient,
        IOptions<AzdoFetcherOptions> options,
        ChainedAzdoCredentialProvider credentials,
        ILogger<AzdoItemsArchiveDownloader> logger)
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
    public async Task DownloadAsync(
        AzdoSkillSource source,
        string commitSha,
        string? scopePath,
        Stream destinationStream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(commitSha);
        ArgumentNullException.ThrowIfNull(destinationStream);

        var components = source.ResolvedComponents;
        var normalizedScope = NormalizeScope(scopePath);

        var query =
            $"path={Uri.EscapeDataString(normalizedScope ?? "/")}" +
            $"&recursionLevel=full" +
            $"&$format=zip" +
            $"&download=true" +
            $"&versionDescriptor.version={Uri.EscapeDataString(commitSha)}" +
            $"&versionDescriptor.versionType=commit" +
            $"&api-version={_options.ApiVersion}";

        if (!string.IsNullOrEmpty(normalizedScope) && normalizedScope != "/")
        {
            query += $"&scopePath={Uri.EscapeDataString(normalizedScope)}";
        }

        var path = $"{Uri.EscapeDataString(components.Organization)}/{Uri.EscapeDataString(components.Project)}/_apis/git/repositories/{Uri.EscapeDataString(components.Repo)}/items?{query}";
        var requestUri = new Uri(components.BaseUrl, path);

        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.UserAgent.ParseAdd(_options.UserAgent);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/zip"));
        request.Headers.Authorization = await _credentials.TryGetAsync(source, cancellationToken).ConfigureAwait(false);

        _logger.LogDebug("GET {Uri}", requestUri);

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var body = await SafeReadBodyAsync(response, cancellationToken).ConfigureAwait(false);
            throw new AzdoApiException(
                $"Azure DevOps returned {(int)response.StatusCode} {response.StatusCode} when fetching items for '{source.Slug}' (commit={commitSha}, scope='{normalizedScope ?? "/"}').{(string.IsNullOrEmpty(body) ? string.Empty : " Body: " + body)}",
                response.StatusCode);
        }

        if (response.Content.Headers.ContentLength is { } length && length > _options.MaxArchiveSizeBytes)
        {
            throw new AzdoApiException(
                $"Archive size {length:N0} bytes exceeds the configured maximum of {_options.MaxArchiveSizeBytes:N0} bytes.",
                response.StatusCode);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        var buffer = new byte[BufferSize];
        long totalRead = 0;
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            totalRead += read;
            if (totalRead > _options.MaxArchiveSizeBytes)
            {
                throw new AzdoApiException(
                    $"Archive exceeded the configured maximum of {_options.MaxArchiveSizeBytes:N0} bytes while streaming.",
                    response.StatusCode);
            }

            await destinationStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
    }

    private static string? NormalizeScope(string? scopePath)
    {
        if (string.IsNullOrWhiteSpace(scopePath))
        {
            return null;
        }

        var normalized = scopePath.Replace('\\', '/').Trim();
        if (!normalized.StartsWith('/'))
        {
            normalized = "/" + normalized;
        }

        return normalized.TrimEnd('/');
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
