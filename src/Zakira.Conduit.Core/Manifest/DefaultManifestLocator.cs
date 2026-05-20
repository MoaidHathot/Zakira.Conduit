using Zakira.Conduit.Hosting;

namespace Zakira.Conduit.Manifest;

/// <summary>
///     Default <see cref="IManifestLocator"/> implementing the XDG-style
///     resolution order documented on the interface.
/// </summary>
public sealed class DefaultManifestLocator : IManifestLocator
{
    private readonly IEnvironment _environment;

    public DefaultManifestLocator(IEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        _environment = environment;
    }

    /// <inheritdoc />
    public string Locate(string? explicitPath)
    {
        var candidates = EnumerateCandidates(explicitPath);

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        var searched = string.Join(Environment.NewLine + "  - ", candidates);
        throw new ManifestException(
            $"No conduit manifest found. Searched (in order):{Environment.NewLine}  - {searched}",
            manifestPath: null);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> EnumerateCandidates(string? explicitPath)
    {
        var list = new List<string>(capacity: 3);

        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            // Honor caller's path exactly (absolute or relative to cwd).
            list.Add(Path.IsPathRooted(explicitPath)
                ? explicitPath
                : Path.GetFullPath(explicitPath, _environment.GetCurrentDirectory()));
            return list;
        }

        // Follow the XDG Base Directory Specification on every platform: an
        // explicit XDG_CONFIG_HOME wins, otherwise the conventional fallback
        // is the user's home directory + `.config`. We intentionally do NOT
        // also probe Windows-native locations (%APPDATA%, %LOCALAPPDATA%)
        // because mixing conventions made it ambiguous which file would win.
        var xdg = _environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (!string.IsNullOrWhiteSpace(xdg))
        {
            list.Add(Path.Combine(xdg, ManifestNames.ConfigDirectoryName, ManifestNames.DefaultFileName));
        }

        var home = _environment.GetHomeDirectory();
        if (!string.IsNullOrWhiteSpace(home))
        {
            list.Add(Path.Combine(home, ".config", ManifestNames.ConfigDirectoryName, ManifestNames.DefaultFileName));
        }

        list.Add(Path.Combine(_environment.GetCurrentDirectory(), ManifestNames.DefaultFileName));

        return list;
    }
}
