namespace Zakira.Conduit.Strategies.Skills;

/// <summary>
///     A discovered skill: its containing folder, the basename (matching the
///     <c>SKILL.md</c> frontmatter <c>name:</c>), and a snapshot of the
///     parsed frontmatter when present.
/// </summary>
/// <param name="SkillDirectory">
///     Absolute path to the directory that contains the <c>SKILL.md</c>.
///     This is the directory the mirror should copy from.
/// </param>
/// <param name="Name">
///     The skill name, derived from <see cref="SkillDirectory"/>'s basename.
///     Must match the <c>name:</c> field in <see cref="Frontmatter"/> when
///     the frontmatter is present.
/// </param>
/// <param name="Frontmatter">
///     The parsed frontmatter, or <see langword="null"/> when the
///     <c>SKILL.md</c> has no <c>---</c> block at the top.
/// </param>
/// <param name="OriginContentDirectory">
///     The fetched content root the skill was discovered under. Recorded so
///     downstream reporting can correlate skills with their source.
/// </param>
public sealed record SkillDescriptor(
    string SkillDirectory,
    string Name,
    SkillFrontmatter? Frontmatter,
    string OriginContentDirectory);

/// <summary>
///     The handful of frontmatter fields Conduit cares about. The
///     <c>SKILL.md</c> may contain richer YAML; only these properties are
///     extracted (the rest is left in the file for downstream agents to read).
/// </summary>
/// <param name="Name">The skill's canonical name. Must match the folder basename.</param>
/// <param name="Description">Optional human-readable summary.</param>
public sealed record SkillFrontmatter(string? Name, string? Description);
