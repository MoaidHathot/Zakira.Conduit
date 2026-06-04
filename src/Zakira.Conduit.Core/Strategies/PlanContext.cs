using Zakira.Conduit.Manifest;
using Zakira.Conduit.Mirroring;
using Zakira.Conduit.Paths;
using Zakira.Conduit.Sources;

namespace Zakira.Conduit.Strategies;

/// <summary>
///     The inputs to <see cref="IPlanStrategy.Plan"/>: an entry whose source
///     has been fetched, plus the collaborators a strategy needs to compute
///     concrete destination paths.
/// </summary>
public sealed class PlanContext
{
    public PlanContext(
        ConduitEntry entry,
        FetchedSource fetched,
        string manifestDirectory,
        IPathResolver pathResolver,
        MirrorFilter? filter,
        StrategyConfigSnapshot strategiesConfig)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(fetched);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestDirectory);
        ArgumentNullException.ThrowIfNull(pathResolver);
        ArgumentNullException.ThrowIfNull(strategiesConfig);

        Entry = entry;
        Fetched = fetched;
        ManifestDirectory = manifestDirectory;
        PathResolver = pathResolver;
        Filter = filter;
        StrategiesConfig = strategiesConfig;
    }

    /// <summary>The entry the strategy is planning for.</summary>
    public ConduitEntry Entry { get; }

    /// <summary>The fetched source content. Always has at least one unit.</summary>
    public FetchedSource Fetched { get; }

    /// <summary>Manifest-file parent directory, used to root relative target paths.</summary>
    public string ManifestDirectory { get; }

    /// <summary>Resolver for <c>~</c>, env vars, and relative paths.</summary>
    public IPathResolver PathResolver { get; }

    /// <summary>
    ///     The source-level include/exclude filter. Strategies pass this
    ///     through on every <see cref="PlannedDestination"/> they emit so
    ///     the mirror can honour it.
    /// </summary>
    public MirrorFilter? Filter { get; }

    /// <summary>
    ///     Manifest-global strategy configuration (e.g. the merged
    ///     skills-strategy harness registry). Snapshotted at plan time so the
    ///     strategy doesn't need its own ambient state.
    /// </summary>
    public StrategyConfigSnapshot StrategiesConfig { get; }
}

/// <summary>
///     A "no fetched content" planning context, used by the validator and
///     orphan cleaner to enumerate the destinations an entry will produce
///     based on its declared shape alone. Strategies whose destinations are
///     content-dependent (e.g. skills, expand) return an empty list.
/// </summary>
public sealed class StaticPlanContext
{
    public StaticPlanContext(
        ConduitEntry entry,
        string manifestDirectory,
        IPathResolver pathResolver,
        StrategyConfigSnapshot strategiesConfig)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestDirectory);
        ArgumentNullException.ThrowIfNull(pathResolver);
        ArgumentNullException.ThrowIfNull(strategiesConfig);

        Entry = entry;
        ManifestDirectory = manifestDirectory;
        PathResolver = pathResolver;
        StrategiesConfig = strategiesConfig;
    }

    public ConduitEntry Entry { get; }
    public string ManifestDirectory { get; }
    public IPathResolver PathResolver { get; }
    public StrategyConfigSnapshot StrategiesConfig { get; }
}
