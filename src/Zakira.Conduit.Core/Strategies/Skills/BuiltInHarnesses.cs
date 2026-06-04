namespace Zakira.Conduit.Strategies.Skills;

/// <summary>
///     The built-in agent-harness registry. Each entry maps a harness name
///     to one or more relative search paths probed under the user's chosen
///     target. The first path found on disk is used; multiple matches
///     produce multiple destinations (one per match).
/// </summary>
/// <remarks>
///     <para>
///         OpenCode is the only harness with two search paths today: the
///         project-local <c>.opencode/skills/</c> convention and the global
///         <c>.config/opencode/skills/</c> (XDG-style) layout used when the
///         user targets <c>~</c>.
///     </para>
///     <para>
///         GitHub Copilot is intentionally absent: Copilot uses
///         <c>.github/copilot-instructions.md</c> and
///         <c>.github/instructions/*.instructions.md</c> &mdash; instruction
///         files, not skill folders &mdash; so the folder-based skills
///         strategy does not apply. A future <c>copilot-instructions</c>
///         strategy will handle that layout.
///     </para>
/// </remarks>
public static class BuiltInHarnesses
{
    /// <summary>
    ///     The default registry. Each value is a list of paths relative to
    ///     the user's chosen target; the harness "matches" when any of them
    ///     resolves to an existing directory.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Default { get; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["opencode"] = new[]
            {
                ".opencode/skills",
                ".config/opencode/skills",
            },
            ["claude"] = new[] { ".claude/skills" },
            ["codex"]  = new[] { ".codex/skills" },
            ["agents"] = new[] { ".agents/skills" },
        };
}
