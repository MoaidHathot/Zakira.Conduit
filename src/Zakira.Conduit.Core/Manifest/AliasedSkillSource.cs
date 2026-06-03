using System.Text.Json.Serialization;

namespace Zakira.Conduit.Manifest;

/// <summary>
///     A transient source wrapper that pairs an inner <see cref="ISkillSource"/>
///     with an explicit destination alias supplied by the user. Created by the
///     <see cref="SourceShorthandJsonConverter"/> from one of two JSON shapes:
///     <list type="bullet">
///         <item><description>
///             In-string arrow suffix on a bare URI shorthand:
///             <c>"https://github.com/owner/repo -&gt; MyName"</c>. The portion
///             after <c> -&gt; </c> becomes <see cref="As"/>.
///         </description></item>
///         <item><description>
///             Object wrapper form (valid as a top-level <c>source</c> value or
///             as an array element): <c>{ "source": ..., "as": "MyName" }</c>.
///             The inner <c>source</c> may itself be any other source shape
///             (bare URI, concrete <c>{ "type": ... }</c> object, etc.).
///         </description></item>
///     </list>
/// </summary>
/// <remarks>
///     <para>
///         This record never reaches the validator, synchronizer, or any
///         fetcher: the <see cref="Sources.Inference.SkillSourceInferenceCoordinator"/>
///         unwraps every <see cref="AliasedSkillSource"/> at manifest-load
///         time, applies <see cref="As"/> to the owning entry's name (or to
///         the synthesized name of an array-expanded sub-entry), and then
///         drops the wrapper.
///     </para>
///     <para>
///         <see cref="AliasedSkillSource"/> is intentionally <b>not</b>
///         registered as a <c>[JsonDerivedType]</c> on
///         <see cref="ISkillSource"/>: it has no <c>type</c> discriminator and
///         is recognised exclusively by the shorthand converter (which peeks
///         for the wrapper shape <c>{ source, as }</c>).
///     </para>
/// </remarks>
public sealed record AliasedSkillSource : ISkillSource
{
    /// <summary>
    ///     The non-JSON, internal discriminator value (<c>"aliased"</c>).
    ///     Not used for deserialisation; included only so logs / <see cref="Kind"/>
    ///     have a stable label if a wrapper somehow leaks out.
    /// </summary>
    public const string TypeDiscriminator = "aliased";

    /// <summary>The wrapped source. Required, never itself an <see cref="AliasedSkillSource"/>.</summary>
    public required ISkillSource Inner { get; init; }

    /// <summary>
    ///     The destination alias. Must match the entry-name charset
    ///     <c>^[A-Za-z0-9._-]+$</c>. The shorthand converter rejects
    ///     anything else.
    /// </summary>
    public required string As { get; init; }

    /// <inheritdoc />
    [JsonIgnore]
    public string Kind => TypeDiscriminator;
}
