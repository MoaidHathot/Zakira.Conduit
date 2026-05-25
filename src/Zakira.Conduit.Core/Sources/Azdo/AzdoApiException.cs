using System.Net;

namespace Zakira.Conduit.Sources.Azdo;

/// <summary>
///     Raised when Azure DevOps returns a non-success status code or otherwise
///     refuses a request.
/// </summary>
public sealed class AzdoApiException : Exception
{
    /// <summary>The HTTP status code returned by AzDO.</summary>
    public HttpStatusCode StatusCode { get; }

    public AzdoApiException(string message, HttpStatusCode statusCode, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}
