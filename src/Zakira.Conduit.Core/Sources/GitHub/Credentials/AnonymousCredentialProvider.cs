using System.Net.Http.Headers;
using Zakira.Conduit.Manifest;

namespace Zakira.Conduit.Sources.GitHub.Credentials;

/// <summary>
///     Explicit "send no <c>Authorization</c> header" marker. Reaching this
///     provider in the chain short-circuits subsequent providers and tells
///     the chained provider to return <see langword="null"/> deliberately,
///     so the caller proceeds anonymously instead of erroring.
/// </summary>
public sealed class AnonymousCredentialProvider : IGitHubCredentialProvider
{
    /// <summary>JSON discriminator for this mode.</summary>
    public const string ModeName = "anonymous";

    /// <inheritdoc />
    public string Mode => ModeName;

    /// <inheritdoc />
    public Task<AuthenticationHeaderValue?> TryGetAsync(GitHubSource source, CancellationToken cancellationToken = default) =>
        Task.FromResult<AuthenticationHeaderValue?>(null);
}
