namespace Zakira.Conduit.Paths;

/// <summary>
///     Resolves path-like manifest values, expanding <c>~</c>, environment
///     variables (<c>$VAR</c> and <c>${VAR}</c> on every platform, <c>%VAR%</c>
///     on Windows), and rooting relative paths against a base directory.
/// </summary>
public interface IPathResolver
{
    /// <summary>
    ///     Expands and absolutizes <paramref name="value"/>. <paramref name="basePath"/>
    ///     is used to root relative paths.
    /// </summary>
    string Resolve(string value, string basePath);
}
