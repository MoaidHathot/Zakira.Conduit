using Zakira.Conduit.Manifest;

namespace Zakira.Conduit.Sources;

/// <summary>
///     Resolves the appropriate <see cref="ISkillSourceFetcher"/> for a given source.
/// </summary>
public interface ISkillSourceFetcherRegistry
{
    /// <summary>Returns the fetcher for <paramref name="source"/>.</summary>
    /// <exception cref="NotSupportedException">No fetcher is registered for this source kind.</exception>
    ISkillSourceFetcher GetFetcher(ISkillSource source);
}
