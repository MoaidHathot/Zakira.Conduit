using System.Text.Json.Serialization;

namespace Zakira.Conduit.Manifest;

/// <summary>
///     A transient source representation produced when the JSON value of
///     <c>source</c> is an array. Each element is itself a source &mdash;
///     either an inline scalar (which became a <see cref="UriBasedSource"/>),
///     or an inline object (a concrete <see cref="ISource"/>).
///     <para>
///         This record never reaches the synchronizer: the inference
///         coordinator expands every entry whose source is an
///         <see cref="ArraySource"/> into N independent entries, one
///         per element. Element names are derived from the parent entry name
///         and an element-specific basename, so each element gets its own
///         row in <c>conduit list</c> and its own record in the state file.
///     </para>
/// </summary>
public sealed record ArraySource : ISource
{
    /// <summary>The JSON discriminator value (<c>"array"</c>).</summary>
    public const string TypeDiscriminator = "array";

    /// <summary>The array elements, in declaration order.</summary>
    public required IReadOnlyList<ISource> Elements { get; init; }

    /// <inheritdoc />
    [JsonIgnore]
    public string Kind => TypeDiscriminator;
}
