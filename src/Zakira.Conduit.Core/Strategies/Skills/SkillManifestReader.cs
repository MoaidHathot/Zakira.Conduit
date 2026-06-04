using System.Text;

namespace Zakira.Conduit.Strategies.Skills;

/// <summary>
///     Parses the YAML frontmatter at the top of a <c>SKILL.md</c> file.
///     The full YAML spec is intentionally out of scope: only the leading
///     <c>---</c>-delimited block is consumed, and only the <c>name:</c> and
///     <c>description:</c> top-level scalar keys are extracted. Other keys
///     are tolerated (skipped) so richer skills don't fail to parse.
/// </summary>
/// <remarks>
///     <para>
///         The parser handles both quoted (<c>name: "foo"</c>,
///         <c>name: 'foo'</c>) and bare (<c>name: foo</c>) scalar values.
///         Lines starting with <c>#</c> are treated as comments and skipped.
///         Lines whose first non-whitespace character is something other
///         than a key are silently skipped (so nested YAML blocks don't trip
///         the parser).
///     </para>
///     <para>
///         A file without a leading <c>---</c> line is treated as having no
///         frontmatter; the reader returns <see langword="null"/>.
///     </para>
/// </remarks>
public static class SkillManifestReader
{
    /// <summary>
    ///     Reads <paramref name="skillMdPath"/> and returns the extracted
    ///     frontmatter, or <see langword="null"/> when the file has no
    ///     leading <c>---</c> block.
    /// </summary>
    /// <exception cref="IOException">If the file cannot be read.</exception>
    public static SkillFrontmatter? Read(string skillMdPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(skillMdPath);

        using var stream = new FileStream(skillMdPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        var firstLine = reader.ReadLine();
        if (firstLine is null || firstLine.Trim() != "---")
        {
            return null;
        }

        string? name = null;
        string? description = null;

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var trimmed = line.TrimEnd();

            if (trimmed.Trim() == "---")
            {
                break;
            }

            // Skip blank lines and comments.
            var sansLeading = trimmed.TrimStart();
            if (sansLeading.Length == 0 || sansLeading[0] == '#')
            {
                continue;
            }

            // Only consider top-level (no indent) scalar keys; nested YAML
            // (e.g. "metadata:" with indented children) is ignored.
            if (trimmed.Length != sansLeading.Length)
            {
                continue;
            }

            var colon = sansLeading.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            var key = sansLeading[..colon].Trim();
            var value = sansLeading[(colon + 1)..].Trim();

            // Drop inline trailing comment, but only when the comment is
            // outside of any quoted span.
            value = StripTrailingComment(value);

            // Unquote.
            value = Unquote(value);

            switch (key)
            {
                case "name" when name is null:
                    if (value.Length > 0)
                    {
                        name = value;
                    }
                    break;

                case "description" when description is null:
                    if (value.Length > 0)
                    {
                        description = value;
                    }
                    break;
            }
        }

        if (name is null && description is null)
        {
            // We saw a frontmatter block but didn't recognise any of our
            // keys. Return a synthetic value so the caller knows the block
            // existed (versus "no frontmatter at all").
            return new SkillFrontmatter(Name: null, Description: null);
        }

        return new SkillFrontmatter(name, description);
    }

    private static string StripTrailingComment(string value)
    {
        if (value.Length == 0)
        {
            return value;
        }

        var inSingle = false;
        var inDouble = false;

        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];

            if (c == '\'' && !inDouble)
            {
                inSingle = !inSingle;
                continue;
            }

            if (c == '"' && !inSingle)
            {
                inDouble = !inDouble;
                continue;
            }

            if (c == '#' && !inSingle && !inDouble)
            {
                // Strip from the '#' onwards, but only when preceded by a
                // whitespace character (so URLs like "https://..." don't
                // get truncated).
                if (i == 0 || char.IsWhiteSpace(value[i - 1]))
                {
                    return value[..i].TrimEnd();
                }
            }
        }

        return value;
    }

    private static string Unquote(string value)
    {
        if (value.Length < 2)
        {
            return value;
        }

        var first = value[0];
        var last = value[^1];

        if ((first == '"' && last == '"') || (first == '\'' && last == '\''))
        {
            return value[1..^1];
        }

        return value;
    }
}
