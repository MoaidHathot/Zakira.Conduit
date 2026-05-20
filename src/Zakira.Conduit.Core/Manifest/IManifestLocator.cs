namespace Zakira.Conduit.Manifest;

/// <summary>
///     Locates a conduit manifest file on disk using the resolution order:
///     <list type="number">
///         <item><description>The explicit path provided by the caller.</description></item>
///         <item><description><c>$XDG_CONFIG_HOME/Zakira.Conduit/conduit.json</c>.</description></item>
///         <item><description><c>$HOME/.config/Zakira.Conduit/conduit.json</c> (XDG default fallback).</description></item>
///         <item><description><c>%APPDATA%/Zakira.Conduit/conduit.json</c> (Windows fallback).</description></item>
///         <item><description><c>./conduit.json</c> (current working directory).</description></item>
///     </list>
/// </summary>
public interface IManifestLocator
{
    /// <summary>
    ///     Resolves the manifest path. <paramref name="explicitPath"/> wins when
    ///     provided; otherwise the discovery order is followed.
    /// </summary>
    /// <returns>The resolved absolute path. Existence is verified.</returns>
    /// <exception cref="ManifestException">No candidate manifest exists.</exception>
    string Locate(string? explicitPath);

    /// <summary>
    ///     Returns the list of candidate paths the locator would search, in order.
    ///     The list is not filtered by existence.
    /// </summary>
    IReadOnlyList<string> EnumerateCandidates(string? explicitPath);
}
