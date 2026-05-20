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
///         <b>Limitation:</b> JSON comments are not preserved by any parser
///         in the BCL. Users with hand-written comments will lose them.
///         Trailing commas are also normalised out.
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
}
