using Zakira.Conduit.Manifest;
using Zakira.Conduit.Paths;

namespace Zakira.Conduit.Manifest;

/// <summary>
///     Second-pass validator that catches cross-entry destination collisions
///     which the pure <see cref="ManifestValidator"/> misses because it works
///     on raw target strings rather than resolved absolute paths. Two
///     entries pointing at, say, <c>~/foo</c> and <c>$HOME/foo</c> look
///     distinct lexically but collide once <see cref="IPathResolver.Resolve"/>
///     turns them into absolute paths. This validator runs after manifest
///     load and reports those collisions with the same hard-error shape as
///     the static checks.
/// </summary>
public sealed class ResolvedDestinationValidator
{
    private readonly IPathResolver _pathResolver;

    public ResolvedDestinationValidator(IPathResolver pathResolver)
    {
        ArgumentNullException.ThrowIfNull(pathResolver);
        _pathResolver = pathResolver;
    }

    /// <summary>
    ///     Returns the list of resolved-path collision errors. Empty when no
    ///     two entries resolve to the same destination directory.
    /// </summary>
    /// <remarks>
    ///     Errors returned here are deliberately keyed on resolved
    ///     absolute paths, so they will not double-report a collision the
    ///     static <see cref="ManifestValidator"/> already caught (the static
    ///     check uses raw input strings as keys, so its messages reference
    ///     the as-written path; this validator references the resolved one).
    /// </remarks>
    public IReadOnlyList<string> Validate(ConduitManifest manifest, string manifestPath)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);

        var manifestDir = Path.GetDirectoryName(Path.GetFullPath(manifestPath)) ?? Directory.GetCurrentDirectory();
        var comparer = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

        // Map: resolved-destination-path -> first-seen owning entry name.
        var seen = new Dictionary<string, string>(comparer);
        var errors = new List<string>();

        for (var i = 0; i < manifest.Entries.Count; i++)
        {
            var entry = manifest.Entries[i];
            if (string.IsNullOrWhiteSpace(entry.Name) || entry.Targets is null || entry.Targets.Count == 0)
            {
                continue;
            }

            foreach (var resolved in EnumerateResolvedDestinations(entry, manifestDir))
            {
                if (seen.TryGetValue(resolved, out var owner))
                {
                    // Only emit when the OWNING entry-name differs from this one;
                    // a single entry repeating itself in `targets` is caught
                    // elsewhere.
                    if (!string.Equals(owner, entry.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        errors.Add(
                            $"entries[{i}].targets: resolved destination '{resolved}' is already produced by entry '{owner}'. " +
                            "Two entries cannot write into the same directory (after expanding '~', env vars, and relative paths); rename one (set 'name' or 'as').");
                    }
                }
                else
                {
                    seen[resolved] = entry.Name!;
                }
            }
        }

        return errors;
    }

    /// <summary>
    ///     Enumerates each entry's resolved destination directories using the
    ///     same name/alias rules the synchronizer follows. Path strings are
    ///     full-pathed so equivalent input strings collapse to the same key.
    /// </summary>
    private IEnumerable<string> EnumerateResolvedDestinations(ConduitEntry entry, string manifestDir)
    {
        var multiUnitDestNames = entry.Source switch
        {
            GitHubSource gh when gh.EffectivePaths.Count > 1 => gh.EffectivePaths.Select(p => p.ResolvedBasename).ToList(),
            AzdoSource azdo when azdo.EffectivePaths.Count > 1 => azdo.EffectivePaths.Select(p => p.ResolvedBasename).ToList(),
            LocalDirectorySource local when local.EffectivePaths.Count > 1 => local.EffectivePaths.Select(p => p.ResolvedBasename).ToList(),
            _ => null,
        };

        foreach (var target in entry.Targets)
        {
            if (target is null || string.IsNullOrWhiteSpace(target.Path))
            {
                continue;
            }

            string resolvedParent;
            try
            {
                resolvedParent = _pathResolver.Resolve(target.Path, manifestDir);
            }
            catch
            {
                // If the path resolver throws on a malformed input, skip; the
                // static validator (or the synchronizer at run-time) will
                // surface a more specific error.
                continue;
            }

            if (multiUnitDestNames is not null)
            {
                foreach (var unitName in multiUnitDestNames)
                {
                    var combined = Path.GetFullPath(Path.Combine(resolvedParent, unitName));
                    yield return Normalise(combined);
                }
            }
            else
            {
                var destName = string.IsNullOrWhiteSpace(target.As) ? entry.Name : target.As;
                if (!string.IsNullOrWhiteSpace(destName))
                {
                    var combined = Path.GetFullPath(Path.Combine(resolvedParent, destName!));
                    yield return Normalise(combined);
                }
            }
        }
    }

    private static string Normalise(string fullPath) =>
        fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
