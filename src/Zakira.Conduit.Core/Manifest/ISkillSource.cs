using System.Text.Json.Serialization;

namespace Zakira.Conduit.Manifest;

/// <summary>
///     Marker interface implemented by every concrete skill source kind.
///     New source kinds (e.g. GitLab, local path, http archive) are added by
///     implementing this interface and registering a corresponding
///     <see cref="Sources.ISkillSourceFetcher"/>.
/// </summary>
/// <remarks>
///     The JSON discriminator is the lowercase <c>type</c> property; e.g.
///     <c>{ "type": "github", ... }</c>.
/// </remarks>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type", UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization)]
[JsonDerivedType(typeof(GitHubSkillSource), GitHubSkillSource.TypeDiscriminator)]
[JsonDerivedType(typeof(LocalDirectorySkillSource), LocalDirectorySkillSource.TypeDiscriminator)]
public interface ISkillSource
{
    /// <summary>
    ///     A short, human-readable identifier for this source kind. Mirrors the
    ///     JSON discriminator and is useful for logging.
    /// </summary>
    [JsonIgnore]
    string Kind { get; }
}
