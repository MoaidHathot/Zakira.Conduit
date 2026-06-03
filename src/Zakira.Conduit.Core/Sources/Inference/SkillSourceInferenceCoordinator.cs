using Zakira.Conduit.Manifest;

namespace Zakira.Conduit.Sources.Inference;

/// <summary>
///     Walks every registered <see cref="ISkillSourceInferrer"/> until one
///     recognises the URI, then returns the concrete <see cref="ISkillSource"/>.
///     Also rewrites a whole <see cref="ConduitManifest"/> in one pass so that
///     downstream code (validator, synchronizer, fetchers) only ever sees
///     concrete sources and entries with a populated <see cref="ConduitEntry.Name"/>.
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
    ///         <item><description>every <see cref="UriBasedSkillSource"/> entry has been replaced with its inferred concrete source,</description></item>
    ///         <item><description>every <see cref="AliasedSkillSource"/> wrapper has been unwrapped and its alias applied to the owning entry's name,</description></item>
    ///         <item><description>every entry whose source is an <see cref="ArraySkillSource"/> has been expanded into N independent entries (one per array element), and</description></item>
    ///         <item><description>every resulting entry has <see cref="ConduitEntry.Name"/> populated (from the user-supplied <c>name</c>, an explicit alias, or a source-derived default).</description></item>
    ///     </list>
    ///     Entries whose source is already a concrete kind (and whose name is
    ///     already supplied) pass through unchanged.
    /// </summary>
    public ConduitManifest Rewrite(ConduitManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var newEntries = new List<ConduitEntry>(manifest.Entries.Count);
        for (var i = 0; i < manifest.Entries.Count; i++)
        {
            var entry = manifest.Entries[i];

            // Step 1: strip any outermost AliasedSkillSource wrapper, recording
            // the alias so we can use it as a default for entry.Name later.
            var (unwrappedSource, topAlias) = UnwrapAlias(entry.Source, $"entries[{i}] ('{entry.Name ?? "<unnamed>"}').source");

            // Step 2: if the unwrapped source is an array, expand it now.
            // The parent name (used to prefix element names) is the user's
            // explicit entry.Name when set, otherwise the wrapper alias if any,
            // otherwise null (elements stand on their own).
            if (unwrappedSource is ArraySkillSource arr)
            {
                var parentName = SanitizeOrNull(entry.Name) ?? SanitizeOrNull(topAlias);
                AppendExpandedEntries(newEntries, entry, arr, i, parentName);
                continue;
            }

            // Step 3: scalar. Infer concrete kind if needed, then fill name.
            var concrete = ResolveScalarSource(unwrappedSource, i, entry.Name);
            var resolvedName = SanitizeOrNull(entry.Name)
                               ?? SanitizeOrNull(topAlias)
                               ?? SanitizeOrNull(DefaultSourceNameDeriver.Derive(concrete));

            newEntries.Add(entry with
            {
                Source = concrete,
                Name = resolvedName,
            });
        }

        return manifest with { Entries = newEntries };
    }

    private void AppendExpandedEntries(
        List<ConduitEntry> sink,
        ConduitEntry entry,
        ArraySkillSource array,
        int index,
        string? parentName)
    {
        // Reject per-target 'as' aliases on multi-element entries: aliases
        // wouldn't apply cleanly to N destinations (same constraint the
        // existing multi-paths feature applies).
        if (entry.Targets.Any(t => !string.IsNullOrWhiteSpace(t.As)))
        {
            throw new SkillSourceInferenceException(
                $"entries[{index}] ('{entry.Name ?? "<unnamed>"}'): per-target 'as' aliases are not allowed on entries whose source is an array.");
        }

        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var j = 0; j < array.Elements.Count; j++)
        {
            // Unwrap a per-element alias (in-string arrow or wrapper-object form)
            // before resolving the scalar source.
            var (innerElement, elementAlias) = UnwrapAlias(
                array.Elements[j],
                $"entries[{index}] ('{entry.Name ?? "<unnamed>"}').source[{j}]");

            ISkillSource concreteElement;
            try
            {
                concreteElement = ResolveScalarSource(innerElement, index, entry.Name, elementIndex: j);
            }
            catch (SkillSourceInferenceException ex)
            {
                throw new SkillSourceInferenceException(
                    $"entries[{index}] ('{entry.Name ?? "<unnamed>"}').source[{j}]: {ex.Message}", ex);
            }

            // Pick the per-element identity: alias wins over source-derived,
            // which wins over a positional fallback.
            var baseName = SanitizeOrNull(elementAlias)
                           ?? SanitizeOrNull(DefaultSourceNameDeriver.Derive(concreteElement))
                           ?? $"element-{j}";

            // Combine with the parent name (when set) so logs and state stay
            // grouped; without a parent the element name stands on its own.
            var elementName = parentName is null
                ? SanitizeName(baseName)
                : SanitizeName($"{parentName}-{baseName}");

            if (!usedNames.Add(elementName))
            {
                elementName = SanitizeName($"{elementName}-{j}");
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

    /// <summary>
    ///     Strips an outermost <see cref="AliasedSkillSource"/> wrapper (if
    ///     present), returning the inner source plus the alias the wrapper
    ///     carried. Non-wrapper sources pass through unchanged with no alias.
    /// </summary>
    private static (ISkillSource Inner, string? Alias) UnwrapAlias(ISkillSource source, string locationForErrors)
    {
        if (source is null)
        {
            throw new SkillSourceInferenceException($"{locationForErrors}: source must not be null.");
        }

        if (source is AliasedSkillSource aliased)
        {
            // The converter already rejects wrappers-around-wrappers and
            // wrappers-around-arrays, but defend against direct construction.
            if (aliased.Inner is AliasedSkillSource)
            {
                throw new SkillSourceInferenceException(
                    $"{locationForErrors}: aliased wrappers must not be nested.");
            }

            return (aliased.Inner, aliased.As);
        }

        return (source, null);
    }

    private ISkillSource ResolveScalarSource(ISkillSource source, int entryIndex, string? entryName, int? elementIndex = null)
    {
        return source switch
        {
            UriBasedSkillSource uri => InferWithContext(uri, entryIndex, entryName, elementIndex),
            ArraySkillSource => throw new SkillSourceInferenceException("nested 'source' arrays are not supported."),
            AliasedSkillSource => throw new SkillSourceInferenceException("aliased wrappers must be unwrapped before resolving."),
            _ => source,
        };
    }

    private ISkillSource InferWithContext(UriBasedSkillSource uri, int entryIndex, string? entryName, int? elementIndex)
    {
        try
        {
            return Infer(uri);
        }
        catch (SkillSourceInferenceException ex)
        {
            var location = elementIndex is null
                ? $"entries[{entryIndex}] ('{entryName ?? "<unnamed>"}')"
                : $"entries[{entryIndex}] ('{entryName ?? "<unnamed>"}').source[{elementIndex}]";
            throw new SkillSourceInferenceException($"{location}: {ex.Message}", ex);
        }
    }

    private static string? SanitizeOrNull(string? raw) =>
        string.IsNullOrWhiteSpace(raw) ? null : SanitizeName(raw);

    private static string SanitizeName(string raw)
    {
        // Entry names are [A-Za-z0-9._-]. Replace anything else with '-' and
        // collapse runs of '-' so a name like "owner/repo name" becomes
        // "owner-repo-name" rather than "owner-repo--name".
        var chars = raw.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            var c = chars[i];
            var ok = (c >= 'A' && c <= 'Z')
                     || (c >= 'a' && c <= 'z')
                     || (c >= '0' && c <= '9')
                     || c == '.' || c == '_' || c == '-';
            if (!ok)
            {
                chars[i] = '-';
            }
        }

        // Collapse runs of '-' and trim.
        var sb = new System.Text.StringBuilder(chars.Length);
        var prevDash = false;
        foreach (var c in chars)
        {
            if (c == '-')
            {
                if (prevDash)
                {
                    continue;
                }

                prevDash = true;
            }
            else
            {
                prevDash = false;
            }

            sb.Append(c);
        }

        var result = sb.ToString().Trim('-');
        return string.IsNullOrEmpty(result) ? "entry" : result;
    }
}
