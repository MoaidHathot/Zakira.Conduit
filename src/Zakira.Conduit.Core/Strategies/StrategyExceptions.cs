namespace Zakira.Conduit.Strategies;

/// <summary>
///     Thrown by <see cref="IPlanStrategyRegistry.Resolve"/> when the entry
///     references a strategy name that no implementation is registered for.
/// </summary>
public sealed class UnknownStrategyException : InvalidOperationException
{
    public UnknownStrategyException(string requested, IReadOnlyList<string> available)
        : base(BuildMessage(requested, available))
    {
        Requested = requested;
        Available = available;
    }

    /// <summary>The unknown strategy name that was looked up.</summary>
    public string Requested { get; }

    /// <summary>The names of every strategy currently registered.</summary>
    public IReadOnlyList<string> Available { get; }

    private static string BuildMessage(string requested, IReadOnlyList<string> available)
    {
        var list = available is { Count: > 0 }
            ? string.Join(", ", available)
            : "(none)";

        return $"Unknown strategy '{requested}'. Available strategies: {list}.";
    }
}

/// <summary>
///     Thrown by <see cref="IPlanStrategy.Plan"/> when the entry cannot be
///     mirrored as written &mdash; e.g. multi-unit content with no suggested
///     names, or unresolvable collisions under
///     <see cref="OnCollisionPolicy.Error"/>.
/// </summary>
public sealed class StrategyPlanException : InvalidOperationException
{
    public StrategyPlanException(string message)
        : base(message)
    {
    }

    public StrategyPlanException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
