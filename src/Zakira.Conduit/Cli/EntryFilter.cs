namespace Zakira.Conduit.Cli;

/// <summary>
///     Normalises the values collected by the <c>--entry</c> / <c>-e</c>
///     option across commands. <c>--entry a --entry b</c>,
///     <c>--entry a,b</c>, and <c>--entry " a , b , "</c> all produce the
///     same set <c>{ "a", "b" }</c>.
/// </summary>
internal static class EntryFilter
{
    /// <summary>
    ///     Expands every supplied value by splitting on commas, trimming
    ///     whitespace, and dropping empties. The result preserves the
    ///     original first-seen order of names so log output stays stable.
    /// </summary>
    public static string[] Normalise(string[]? raw)
    {
        if (raw is null || raw.Length == 0)
        {
            return Array.Empty<string>();
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var ordered = new List<string>(raw.Length);

        foreach (var value in raw)
        {
            if (value is null)
            {
                continue;
            }

            foreach (var part in value.Split(','))
            {
                var trimmed = part.Trim();
                if (trimmed.Length == 0)
                {
                    continue;
                }

                if (seen.Add(trimmed))
                {
                    ordered.Add(trimmed);
                }
            }
        }

        return ordered.ToArray();
    }
}
