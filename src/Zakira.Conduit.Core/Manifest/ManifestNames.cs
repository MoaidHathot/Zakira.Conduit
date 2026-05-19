namespace Zakira.Conduit.Manifest;

/// <summary>
///     File-name constants for the conduit manifest.
/// </summary>
public static class ManifestNames
{
    /// <summary>
    ///     The default file name of the conduit manifest, expected at
    ///     <c>$XDG_CONFIG_HOME/conduit/conduit.json</c>.
    /// </summary>
    public const string DefaultFileName = "conduit.json";

    /// <summary>
    ///     The directory name (under <c>$XDG_CONFIG_HOME</c>) that contains the manifest.
    /// </summary>
    public const string ConfigDirectoryName = "conduit";

    /// <summary>
    ///     The current manifest schema version.
    /// </summary>
    public const int CurrentSchemaVersion = 1;
}
