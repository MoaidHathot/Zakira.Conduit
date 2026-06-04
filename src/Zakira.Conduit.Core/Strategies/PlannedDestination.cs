using Zakira.Conduit.Manifest;
using Zakira.Conduit.Mirroring;

namespace Zakira.Conduit.Strategies;

/// <summary>
///     One planned mirror operation: copy <see cref="SourceDirectory"/>
///     into <see cref="TargetDirectory"/>, optionally filtered.
/// </summary>
/// <param name="SourceDirectory">
///     Absolute path to the directory whose contents should be mirrored.
///     For most strategies this is a sub-directory of a fetched content unit.
/// </param>
/// <param name="TargetDirectory">
///     Absolute path the contents should be mirrored into.
/// </param>
/// <param name="Filter">
///     File-relative filter applied at mirror time. Patterns are anchored
///     to <see cref="SourceDirectory"/>. <see langword="null"/> means
///     "include everything".
/// </param>
/// <param name="OriginEntry">
///     The entry that produced this destination. Used for diagnostics and
///     by collision messages.
/// </param>
/// <param name="OriginContentDirectory">
///     The fetched content root this destination's
///     <see cref="SourceDirectory"/> sits under. Equal to
///     <see cref="SourceDirectory"/> for whole-unit strategies (wrap, flat);
///     differs for strategies that narrow to a sub-directory (expand, skills).
/// </param>
public sealed record PlannedDestination(
    string SourceDirectory,
    string TargetDirectory,
    MirrorFilter? Filter,
    ConduitEntry OriginEntry,
    string OriginContentDirectory);
