using System.Runtime.InteropServices;

namespace Zakira.Conduit.Hosting;

/// <summary>
///     The production <see cref="IEnvironment"/>, backed by <see cref="System.Environment"/>.
/// </summary>
public sealed class SystemEnvironment : IEnvironment
{
    /// <inheritdoc />
    public string? GetEnvironmentVariable(string name) => Environment.GetEnvironmentVariable(name);

    /// <inheritdoc />
    public string GetHomeDirectory()
    {
        // Prefer USERPROFILE on Windows and HOME elsewhere; fall back to SpecialFolder.UserProfile.
        var home = IsWindows ? Environment.GetEnvironmentVariable("USERPROFILE") : Environment.GetEnvironmentVariable("HOME");
        if (!string.IsNullOrWhiteSpace(home))
        {
            return home;
        }

        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    /// <inheritdoc />
    public string GetCurrentDirectory() => Directory.GetCurrentDirectory();

    /// <inheritdoc />
    public bool IsWindows { get; } = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
}
