using Microsoft.Extensions.FileSystemGlobbing;

namespace Zakira.Conduit.Mirroring;

/// <summary>
///     File-relative-path filter used by <see cref="IDirectoryMirror"/> to
///     decide whether a given file in a fetched source directory should be
///     mirrored into the target. Built from a manifest entry's optional
///     <c>include</c> / <c>exclude</c> pattern lists.
/// </summary>
/// <remarks>
///     <para>
///         Patterns use the
///         <see cref="Microsoft.Extensions.FileSystemGlobbing.Matcher"/>
///         dialect (the same one used by ASP.NET Core's static-file middleware):
///         <c>*</c>, <c>**</c>, <c>?</c> and bracket classes. Negation
///         (<c>!pattern</c>, gitignore-style) is <b>not</b> supported &mdash;
///         use <see cref="Excludes"/> for exclusions instead.
///     </para>
///     <para>
///         A path is mirrored when both:
///         <list type="bullet">
///             <item><description>at least one entry in <see cref="Includes"/> matches it (or <see cref="Includes"/> is empty, meaning "include everything"), and</description></item>
///             <item><description>zero entries in <see cref="Excludes"/> match it.</description></item>
///         </list>
///     </para>
///     <para>
///         Paths are normalised to forward slashes and made relative to the
///         source-content root before being matched, so patterns are portable
///         across operating systems.
///     </para>
/// </remarks>
public sealed class MirrorFilter
{
    /// <summary>The all-pass filter used when no include/exclude is configured.</summary>
    public static MirrorFilter MatchEverything { get; } = new(null, null);

    private readonly Matcher? _matcher;

    /// <summary>
    ///     Creates a filter from raw include/exclude pattern lists. Null or
    ///     empty lists mean "no constraint" for that side.
    /// </summary>
    public MirrorFilter(IReadOnlyList<string>? includes, IReadOnlyList<string>? excludes)
    {
        Includes = includes ?? Array.Empty<string>();
        Excludes = excludes ?? Array.Empty<string>();

        if (Includes.Count == 0 && Excludes.Count == 0)
        {
            _matcher = null;
            return;
        }

        var matcher = new Matcher(StringComparison.Ordinal);
        if (Includes.Count == 0)
        {
            matcher.AddInclude("**/*");
        }
        else
        {
            foreach (var p in Includes)
            {
                matcher.AddInclude(p);
            }
        }

        foreach (var p in Excludes)
        {
            matcher.AddExclude(p);
        }

        _matcher = matcher;
    }

    /// <summary>The raw include patterns. Empty means "include everything".</summary>
    public IReadOnlyList<string> Includes { get; }

    /// <summary>The raw exclude patterns. Empty means "exclude nothing".</summary>
    public IReadOnlyList<string> Excludes { get; }

    /// <summary>Returns <see langword="true"/> when this filter never rejects a path.</summary>
    public bool MatchesEverything => _matcher is null;

    /// <summary>
    ///     Returns <see langword="true"/> when <paramref name="relativePath"/>
    ///     should be mirrored. <paramref name="relativePath"/> is expected to
    ///     use forward slashes and be relative to the source content root.
    /// </summary>
    public bool ShouldInclude(string relativePath)
    {
        if (_matcher is null)
        {
            return true;
        }

        // Matcher.Match expects paths to be enumerated under a "root"; for a
        // single path the easiest API is the InMemoryDirectoryInfo helper.
        var result = _matcher.Match(relativePath);
        return result.HasMatches;
    }
}
