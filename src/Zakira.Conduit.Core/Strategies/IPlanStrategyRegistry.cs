namespace Zakira.Conduit.Strategies;

/// <summary>
///     Lookup interface that maps a strategy name (case-insensitive) to its
///     concrete <see cref="IPlanStrategy"/> implementation.
/// </summary>
public interface IPlanStrategyRegistry
{
    /// <summary>
    ///     Returns the strategy for <paramref name="name"/>, or the default
    ///     strategy (<see cref="StrategyNames.Wrap"/>) when
    ///     <paramref name="name"/> is null or empty.
    /// </summary>
    /// <exception cref="UnknownStrategyException">
    ///     When <paramref name="name"/> is non-empty but unrecognised.
    /// </exception>
    IPlanStrategy Resolve(string? name);

    /// <summary>
    ///     Returns <see langword="true"/> when a strategy with the given name
    ///     is registered. The lookup is case-insensitive.
    /// </summary>
    bool IsRegistered(string name);

    /// <summary>The names of every registered strategy (sorted, lowercase).</summary>
    IReadOnlyList<string> Names { get; }
}
