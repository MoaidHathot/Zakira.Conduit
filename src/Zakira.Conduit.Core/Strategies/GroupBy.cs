namespace Zakira.Conduit.Strategies;

/// <summary>
///     Composable grouping modifier applied on top of a strategy's destination
///     layout. Independent of <see cref="IPlanStrategy"/>: every strategy
///     respects it the same way &mdash; "wrap each source's planned output in
///     a sub-directory named after the source".
/// </summary>
public enum GroupBy
{
    /// <summary>No grouping; the strategy's destinations are used as-is.</summary>
    None = 0,

    /// <summary>
    ///     Each entry's planned destinations are wrapped in an additional
    ///     sub-directory named after the entry's source (the GitHub/AzDO repo
    ///     name, the local-directory basename, or an explicit alias). Useful
    ///     when several entries write into a shared target and you want
    ///     each source's contributions kept together for provenance.
    /// </summary>
    Source = 1,
}
