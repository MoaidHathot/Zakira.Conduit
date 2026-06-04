using Zakira.Conduit.Manifest;
using Zakira.Conduit.Paths;
using Zakira.Conduit.Strategies;

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
    private readonly IPlanStrategyRegistry _strategies;

    public ResolvedDestinationValidator(IPathResolver pathResolver, IPlanStrategyRegistry strategies)
    {
        ArgumentNullException.ThrowIfNull(pathResolver);
        ArgumentNullException.ThrowIfNull(strategies);
        _pathResolver = pathResolver;
        _strategies = strategies;
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
        var snapshot = StrategyConfigSnapshotBuilder.Build(manifest);

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

            IPlanStrategy strategy;
            try
            {
                strategy = _strategies.Resolve(entry.Strategy);
            }
            catch (UnknownStrategyException)
            {
                // Per-entry validator already surfaced this.
                continue;
            }

            IReadOnlyList<string> staticDests;
            try
            {
                var ctx = new StaticPlanContext(entry, manifestDir, _pathResolver, snapshot);
                staticDests = strategy.EnumerateStaticDestinations(ctx);
            }
            catch
            {
                continue;
            }

            foreach (var resolved in staticDests)
            {
                string normalised;
                try
                {
                    normalised = Normalise(Path.GetFullPath(resolved));
                }
                catch
                {
                    continue;
                }

                if (seen.TryGetValue(normalised, out var owner))
                {
                    if (!string.Equals(owner, entry.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        errors.Add(
                            $"entries[{i}].targets: resolved destination '{normalised}' is already produced by entry '{owner}'. " +
                            "Two entries cannot write into the same directory (after expanding '~', env vars, and relative paths); rename one (set 'name' or 'as').");
                    }
                }
                else
                {
                    seen[normalised] = entry.Name!;
                }
            }
        }

        return errors;
    }

    private static string Normalise(string fullPath) =>
        fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
