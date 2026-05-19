using Zakira.Conduit.Manifest;

namespace Zakira.Conduit.Sources;

/// <summary>
///     Fetches a single <see cref="ISkillSource"/> kind into a local directory.
///     Implementations are registered with <see cref="ISkillSourceFetcherRegistry"/>.
/// </summary>
public interface ISkillSourceFetcher
{
    /// <summary>
    ///     The discriminator value handled by this fetcher (e.g. <c>"github"</c>).
    /// </summary>
    string SourceKind { get; }

    /// <summary>
    ///     Materializes the source into a local working directory. The returned
    ///     <see cref="FetchedSource"/> owns the directory; dispose it to clean up.
    /// </summary>
    /// <param name="source">The source description from the manifest.</param>
    /// <param name="context">Ambient information about the sync run (e.g. manifest directory for relative path resolution).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<FetchedSource> FetchAsync(ISkillSource source, FetchContext context, CancellationToken cancellationToken = default);
}
