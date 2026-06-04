using System.Net.Http.Headers;
using Zakira.Conduit.Manifest;

namespace Zakira.Conduit.Sources.GitHub.Credentials;

/// <summary>
///     One way of acquiring a GitHub credential to attach to outbound HTTP
///     requests. Returns <see langword="null"/> when this provider can't
///     contribute a credential for the given source (e.g. an env var is
///     unset or <c>gh</c> isn't installed); callers can then move on to the
///     next link in the chain. Mirrors the AzDO credential model so behaviour
///     stays consistent across source kinds.
/// </summary>
public interface IGitHubCredentialProvider
{
    /// <summary>The mode name this provider answers to (<c>"env"</c>, <c>"gh"</c>, ...).</summary>
    string Mode { get; }

    /// <summary>
    ///     Attempts to acquire a credential. Returns <see langword="null"/>
    ///     when no credential is available from this provider.
    /// </summary>
    Task<AuthenticationHeaderValue?> TryGetAsync(GitHubSource source, CancellationToken cancellationToken = default);
}
