using System.Net.Http.Headers;
using Microsoft.Extensions.Options;
using Zakira.Conduit.Manifest;

namespace Zakira.Conduit.Sources.GitHub.Credentials;

/// <summary>
///     Adapter that exposes the legacy <see cref="GitHubFetcherOptions.Token"/>
///     setting as a regular <see cref="IGitHubCredentialProvider"/> at the
///     <c>options</c> mode position. Registered automatically when DI sees a
///     non-empty token in the options, so existing callers that set
///     <see cref="GitHubFetcherOptions.Token"/> at startup keep working
///     unchanged.
/// </summary>
public sealed class OptionsTokenCredentialProvider : IGitHubCredentialProvider
{
    /// <summary>JSON discriminator for this mode.</summary>
    public const string ModeName = "options";

    private readonly GitHubFetcherOptions _options;

    public OptionsTokenCredentialProvider(IOptions<GitHubFetcherOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
    }

    /// <inheritdoc />
    public string Mode => ModeName;

    /// <inheritdoc />
    public Task<AuthenticationHeaderValue?> TryGetAsync(GitHubSource source, CancellationToken cancellationToken = default)
    {
        var token = _options.Token;
        if (string.IsNullOrWhiteSpace(token))
        {
            return Task.FromResult<AuthenticationHeaderValue?>(null);
        }

        return Task.FromResult<AuthenticationHeaderValue?>(new AuthenticationHeaderValue("Bearer", token));
    }
}
