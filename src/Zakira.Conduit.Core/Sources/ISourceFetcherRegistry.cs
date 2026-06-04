using Zakira.Conduit.Manifest;

namespace Zakira.Conduit.Sources;

/// <summary>
///     Resolves the appropriate <see cref="ISourceFetcher"/> for a given source.
/// </summary>
public interface ISourceFetcherRegistry
{
    /// <summary>Returns the fetcher for <paramref name="source"/>.</summary>
    /// <exception cref="NotSupportedException">No fetcher is registered for this source kind.</exception>
    ISourceFetcher GetFetcher(ISource source);
}
