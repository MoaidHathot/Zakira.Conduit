namespace Zakira.Conduit.Strategies;

/// <summary>
///     Default <see cref="IPlanStrategyRegistry"/>. Populated via DI from the
///     registered <see cref="IPlanStrategy"/> instances. Strategy names must
///     be unique (case-insensitive); the constructor throws on duplicates so
///     misconfiguration surfaces at startup rather than first-sync.
/// </summary>
public sealed class PlanStrategyRegistry : IPlanStrategyRegistry
{
    private readonly Dictionary<string, IPlanStrategy> _byName;

    public PlanStrategyRegistry(IEnumerable<IPlanStrategy> strategies)
    {
        ArgumentNullException.ThrowIfNull(strategies);

        _byName = new Dictionary<string, IPlanStrategy>(StringComparer.OrdinalIgnoreCase);

        foreach (var strategy in strategies)
        {
            if (strategy is null)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(strategy.Name))
            {
                throw new InvalidOperationException("A plan strategy was registered with a null or whitespace Name.");
            }

            if (!_byName.TryAdd(strategy.Name, strategy))
            {
                throw new InvalidOperationException(
                    $"Two plan strategies are registered under the same name '{strategy.Name}'. " +
                    "Names must be unique (case-insensitive).");
            }
        }

        Names = _byName.Keys.OrderBy(static n => n, StringComparer.Ordinal).ToArray();
    }

    /// <inheritdoc />
    public IPlanStrategy Resolve(string? name)
    {
        var effective = string.IsNullOrWhiteSpace(name) ? StrategyNames.Wrap : name.Trim();

        if (!_byName.TryGetValue(effective, out var strategy))
        {
            throw new UnknownStrategyException(effective, Names);
        }

        return strategy;
    }

    /// <inheritdoc />
    public bool IsRegistered(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        return _byName.ContainsKey(name.Trim());
    }

    /// <inheritdoc />
    public IReadOnlyList<string> Names { get; }
}
