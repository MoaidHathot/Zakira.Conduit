namespace Zakira.Conduit.Strategies;

/// <summary>
///     Plans the set of mirror operations a single
///     <see cref="Manifest.ConduitEntry"/> produces.
/// </summary>
/// <remarks>
///     <para>
///         Each entry's <c>strategy</c> field selects exactly one strategy.
///         Strategies are stateless (per request); state lives on the
///         supplied <see cref="PlanContext"/>.
///     </para>
///     <para>
///         Strategies separate <i>dynamic</i> planning (after the fetcher
///         has materialised content, used by <c>conduit sync</c>) from
///         <i>static</i> planning (no fetch, used by the validator and
///         orphan cleaner to enumerate predictable destinations).
///         Strategies whose plan depends on fetched content
///         (e.g. <c>skills</c>, <c>expand</c>) return an empty list from
///         <see cref="EnumerateStaticDestinations"/>; in that case downstream
///         callers fall back to state-recorded destinations.
///     </para>
/// </remarks>
public interface IPlanStrategy
{
    /// <summary>
    ///     The canonical, lowercase strategy name. Used to resolve the entry's
    ///     <c>strategy</c> field via <see cref="IPlanStrategyRegistry"/>.
    /// </summary>
    string Name { get; }

    /// <summary>
    ///     Returns strategy-specific validation errors for <paramref name="entry"/>.
    ///     Called by <see cref="Manifest.ManifestValidator"/> after generic
    ///     entry validation. Implementations should produce
    ///     <c>"entries[{index}].field: message"</c>-shaped strings.
    /// </summary>
    /// <param name="entry">The entry being validated.</param>
    /// <param name="entryIndex">Index of <paramref name="entry"/> in the manifest, for error prefixes.</param>
    IReadOnlyList<string> ValidateEntry(Manifest.ConduitEntry entry, int entryIndex);

    /// <summary>
    ///     Plans the mirror operations for an entry whose source has been
    ///     fetched. The returned list is iterated by
    ///     <see cref="Synchronization.DefaultConduitSynchronizer"/>; each
    ///     element becomes one <see cref="Mirroring.IDirectoryMirror.MirrorAsync"/>
    ///     call.
    /// </summary>
    /// <exception cref="StrategyPlanException">
    ///     When the entry is malformed in a way that survived validation
    ///     (e.g. multi-unit fetched source with no suggested name) or when an
    ///     <see cref="OnCollisionPolicy.Error"/> collision is detected.
    /// </exception>
    IReadOnlyList<PlannedDestination> Plan(PlanContext context);

    /// <summary>
    ///     Enumerates the <i>predictable</i> target directories this entry
    ///     will produce, computed from the entry's declared shape without any
    ///     fetching. Used by:
    ///     <list type="bullet">
    ///         <item><description>
    ///             The cross-entry collision validator: it compares static
    ///             destinations across all entries.
    ///         </description></item>
    ///         <item><description>
    ///             The orphan cleaner: it uses static destinations as the
    ///             "live owner" map for surviving entries.
    ///         </description></item>
    ///     </list>
    ///     Strategies whose destinations are content-dependent return an
    ///     empty list and rely on state-recorded destinations instead.
    /// </summary>
    IReadOnlyList<string> EnumerateStaticDestinations(StaticPlanContext context);
}
