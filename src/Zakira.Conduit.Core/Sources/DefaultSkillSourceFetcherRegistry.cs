using Zakira.Conduit.Manifest;

namespace Zakira.Conduit.Sources;

/// <summary>
///     Default registry that resolves fetchers by <see cref="ISkillSource.Kind"/>.
/// </summary>
public sealed class DefaultSkillSourceFetcherRegistry : ISkillSourceFetcherRegistry
{
    private readonly Dictionary<string, ISkillSourceFetcher> _fetchers;

    public DefaultSkillSourceFetcherRegistry(IEnumerable<ISkillSourceFetcher> fetchers)
    {
        ArgumentNullException.ThrowIfNull(fetchers);

        _fetchers = new Dictionary<string, ISkillSourceFetcher>(StringComparer.OrdinalIgnoreCase);
        foreach (var fetcher in fetchers)
        {
            if (!_fetchers.TryAdd(fetcher.SourceKind, fetcher))
            {
                throw new InvalidOperationException($"Multiple fetchers registered for source kind '{fetcher.SourceKind}'.");
            }
        }
    }

    /// <inheritdoc />
    public ISkillSourceFetcher GetFetcher(ISkillSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (!_fetchers.TryGetValue(source.Kind, out var fetcher))
        {
            throw new NotSupportedException($"No fetcher registered for source kind '{source.Kind}'.");
        }

        return fetcher;
    }
}
