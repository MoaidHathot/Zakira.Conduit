using System.Net.Http.Headers;
using Zakira.Conduit.Hosting;
using Zakira.Conduit.Manifest;

namespace Zakira.Conduit.Sources.Azdo.Credentials;

/// <summary>
///     Reads a PAT from one of the well-known environment variables and emits
///     a <c>Basic</c> header. Variables checked in order:
///     <list type="bullet">
///         <item><description><c>CONDUIT_AZDO_TOKEN</c></description></item>
///         <item><description><c>AZURE_DEVOPS_EXT_PAT</c> (matches the <c>azure-devops</c> az extension)</description></item>
///         <item><description><c>SYSTEM_ACCESSTOKEN</c> (set by AzDO Pipelines)</description></item>
///     </list>
/// </summary>
public sealed class EnvironmentPatCredentialProvider : IAzdoCredentialProvider
{
    private static readonly string[] CandidateVars =
    {
        "CONDUIT_AZDO_TOKEN",
        "AZURE_DEVOPS_EXT_PAT",
        "SYSTEM_ACCESSTOKEN",
    };

    private readonly IEnvironment _environment;

    public EnvironmentPatCredentialProvider(IEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        _environment = environment;
    }

    /// <inheritdoc />
    public string Mode => "env";

    /// <inheritdoc />
    public Task<AuthenticationHeaderValue?> TryGetAsync(AzdoSkillSource source, CancellationToken cancellationToken = default)
    {
        foreach (var name in CandidateVars)
        {
            var value = _environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return Task.FromResult<AuthenticationHeaderValue?>(AzdoAuth.BasicForPat(value));
            }
        }

        return Task.FromResult<AuthenticationHeaderValue?>(null);
    }
}
