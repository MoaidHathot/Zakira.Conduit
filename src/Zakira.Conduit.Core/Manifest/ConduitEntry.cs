using System.Text.Json.Serialization;

namespace Zakira.Conduit.Manifest;

/// <summary>
///     A single entry in a conduit manifest: one remote source, mirrored to
///     one or more local target directories.
/// </summary>
public sealed record ConduitEntry
{
    /// <summary>
    ///     A unique, human-friendly identifier for this entry. Used both for
    ///     logging and as the sub-directory name inside each target where the
    ///     mirrored content is placed (i.e. each target receives a
    ///     <c>&lt;target&gt;/&lt;name&gt;/</c> directory).
    /// </summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>
    ///     The remote source this entry mirrors. The concrete type is
    ///     determined by the polymorphic JSON discriminator.
    /// </summary>
    [JsonPropertyName("source")]
    public required ISkillSource Source { get; init; }

    /// <summary>
    ///     One or more local target directories into which a sub-directory
    ///     named <see cref="Name"/> will be created (or replaced) with the
    ///     mirrored content. Paths may include <c>~</c> and environment
    ///     variables such as <c>$XDG_CONFIG_HOME</c>.
    ///     <para>
    ///         Each entry in the array can be either a plain string (the
    ///         common case) or an object with <c>path</c> and an optional
    ///         <c>as</c> alias that overrides <see cref="Name"/> for that
    ///         target only. The aliased form is rejected for multi-content
    ///         entries by <see cref="ManifestValidator"/>.
    ///     </para>
    /// </summary>
    [JsonPropertyName("targets")]
    public required IReadOnlyList<PathSpec> Targets { get; init; }

    /// <summary>
    ///     Optional, free-form description for documentation purposes.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>
    ///     If <see langword="true"/>, the entry is skipped during sync.
    /// </summary>
    [JsonPropertyName("disabled")]
    public bool Disabled { get; init; }
}
