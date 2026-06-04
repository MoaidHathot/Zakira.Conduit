using System.Net.Http.Headers;
using Zakira.Conduit.Hosting;
using Zakira.Conduit.Manifest;

namespace Zakira.Conduit.Sources.GitHub.Credentials;

/// <summary>
///     Reads a PAT from an environment variable whose name is supplied by
///     the <see cref="GitHubSource.PatEnv"/> field (defaults to
///     <c>CONDUIT_GITHUB_TOKEN</c>). Exists as a separate mode so the user
///     can opt into "I'm storing my token in <c>MY_CUSTOM_VAR</c>; use that
///     specifically" without the broader <see cref="EnvironmentTokenCredentialProvider"/>
///     env-var search list kicking in.
/// </summary>
public sealed class ExplicitPatCredentialProvider : IGitHubCredentialProvider
{
    /// <summary>JSON discriminator for this mode.</summary>
    public const string ModeName = "pat";

    /// <summary>Default env-var name when <see cref="GitHubSource.PatEnv"/> is unset.</summary>
    public const string DefaultPatEnvVar = "CONDUIT_GITHUB_TOKEN";

    private readonly IEnvironment _environment;

    public ExplicitPatCredentialProvider(IEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        _environment = environment;
    }

    /// <inheritdoc />
    public string Mode => ModeName;

    /// <inheritdoc />
    public Task<AuthenticationHeaderValue?> TryGetAsync(GitHubSource source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        var envName = string.IsNullOrWhiteSpace(source.PatEnv) ? DefaultPatEnvVar : source.PatEnv;
        var value = _environment.GetEnvironmentVariable(envName);
        if (string.IsNullOrWhiteSpace(value))
        {
            return Task.FromResult<AuthenticationHeaderValue?>(null);
        }

        return Task.FromResult<AuthenticationHeaderValue?>(new AuthenticationHeaderValue("Bearer", value));
    }
}
