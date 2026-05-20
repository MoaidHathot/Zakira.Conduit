using System.Text.Json.Serialization;

namespace Zakira.Conduit.Manifest;

/// <summary>
///     A skill source backed by one or more directories on the local filesystem.
///     <para>
///         Paths may be absolute, or relative to the manifest's directory, and
///         support <c>~</c> and environment-variable expansion.
///     </para>
///     <para>
///         <list type="bullet">
///             <item><description>If exactly one path resolves, it is mirrored to <c>&lt;target&gt;/&lt;entry.name&gt;/</c>.</description></item>
///             <item><description>If two or more paths resolve, each is mirrored to <c>&lt;target&gt;/&lt;basename(path)&gt;/</c>; the entry name drops out of the destination.</description></item>
///         </list>
///     </para>
/// </summary>
public sealed record LocalDirectorySkillSource : ISkillSource
{
    /// <summary>
    ///     The JSON discriminator value for this source kind (<c>"local"</c>).
    /// </summary>
    public const string TypeDiscriminator = "local";

    /// <summary>
    ///     Single source directory. Syntactic sugar for a one-element
    ///     <see cref="Paths"/>. Mutually exclusive with <see cref="Paths"/>.
    /// </summary>
    [JsonPropertyName("path")]
    public string? Path { get; init; }

    /// <summary>
    ///     One or more source directories. Mutually exclusive with <see cref="Path"/>.
    ///     Each element may be a string or an object with <c>path</c> + optional
    ///     <c>as</c> alias.
    /// </summary>
    [JsonPropertyName("paths")]
    public IReadOnlyList<PathSpec>? Paths { get; init; }

    /// <inheritdoc />
    [JsonIgnore]
    public string Kind => TypeDiscriminator;

    /// <summary>
    ///     Effective list of source directories. Returns <see cref="Paths"/>
    ///     when set, otherwise a one-element list containing <see cref="Path"/>,
    ///     otherwise an empty list (which is a validation error).
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<PathSpec> EffectivePaths =>
        Paths is { Count: > 0 } p
            ? p
            : (string.IsNullOrWhiteSpace(Path) ? Array.Empty<PathSpec>() : new PathSpec[] { new(Path) });
}
