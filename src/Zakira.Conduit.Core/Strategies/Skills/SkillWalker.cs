namespace Zakira.Conduit.Strategies.Skills;

/// <summary>
///     Discovers <c>SKILL.md</c>-bearing folders inside fetched content. The
///     walker is layout-agnostic: a skill is any directory whose
///     immediate-child files include one named <c>SKILL.md</c> (case-sensitive
///     on Linux, case-insensitive on Windows/macOS) and whose own basename
///     doesn't start with a dot.
/// </summary>
/// <remarks>
///     <para>
///         The walk descends into every sub-directory unless the directory
///         is itself a skill folder &mdash; in that case sub-folders are
///         <i>not</i> walked further (skills are leaf containers; nested
///         skills would be ambiguous to mirror).
///     </para>
///     <para>
///         Hidden directories (basename starts with <c>.</c>) and a small set
///         of well-known scaffolding folders (<c>node_modules</c>,
///         <c>bin</c>, <c>obj</c>) are skipped to keep the walk fast on
///         repos that happen to contain a stray <c>SKILL.md</c> in deps.
///     </para>
/// </remarks>
public static class SkillWalker
{
    /// <summary>The well-known filename that identifies a skill folder.</summary>
    public const string SkillManifestFileName = "SKILL.md";

    private static readonly HashSet<string> SkipDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules",
        "bin",
        "obj",
        ".git",
    };

    /// <summary>
    ///     Walks <paramref name="rootDirectory"/> and yields one
    ///     <see cref="SkillDescriptor"/> per discovered skill. When
    ///     <paramref name="explicitNames"/> is non-null and non-empty, only
    ///     skills whose basename matches one of the entries are returned;
    ///     other skills are silently skipped.
    /// </summary>
    /// <param name="rootDirectory">Absolute path to a fetched content directory.</param>
    /// <param name="explicitNames">Optional name filter (case-insensitive). Null or empty = no filter.</param>
    /// <param name="originContentDirectory">
    ///     The fetched content directory the walk is rooted at; recorded on
    ///     each descriptor for downstream provenance.
    /// </param>
    public static IEnumerable<SkillDescriptor> Walk(
        string rootDirectory,
        IReadOnlyCollection<string>? explicitNames,
        string originContentDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);

        if (!Directory.Exists(rootDirectory))
        {
            yield break;
        }

        HashSet<string>? filter = null;
        if (explicitNames is { Count: > 0 })
        {
            filter = new HashSet<string>(explicitNames, StringComparer.OrdinalIgnoreCase);
        }

        var fullRoot = Path.GetFullPath(rootDirectory);
        var fullOrigin = Path.GetFullPath(originContentDirectory);

        var stack = new Stack<string>();
        stack.Push(fullRoot);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            string[] childDirs;
            string[] childFiles;

            try
            {
                childDirs = Directory.GetDirectories(current);
                childFiles = Directory.GetFiles(current);
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            var hasSkillMd = childFiles.Any(f => string.Equals(Path.GetFileName(f), SkillManifestFileName,
                OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal));

            if (hasSkillMd)
            {
                // The current directory IS a skill. Don't descend further:
                // skills are leaf containers in our model.
                var name = Path.GetFileName(current);
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                if (filter is not null && !filter.Contains(name))
                {
                    continue;
                }

                SkillFrontmatter? fm = null;
                var skillMdPath = childFiles.First(f => string.Equals(Path.GetFileName(f), SkillManifestFileName,
                    OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                        ? StringComparison.OrdinalIgnoreCase
                        : StringComparison.Ordinal));

                try
                {
                    fm = SkillManifestReader.Read(skillMdPath);
                }
                catch (IOException)
                {
                    // Treat unreadable frontmatter as absent; the skill is
                    // still mirrored.
                }

                yield return new SkillDescriptor(
                    SkillDirectory: current,
                    Name: name,
                    Frontmatter: fm,
                    OriginContentDirectory: fullOrigin);

                continue;
            }

            foreach (var child in childDirs)
            {
                var childName = Path.GetFileName(child);
                if (string.IsNullOrWhiteSpace(childName))
                {
                    continue;
                }

                if (childName.StartsWith('.'))
                {
                    continue;
                }

                if (SkipDirectoryNames.Contains(childName))
                {
                    continue;
                }

                stack.Push(child);
            }
        }
    }
}
