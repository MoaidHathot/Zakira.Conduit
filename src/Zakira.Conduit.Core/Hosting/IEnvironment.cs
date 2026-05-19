namespace Zakira.Conduit.Hosting;

/// <summary>
///     Abstraction over the host environment (env vars, home directory, cwd).
///     Exists so manifest discovery can be unit-tested deterministically.
/// </summary>
public interface IEnvironment
{
    /// <summary>Returns the value of <paramref name="name"/> or <see langword="null"/>.</summary>
    string? GetEnvironmentVariable(string name);

    /// <summary>Returns the user's home directory.</summary>
    string GetHomeDirectory();

    /// <summary>Returns the current working directory.</summary>
    string GetCurrentDirectory();

    /// <summary>Returns <see langword="true"/> if running on Windows.</summary>
    bool IsWindows { get; }
}
