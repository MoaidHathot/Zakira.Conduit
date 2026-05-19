using System.Text.Json.Serialization;

namespace Zakira.Conduit.Manifest;

/// <summary>
///     A skill source backed by a directory on the local filesystem. The
///     directory is mirrored as-is into each target.
///     <para>
///         <see cref="Path"/> may be absolute, or relative to the manifest's
///         directory, and supports <c>~</c> and environment-variable expansion.
///     </para>
/// </summary>
public sealed record LocalDirectorySkillSource : ISkillSource
{
    /// <summary>
    ///     The JSON discriminator value for this source kind (<c>"local"</c>).
    /// </summary>
    public const string TypeDiscriminator = "local";

    /// <summary>
    ///     The local directory whose contents are mirrored. Required.
    /// </summary>
    [JsonPropertyName("path")]
    public required string Path { get; init; }

    /// <inheritdoc />
    [JsonIgnore]
    public string Kind => TypeDiscriminator;
}
