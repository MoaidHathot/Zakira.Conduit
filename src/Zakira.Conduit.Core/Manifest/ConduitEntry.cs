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
    ///     <para>
    ///         Optional in the manifest. When omitted, the
    ///         <see cref="Sources.Inference.SkillSourceInferenceCoordinator"/>
    ///         derives a default name from the source (GitHub/AzDO repo name,
    ///         local directory basename, etc.). May also be supplied indirectly
    ///         via the in-string arrow shorthand
    ///         (<c>"https://github.com/owner/repo -&gt; MyName"</c>) or the
    ///         object wrapper form (<c>{ "source": ..., "as": "MyName" }</c>);
    ///         in both cases the supplied alias becomes the entry name.
    ///     </para>
    ///     <para>
    ///         After the coordinator runs every entry is guaranteed to have a
    ///         non-empty <see cref="Name"/>; if no name can be derived the
    ///         <see cref="ManifestValidator"/> errors with a clear message.
    ///     </para>
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>
    ///     The remote source this entry mirrors. The concrete type is
    ///     determined by the polymorphic JSON discriminator.
    ///     <para>
    ///         For ergonomics the JSON value may also be a bare string
    ///         (e.g. <c>"https://github.com/owner/repo"</c> or <c>"./skills"</c>),
    ///         in which case it is deserialised as a
    ///         <see cref="UriBasedSkillSource"/> and resolved to its concrete
    ///         kind by the inference coordinator at manifest load time.
    ///     </para>
    /// </summary>
    [JsonPropertyName("source")]
    [JsonConverter(typeof(SourceShorthandJsonConverter))]
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

    /// <summary>
    ///     The resolved entry name. Equivalent to <see cref="Name"/> but
    ///     guarantees a non-null value, which is upheld by the inference
    ///     coordinator (it fills <see cref="Name"/> from the source or alias
    ///     before the validator runs). Throws if accessed on a raw
    ///     pre-inference entry whose <see cref="Name"/> is still null.
    /// </summary>
    [JsonIgnore]
    public string ResolvedName => Name
        ?? throw new InvalidOperationException(
            "ConduitEntry.Name is not resolved. The inference coordinator must run on the manifest before downstream code reads ResolvedName.");
}
