using System.Net.Http.Headers;
using Zakira.Conduit.Hosting;
using Zakira.Conduit.Manifest;

namespace Zakira.Conduit.Sources.GitHub.Credentials;

/// <summary>
///     Acquires a GitHub token from the environment using a small ordered
///     lookup: <c>CONDUIT_GITHUB_TOKEN</c> -&gt; <c>GITHUB_TOKEN</c> -&gt;
///     <c>GH_TOKEN</c>. Returns the first non-empty value as a
///     <c>Authorization: Bearer ...</c> header. Mirrors the AzDO
///     <c>EnvironmentPatCredentialProvider</c> in spirit; the env var names
///     are GitHub-conventional (and align with the official <c>gh</c> CLI).
/// </summary>
public sealed class EnvironmentTokenCredentialProvider : IGitHubCredentialProvider
{
    /// <summary>JSON discriminator for this mode.</summary>
    public const string ModeName = "env";

    private static readonly string[] EnvVarOrder = { "CONDUIT_GITHUB_TOKEN", "GITHUB_TOKEN", "GH_TOKEN" };

    private readonly IEnvironment _environment;

    public EnvironmentTokenCredentialProvider(IEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        _environment = environment;
    }

    /// <inheritdoc />
    public string Mode => ModeName;

    /// <inheritdoc />
    public Task<AuthenticationHeaderValue?> TryGetAsync(GitHubSource source, CancellationToken cancellationToken = default)
    {
        foreach (var name in EnvVarOrder)
        {
            var value = _environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return Task.FromResult<AuthenticationHeaderValue?>(new AuthenticationHeaderValue("Bearer", value));
            }
        }

        return Task.FromResult<AuthenticationHeaderValue?>(null);
    }
}
