namespace Zakira.Conduit.Strategies;

/// <summary>
///     Policy for resolving the situation where two planned destinations
///     within a single entry would write to the same path.
/// </summary>
/// <remarks>
///     The default for v1 strategies is <see cref="Error"/>: collisions are
///     loud, with a friendly message naming both sources. The other modes
///     exist for users who knowingly want to merge multiple sources into a
///     shared target.
/// </remarks>
public enum OnCollisionPolicy
{
    /// <summary>Abort the entry and report which two sources collide. (Default.)</summary>
    Error = 0,

    /// <summary>Keep the first-seen destination; subsequent duplicates are dropped from the plan.</summary>
    Skip = 1,

    /// <summary>Each subsequent duplicate replaces the previous one in the plan.</summary>
    LastWins = 2,
}
