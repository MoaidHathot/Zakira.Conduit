using Zakira.Conduit.Manifest;

namespace Zakira.Conduit.Sources.Inference;

/// <summary>
///     One half of the URI-to-source-kind inference pipeline. Each
///     implementation answers a single question:
///     "given this <c>uri</c>, can I produce a concrete
///     <see cref="ISource"/> for my kind?".
/// </summary>
/// <remarks>
///     Implementations are pure (no IO, no environment lookups) so the
///     <see cref="SourceInferenceCoordinator"/> can probe them
///     deterministically. Implementations must not mutate
///     <paramref name="source"/>; they read from it and emit a new record.
/// </remarks>
public interface ISourceInferrer
{
    /// <summary>
    ///     A short identifier (e.g. <c>"github"</c>) used in error messages.
    /// </summary>
    string Kind { get; }

    /// <summary>
    ///     Whether this inferrer recognises <paramref name="uri"/> as belonging
    ///     to its kind. Called for every registered inferrer in registration
    ///     order; the first match wins.
    /// </summary>
    bool CanHandle(string uri);

    /// <summary>
    ///     Builds the concrete source for <paramref name="source"/>. Throws
    ///     <see cref="SourceInferenceException"/> when a per-kind field
    ///     constraint is violated (e.g. a non-applicable optional field).
    /// </summary>
    ISource Infer(UriBasedSource source);
}

/// <summary>
///     Raised when an <see cref="ISourceInferrer"/> cannot translate a
///     <see cref="UriBasedSource"/> into its concrete form (for example,
///     the URI is malformed, or a field that doesn't apply to the inferred
///     kind was set).
/// </summary>
public sealed class SourceInferenceException : Exception
{
    public SourceInferenceException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
