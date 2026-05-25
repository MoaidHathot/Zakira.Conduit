namespace Zakira.Conduit.Sources.Azdo;

/// <summary>
///     Options controlling how the Azure DevOps fetcher talks to the REST API.
/// </summary>
public sealed class AzdoFetcherOptions
{
    /// <summary>
    ///     Default base URL for the AzDO REST API. May be overridden per-source
    ///     via <c>baseUrl</c> / <c>url</c> in the manifest. Defaults to
    ///     <c>https://dev.azure.com/</c>.
    /// </summary>
    public Uri DefaultBaseAddress { get; set; } = new("https://dev.azure.com/");

    /// <summary>The <c>User-Agent</c> header value.</summary>
    public string UserAgent { get; set; } = "Zakira.Conduit";

    /// <summary>API version query-string parameter sent on every REST call.</summary>
    public string ApiVersion { get; set; } = "7.1";

    /// <summary>
    ///     Soft cap on the size of a fetched archive, in bytes. Default 256 MiB.
    /// </summary>
    public long MaxArchiveSizeBytes { get; set; } = 256L * 1024 * 1024;

    /// <summary>
    ///     Default AAD resource ID used by <see cref="Credentials.AzCliCredentialProvider"/>
    ///     when requesting an access token. This is the well-known Azure DevOps
    ///     application ID and should not normally be overridden.
    /// </summary>
    public string AzCliResource { get; set; } = "499b84ac-1321-427f-aa17-267ca6975798";
}
