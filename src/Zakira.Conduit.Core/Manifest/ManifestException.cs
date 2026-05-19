namespace Zakira.Conduit.Manifest;

/// <summary>
///     Raised when a manifest cannot be loaded or fails validation.
/// </summary>
public sealed class ManifestException : Exception
{
    /// <summary>The path of the manifest file, if known.</summary>
    public string? ManifestPath { get; }

    /// <summary>One human-readable error per problem found.</summary>
    public IReadOnlyList<string> Errors { get; }

    public ManifestException(string message, string? manifestPath = null, IEnumerable<string>? errors = null, Exception? innerException = null)
        : base(message, innerException)
    {
        ManifestPath = manifestPath;
        Errors = errors?.ToArray() ?? Array.Empty<string>();
    }
}
