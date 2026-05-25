using System.Collections.Concurrent;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Zakira.Conduit.Manifest;

namespace Zakira.Conduit.Sources.Azdo.Credentials;

/// <summary>
///     Acquires a bearer token by shelling out to the <c>az</c> CLI.
///     <para>
///         Equivalent to running:
///         <c>az account get-access-token --resource &lt;guid&gt; --query accessToken -o tsv</c>.
///         The well-known AzDO AAD resource GUID is configurable through
///         <see cref="AzdoFetcherOptions.AzCliResource"/>.
///     </para>
///     <para>
///         Tokens are cached in-memory per process for a soft TTL (default 50
///         minutes) to avoid spawning <c>az</c> on every request. They are
///         never persisted to disk.
///     </para>
/// </summary>
public sealed class AzCliCredentialProvider : IAzdoCredentialProvider
{
    private static readonly TimeSpan TokenSoftTtl = TimeSpan.FromMinutes(50);

    private readonly IProcessRunner _processRunner;
    private readonly AzdoFetcherOptions _options;
    private readonly ILogger<AzCliCredentialProvider> _logger;
    private readonly ConcurrentDictionary<string, CachedToken> _cache = new(StringComparer.Ordinal);

    public AzCliCredentialProvider(IProcessRunner processRunner, IOptions<AzdoFetcherOptions> options, ILogger<AzCliCredentialProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(processRunner);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _processRunner = processRunner;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Mode => "az";

    /// <inheritdoc />
    public async Task<AuthenticationHeaderValue?> TryGetAsync(AzdoSkillSource source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        var resource = _options.AzCliResource;
        if (string.IsNullOrWhiteSpace(resource))
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        if (_cache.TryGetValue(resource, out var cached) && cached.ExpiresAt > now)
        {
            return new AuthenticationHeaderValue("Bearer", cached.Token);
        }

        var args = new[]
        {
            "account", "get-access-token",
            "--resource", resource,
            "--query", "accessToken",
            "-o", "tsv",
        };

        ProcessResult result;
        try
        {
            result = await _processRunner.RunAsync("az", args, cancellationToken).ConfigureAwait(false);
        }
        catch (FileNotFoundException ex)
        {
            _logger.LogDebug(ex, "az CLI is not available on PATH; az credential provider yielding null.");
            return null;
        }

        if (result.ExitCode != 0)
        {
            _logger.LogDebug("az get-access-token exited with code {Code}. stderr: {Stderr}", result.ExitCode, result.StandardError.Trim());
            return null;
        }

        var token = result.StandardOutput.Trim();
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }

        _cache[resource] = new CachedToken(token, now + TokenSoftTtl);
        return new AuthenticationHeaderValue("Bearer", token);
    }

    private sealed record CachedToken(string Token, DateTimeOffset ExpiresAt);
}
