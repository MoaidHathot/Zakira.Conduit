using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using Zakira.Conduit.Manifest;
using Zakira.Conduit.Sources.Azdo.Credentials;

namespace Zakira.Conduit.Sources.GitHub.Credentials;

/// <summary>
///     Acquires a GitHub token from the locally-installed <c>gh</c> CLI by
///     invoking <c>gh auth token</c>. Returns <see langword="null"/> when
///     <c>gh</c> isn't installed, isn't logged in, or any non-zero exit
///     occurs; the chain then falls through to the next provider.
/// </summary>
/// <remarks>
///     The acquired token is short-circuit cached per-process for ten
///     minutes so a sync run that touches many GitHub entries doesn't shell
///     out repeatedly. Tokens are never persisted to disk.
/// </remarks>
public sealed class GhCliCredentialProvider : IGitHubCredentialProvider
{
    /// <summary>JSON discriminator for this mode.</summary>
    public const string ModeName = "gh";

    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(10);
    private static readonly SemaphoreSlim CacheLock = new(1, 1);
    private static readonly string[] AuthTokenArgs = { "auth", "token" };
    private static (string Token, DateTimeOffset ExpiresAt)? _cached;

    private readonly IProcessRunner _processRunner;
    private readonly ILogger<GhCliCredentialProvider> _logger;

    public GhCliCredentialProvider(IProcessRunner processRunner, ILogger<GhCliCredentialProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(processRunner);
        ArgumentNullException.ThrowIfNull(logger);
        _processRunner = processRunner;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Mode => ModeName;

    /// <inheritdoc />
    public async Task<AuthenticationHeaderValue?> TryGetAsync(GitHubSource source, CancellationToken cancellationToken = default)
    {
        await CacheLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cached is { } cached && cached.ExpiresAt > DateTimeOffset.UtcNow)
            {
                return new AuthenticationHeaderValue("Bearer", cached.Token);
            }

            var result = await _processRunner.RunAsync("gh", AuthTokenArgs, cancellationToken).ConfigureAwait(false);
            if (result.ExitCode != 0)
            {
                _logger.LogDebug("'gh auth token' exited with code {ExitCode}; falling through.", result.ExitCode);
                return null;
            }

            var token = result.StandardOutput.Trim();
            if (string.IsNullOrEmpty(token))
            {
                return null;
            }

            _cached = (token, DateTimeOffset.UtcNow.Add(CacheLifetime));
            return new AuthenticationHeaderValue("Bearer", token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // gh not installed, PATH lookup failed, OS denied execution, etc.
            // Don't surface; chain moves on.
            _logger.LogDebug(ex, "'gh auth token' invocation failed; falling through.");
            return null;
        }
        finally
        {
            CacheLock.Release();
        }
    }
}
