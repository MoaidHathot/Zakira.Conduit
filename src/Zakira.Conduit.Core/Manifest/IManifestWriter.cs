namespace Zakira.Conduit.Manifest;

/// <summary>
///     Rewrites a manifest file on disk while preserving its existing
///     structure as much as is possible with <see cref="System.Text.Json"/>.
///     Used by <c>pin</c>, <c>update</c>, and future imperative-edit commands
///     to mutate the user's manifest without losing fields or property order.
///     <para>
///         A back-up of the original file is written next to it with a
///         <c>.bak</c> suffix before the new content is committed.
///     </para>
///     <para>
///         <b>Limitation:</b> <see cref="RewriteAsync"/> rebuilds the file
///         from a parsed <see cref="System.Text.Json.Nodes.JsonNode"/> tree,
///         so JSON comments and trailing commas in the source are not
///         preserved. <see cref="ReplaceStringLeavesAsync"/> is a narrow
///         fast-path that <i>does</i> preserve trivia for the common case of
///         editing a few existing leaf string values.
///     </para>
/// </summary>
public interface IManifestWriter
{
    /// <summary>
    ///     Reads the manifest at <paramref name="manifestPath"/>, lets
    ///     <paramref name="mutate"/> modify the parsed JSON tree, writes a
    ///     <c>.bak</c> of the original, and then commits the mutated content
    ///     atomically.
    /// </summary>
    /// <returns>The path of the backup file that was written.</returns>
    Task<string> RewriteAsync(string manifestPath, Action<System.Text.Json.Nodes.JsonObject> mutate, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Surgical string-leaf rewrite that preserves all bytes outside the
    ///     targeted JSON string tokens &mdash; comments, trailing commas,
    ///     whitespace, indentation. Use this whenever a command only needs to
    ///     replace a handful of already-present string values (e.g.
    ///     <c>commit</c> SHA updates in <c>conduit pin</c>); fall back to
    ///     <see cref="RewriteAsync"/> for anything structural.
    ///     <para>
    ///         Returns <see langword="true"/> on success.
    ///         <see langword="false"/> is returned when any requested path
    ///         does not resolve to an existing JSON string leaf, in which
    ///         case the file on disk is left unchanged and no backup is made;
    ///         callers can then fall back to <see cref="RewriteAsync"/>.
    ///     </para>
    /// </summary>
    /// <returns>
    ///     A tuple: <c>Patched</c> is whether the file was modified;
    ///     <c>BackupPath</c> is the path of the <c>.bak</c> file when
    ///     <c>Patched</c> is true, or <see langword="null"/> otherwise.
    /// </returns>
    Task<(bool Patched, string? BackupPath)> ReplaceStringLeavesAsync(
        string manifestPath,
        IReadOnlyList<JsonValuePatcher.StringEdit> edits,
        CancellationToken cancellationToken = default);
}
