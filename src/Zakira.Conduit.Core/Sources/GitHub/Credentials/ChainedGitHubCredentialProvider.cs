using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using Zakira.Conduit.Manifest;

namespace Zakira.Conduit.Sources.GitHub.Credentials;

/// <summary>
///     Walks an ordered list of <see cref="IGitHubCredentialProvider"/>s and
///     returns the first non-null credential. Resolves the chain at call-time
///     against <see cref="GitHubSource.ResolvedAuthChain"/>, so the manifest
///     can configure the order per-entry.
/// </summary>
public sealed class ChainedGitHubCredentialProvider
{
    private readonly IReadOnlyDictionary<string, IGitHubCredentialProvider> _byMode;
    private readonly ILogger<ChainedGitHubCredentialProvider> _logger;

    public ChainedGitHubCredentialProvider(IEnumerable<IGitHubCredentialProvider> providers, ILogger<ChainedGitHubCredentialProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(logger);

        _byMode = providers.ToDictionary(p => p.Mode, p => p, StringComparer.OrdinalIgnoreCase);
        _logger = logger;
    }

    /// <summary>
    ///     Attempts to obtain a credential for <paramref name="source"/>.
    ///     Returns <see langword="null"/> when every provider in the chain
    ///     declined; the caller may proceed anonymously.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    ///     When the source references an unknown auth mode.
    /// </exception>
    public async Task<AuthenticationHeaderValue?> TryGetAsync(GitHubSource source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        var chain = source.ResolvedAuthChain;
        var triedModes = new List<string>(chain.Count);

        foreach (var mode in chain)
        {
            if (!_byMode.TryGetValue(mode, out var provider))
            {
                throw new InvalidOperationException(
                    $"Unknown github auth mode '{mode}'. Known modes: {string.Join(", ", _byMode.Keys)}.");
            }

            triedModes.Add(mode);

            // The anonymous provider is special: hitting it short-circuits the
            // chain with "proceed without auth" (null), rather than falling through.
            if (provider is AnonymousCredentialProvider)
            {
                _logger.LogDebug("github auth: 'anonymous' mode reached; proceeding without an Authorization header.");
                return null;
            }

            var credential = await provider.TryGetAsync(source, cancellationToken).ConfigureAwait(false);
            if (credential is not null)
            {
                _logger.LogDebug("github auth: acquired credential via '{Mode}'.", mode);
                return credential;
            }
        }

        _logger.LogDebug("github auth: no provider in chain [{Chain}] yielded a credential.", string.Join(", ", triedModes));
        return null;
    }
}
