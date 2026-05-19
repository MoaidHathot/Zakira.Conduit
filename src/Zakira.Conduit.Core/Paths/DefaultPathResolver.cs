using System.Text.RegularExpressions;
using Zakira.Conduit.Hosting;

namespace Zakira.Conduit.Paths;

/// <summary>
///     Default <see cref="IPathResolver"/>. Pure logic that delegates only to
///     <see cref="IEnvironment"/> for env-var lookups and home directory.
/// </summary>
public sealed partial class DefaultPathResolver : IPathResolver
{
    [GeneratedRegex(@"\$\{(?<n>[A-Za-z_][A-Za-z0-9_]*)\}|\$(?<n>[A-Za-z_][A-Za-z0-9_]*)")]
    private static partial Regex DollarVarRegex();

    private readonly IEnvironment _environment;

    public DefaultPathResolver(IEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        _environment = environment;
    }

    /// <inheritdoc />
    public string Resolve(string value, string basePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        ArgumentException.ThrowIfNullOrEmpty(basePath);

        var expanded = ExpandTilde(value);
        expanded = ExpandDollarVars(expanded);

        if (_environment.IsWindows)
        {
            expanded = Environment.ExpandEnvironmentVariables(expanded);
        }

        return Path.IsPathRooted(expanded) ? Path.GetFullPath(expanded) : Path.GetFullPath(expanded, basePath);
    }

    private string ExpandTilde(string value)
    {
        if (value.Length == 0 || value[0] != '~')
        {
            return value;
        }

        // Match POSIX shell behavior: only ~ at the start, followed by / or \ or end-of-string.
        if (value.Length == 1)
        {
            return _environment.GetHomeDirectory();
        }

        if (value[1] == '/' || value[1] == '\\')
        {
            return _environment.GetHomeDirectory() + value[1..];
        }

        return value;
    }

    private string ExpandDollarVars(string value) =>
        DollarVarRegex().Replace(value, match =>
        {
            var name = match.Groups["n"].Value;
            return _environment.GetEnvironmentVariable(name) ?? match.Value;
        });
}
