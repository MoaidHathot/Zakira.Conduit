using System.Net.Http.Headers;
using Zakira.Conduit.Hosting;
using Zakira.Conduit.Manifest;

namespace Zakira.Conduit.Sources.Azdo.Credentials;

/// <summary>
///     Reads a PAT from an explicit env var named by
///     <see cref="AzdoSkillSource.PatEnv"/>. Useful when one manifest references
///     several AzDO orgs with different tokens.
/// </summary>
public sealed class ExplicitPatCredentialProvider : IAzdoCredentialProvider
{
    private readonly IEnvironment _environment;

    public ExplicitPatCredentialProvider(IEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        _environment = environment;
    }

    /// <inheritdoc />
    public string Mode => "pat";

    /// <inheritdoc />
    public Task<AuthenticationHeaderValue?> TryGetAsync(AzdoSkillSource source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        var name = string.IsNullOrWhiteSpace(source.PatEnv) ? "CONDUIT_AZDO_TOKEN" : source.PatEnv;
        var value = _environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            return Task.FromResult<AuthenticationHeaderValue?>(null);
        }

        return Task.FromResult<AuthenticationHeaderValue?>(AzdoAuth.BasicForPat(value));
    }
}
