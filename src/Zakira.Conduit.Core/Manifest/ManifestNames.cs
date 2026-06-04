namespace Zakira.Conduit.Manifest;

/// <summary>
///     File-name constants for the conduit manifest.
/// </summary>
public static class ManifestNames
{
    /// <summary>
    ///     The default file name of the conduit manifest, expected at
    ///     <c>$XDG_CONFIG_HOME/Zakira.Conduit/conduit.json</c>.
    /// </summary>
    public const string DefaultFileName = "conduit.json";

    /// <summary>
    ///     The optional JSONC-extension alias. Discovery probes both this
    ///     name and <see cref="DefaultFileName"/> at every search location,
    ///     preferring <see cref="DefaultFileName"/> when both exist. The
    ///     content format is identical &mdash; the loader accepts comments
    ///     (<c>//</c>, <c>/* */</c>) and trailing commas regardless of the
    ///     file extension &mdash; so this is purely an editor hint for
    ///     tooling that switches on <c>.jsonc</c>.
    /// </summary>
    public const string AltJsoncFileName = "conduit.jsonc";

    /// <summary>
    ///     File names probed by the locator, in preference order. The first
    ///     match at any search location wins.
    /// </summary>
    public static readonly IReadOnlyList<string> AllFileNames = new[]
    {
        DefaultFileName,
        AltJsoncFileName,
    };

    /// <summary>
    ///     The directory name (under <c>$XDG_CONFIG_HOME</c>) that contains the manifest.
    /// </summary>
    public const string ConfigDirectoryName = "Zakira.Conduit";

    /// <summary>
    ///     The current manifest schema version.
    /// </summary>
    public const int CurrentSchemaVersion = 1;
}
