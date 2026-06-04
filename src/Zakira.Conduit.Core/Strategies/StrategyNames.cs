namespace Zakira.Conduit.Strategies;

/// <summary>
///     Canonical strategy names. The <see cref="ConduitEntry.Strategy"/> field
///     accepts these (case-insensitive) and resolves them to a concrete
///     <see cref="IPlanStrategy"/> via <see cref="IPlanStrategyRegistry"/>.
/// </summary>
public static class StrategyNames
{
    /// <summary>
    ///     Default strategy. Each fetched content unit is mirrored into a
    ///     sub-directory of each target. For single-unit entries the
    ///     sub-directory is named after the entry (or per-target alias); for
    ///     multi-unit entries each unit uses its
    ///     <see cref="Sources.FetchedContent.SuggestedDestinationName"/>.
    ///     Backwards-compatible with pre-strategy manifests.
    /// </summary>
    public const string Wrap = "wrap";

    /// <summary>
    ///     The contents of each fetched unit are mirrored directly into the
    ///     target directory (no per-entry wrapping sub-directory). Multi-unit
    ///     entries merge into the same target; the entry's
    ///     <see cref="OnCollisionPolicy"/> decides how to resolve duplicate
    ///     relative paths.
    /// </summary>
    public const string Flat = "flat";

    /// <summary>
    ///     Each top-level child directory of every fetched unit becomes its
    ///     own sub-directory at the target. Files at the unit root are
    ///     silently ignored (they have no natural per-directory destination).
    /// </summary>
    public const string Expand = "expand";

    /// <summary>
    ///     Discovers <c>SKILL.md</c> files in the fetched content and mirrors
    ///     each containing folder as an independent skill at the target. The
    ///     skill name (the folder basename, matching the frontmatter
    ///     <c>name:</c>) becomes the destination sub-directory.
    /// </summary>
    public const string Skills = "skills";
}
