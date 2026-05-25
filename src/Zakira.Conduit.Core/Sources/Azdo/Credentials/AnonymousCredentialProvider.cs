using System.Net.Http.Headers;
using Zakira.Conduit.Manifest;

namespace Zakira.Conduit.Sources.Azdo.Credentials;

/// <summary>
///     No-op provider that always yields <see langword="null"/> when present
///     in the chain it short-circuits any later (failing) auth attempts and
///     lets the fetcher attempt the call with no <c>Authorization</c> header.
/// </summary>
public sealed class AnonymousCredentialProvider : IAzdoCredentialProvider
{
    /// <inheritdoc />
    public string Mode => "anonymous";

    /// <inheritdoc />
    public Task<AuthenticationHeaderValue?> TryGetAsync(AzdoSkillSource source, CancellationToken cancellationToken = default) =>
        Task.FromResult<AuthenticationHeaderValue?>(null);
}
