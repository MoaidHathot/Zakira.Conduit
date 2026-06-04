using Zakira.Conduit.Manifest;

namespace Zakira.Conduit.Sources;

/// <summary>
///     Default registry that resolves fetchers by <see cref="ISource.Kind"/>.
/// </summary>
public sealed class DefaultSourceFetcherRegistry : ISourceFetcherRegistry
{
    private readonly Dictionary<string, ISourceFetcher> _fetchers;

    public DefaultSourceFetcherRegistry(IEnumerable<ISourceFetcher> fetchers)
    {
        ArgumentNullException.ThrowIfNull(fetchers);

        _fetchers = new Dictionary<string, ISourceFetcher>(StringComparer.OrdinalIgnoreCase);
        foreach (var fetcher in fetchers)
        {
            if (!_fetchers.TryAdd(fetcher.SourceKind, fetcher))
            {
                throw new InvalidOperationException($"Multiple fetchers registered for source kind '{fetcher.SourceKind}'.");
            }
        }
    }

    /// <inheritdoc />
    public ISourceFetcher GetFetcher(ISource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (!_fetchers.TryGetValue(source.Kind, out var fetcher))
        {
            throw new NotSupportedException($"No fetcher registered for source kind '{source.Kind}'.");
        }

        return fetcher;
    }
}
