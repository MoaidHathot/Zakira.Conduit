using System.Net.Http.Headers;
using System.Text;
using Zakira.Conduit.Manifest;

namespace Zakira.Conduit.Sources.Azdo.Credentials;

/// <summary>
///     One way of acquiring an Azure DevOps credential to attach to outbound
///     HTTP requests. Returns <see langword="null"/> when this provider can't
///     contribute a credential for the given source (e.g. the env var is unset);
///     callers can then move on to the next link in the chain.
/// </summary>
public interface IAzdoCredentialProvider
{
    /// <summary>The mode name this provider answers to (e.g. <c>"env"</c>).</summary>
    string Mode { get; }

    /// <summary>
    ///     Attempts to acquire a credential. Returns <see langword="null"/>
    ///     when no credential is available from this provider.
    /// </summary>
    Task<AuthenticationHeaderValue?> TryGetAsync(AzdoSkillSource source, CancellationToken cancellationToken = default);
}

/// <summary>
///     Helper for building the <c>Authorization: Basic</c> header that AzDO
///     expects when a PAT is used. The username is empty; only the PAT matters.
/// </summary>
internal static class AzdoAuth
{
    public static AuthenticationHeaderValue BasicForPat(string pat)
    {
        var encoded = Convert.ToBase64String(Encoding.ASCII.GetBytes(":" + pat));
        return new AuthenticationHeaderValue("Basic", encoded);
    }
}
