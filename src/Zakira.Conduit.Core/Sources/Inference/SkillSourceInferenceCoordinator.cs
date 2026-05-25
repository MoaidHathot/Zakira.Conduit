using Zakira.Conduit.Manifest;

namespace Zakira.Conduit.Sources.Inference;

/// <summary>
///     Walks every registered <see cref="ISkillSourceInferrer"/> until one
///     recognises the URI, then returns the concrete <see cref="ISkillSource"/>.
///     Also rewrites a whole <see cref="ConduitManifest"/> in one pass.
/// </summary>
public sealed class SkillSourceInferenceCoordinator
{
    private readonly IReadOnlyList<ISkillSourceInferrer> _inferrers;

    public SkillSourceInferenceCoordinator(IEnumerable<ISkillSourceInferrer> inferrers)
    {
        ArgumentNullException.ThrowIfNull(inferrers);
        _inferrers = inferrers.ToArray();
    }

    /// <summary>Convenience for tests / one-off callers.</summary>
    public ISkillSource Infer(UriBasedSkillSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (string.IsNullOrWhiteSpace(source.Uri))
        {
            throw new SkillSourceInferenceException("Source 'uri' must be a non-empty string.");
        }

        foreach (var inferrer in _inferrers)
        {
            if (inferrer.CanHandle(source.Uri))
            {
                return inferrer.Infer(source);
            }
        }

        throw new SkillSourceInferenceException(
            $"No registered source kind could infer a type from uri '{source.Uri}'. " +
            $"Known kinds: {string.Join(", ", _inferrers.Select(i => i.Kind))}. " +
            "Use an explicit 'type' on the source if the URI shape isn't recognised.");
    }

    /// <summary>
    ///     Returns a new <see cref="ConduitManifest"/> in which:
    ///     <list type="bullet">
    ///         <item><description>every <see cref="UriBasedSkillSource"/> entry has been replaced with its inferred concrete source, and</description></item>
    ///         <item><description>every entry whose source is an <see cref="ArraySkillSource"/> has been expanded into N independent entries (one per array element).</description></item>
    ///     </list>
    ///     Entries whose source is already a concrete kind pass through unchanged.
    /// </summary>
    public ConduitManifest Rewrite(ConduitManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var newEntries = new List<ConduitEntry>(manifest.Entries.Count);
        for (var i = 0; i < manifest.Entries.Count; i++)
        {
            var entry = manifest.Entries[i];

            // Step 1: if this entry's source is an array, expand it now. Each
            // expanded entry is then processed individually for scalar inference.
            if (entry.Source is ArraySkillSource arr)
            {
                AppendExpandedEntries(newEntries, entry, arr, i);
                continue;
            }

            newEntries.Add(ResolveScalar(entry, i));
        }

        return manifest with { Entries = newEntries };
    }

    private void AppendExpandedEntries(List<ConduitEntry> sink, ConduitEntry entry, ArraySkillSource array, int index)
    {
        // Reject per-target 'as' aliases on multi-element entries: aliases
        // wouldn't apply cleanly to N destinations (same constraint the
        // existing multi-paths feature applies).
        if (entry.Targets.Any(t => !string.IsNullOrWhiteSpace(t.As)))
        {
            throw new SkillSourceInferenceException(
                $"entries[{index}] ('{entry.Name}'): per-target 'as' aliases are not allowed on entries whose source is an array.");
        }

        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var j = 0; j < array.Elements.Count; j++)
        {
            // Resolve each element to its concrete kind, then derive a unique
            // entry name for it. The base name follows the pattern
            // '<parent>/<element-basename>' so logs and state stay grouped.
            ISkillSource concreteElement;
            try
            {
                concreteElement = ResolveScalarSource(array.Elements[j]);
            }
            catch (SkillSourceInferenceException ex)
            {
                throw new SkillSourceInferenceException(
                    $"entries[{index}] ('{entry.Name}').source[{j}]: {ex.Message}", ex);
            }

            var baseName = DeriveElementName(concreteElement, fallback: $"element-{j}");
            var elementName = SanitizeName($"{entry.Name}-{baseName}");
            if (!usedNames.Add(elementName))
            {
                // Disambiguate collisions with the element index.
                elementName = SanitizeName($"{entry.Name}-{baseName}-{j}");
                usedNames.Add(elementName);
            }

            sink.Add(new ConduitEntry
            {
                Name = elementName,
                Description = entry.Description,
                Disabled = entry.Disabled,
                Source = concreteElement,
                Targets = entry.Targets,
            });
        }
    }

    private ConduitEntry ResolveScalar(ConduitEntry entry, int index)
    {
        if (entry.Source is not UriBasedSkillSource uri)
        {
            return entry;
        }

        try
        {
            var concrete = Infer(uri);
            return entry with { Source = concrete };
        }
        catch (SkillSourceInferenceException ex)
        {
            throw new SkillSourceInferenceException(
                $"entries[{index}] ('{entry.Name}'): {ex.Message}", ex);
        }
    }

    private ISkillSource ResolveScalarSource(ISkillSource source) => source switch
    {
        UriBasedSkillSource uri => Infer(uri),
        ArraySkillSource => throw new SkillSourceInferenceException("nested 'source' arrays are not supported."),
        _ => source,
    };

    /// <summary>
    ///     Picks a short, stable name suffix for an expanded element. Falls
    ///     back to <paramref name="fallback"/> when the source kind doesn't
    ///     expose anything obviously human-friendly.
    /// </summary>
    private static string DeriveElementName(ISkillSource source, string fallback) => source switch
    {
        GitHubSkillSource gh => DeriveFromPathOrName(gh.Path, gh.Paths, gh.RepoName),
        AzdoSkillSource azdo => DeriveFromPathOrName(azdo.Path, azdo.Paths, azdo.ResolvedComponents.Repo),
        LocalDirectorySkillSource local => DeriveLocalName(local) ?? fallback,
        _ => fallback,
    };

    private static string DeriveFromPathOrName(string? path, IReadOnlyList<PathSpec>? paths, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            return BasenameOf(path);
        }

        if (paths is { Count: > 0 })
        {
            return paths.Count == 1 ? paths[0].ResolvedBasename : fallback;
        }

        return fallback;
    }

    private static string? DeriveLocalName(LocalDirectorySkillSource local)
    {
        var paths = local.EffectivePaths;
        if (paths.Count == 1)
        {
            return BasenameOf(paths[0].Path);
        }

        return null;
    }

    private static string BasenameOf(string path)
    {
        var normalized = path.Replace('\\', '/').TrimEnd('/');
        var slash = normalized.LastIndexOf('/');
        return slash < 0 ? normalized : normalized[(slash + 1)..];
    }

    private static string SanitizeName(string raw)
    {
        // Entry names are [A-Za-z0-9._-]. Replace anything else with '-'.
        var chars = raw.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            var c = chars[i];
            if (!char.IsLetterOrDigit(c) && c is not ('.' or '_' or '-'))
            {
                chars[i] = '-';
            }
        }

        var result = new string(chars).Trim('-');
        return result.Length == 0 ? "entry" : result;
    }
}
