using System.Text.Json;
using Zakira.Conduit.Sources.Inference;

namespace Zakira.Conduit.Manifest;

/// <summary>
///     Default <see cref="IManifestLoader"/> backed by <see cref="JsonSerializer"/>.
/// </summary>
public sealed class JsonManifestLoader : IManifestLoader
{
    private readonly SkillSourceInferenceCoordinator? _inferenceCoordinator;

    public JsonManifestLoader()
        : this(null)
    {
    }

    public JsonManifestLoader(SkillSourceInferenceCoordinator? inferenceCoordinator)
    {
        _inferenceCoordinator = inferenceCoordinator;
    }

    /// <inheritdoc />
    public async Task<ConduitManifest> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            throw new ManifestException($"Manifest file not found: '{path}'.", path);
        }

        ConduitManifest? manifest;
        try
        {
            await using var stream = File.OpenRead(path);
            manifest = await JsonSerializer.DeserializeAsync<ConduitManifest>(stream, ManifestJson.ReadOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            throw new ManifestException($"Manifest file '{path}' is not valid JSON: {ex.Message}", path, innerException: ex);
        }
        catch (NotSupportedException ex)
        {
            // Polymorphism failure (unknown discriminator value) surfaces as NotSupportedException.
            throw new ManifestException($"Manifest file '{path}' references an unsupported source type: {ex.Message}", path, innerException: ex);
        }

        if (manifest is null)
        {
            throw new ManifestException($"Manifest file '{path}' deserialized to null.", path);
        }

        // Resolve any 'uri'-shaped sources into their concrete kinds before
        // validation runs. The validator never sees a UriBasedSkillSource.
        if (_inferenceCoordinator is not null)
        {
            try
            {
                manifest = _inferenceCoordinator.Rewrite(manifest);
            }
            catch (SkillSourceInferenceException ex)
            {
                throw new ManifestException($"Manifest file '{path}' has a source that could not be inferred: {ex.Message}", path, innerException: ex);
            }
        }

        var errors = ManifestValidator.Validate(manifest);
        if (errors.Count > 0)
        {
            throw new ManifestException($"Manifest file '{path}' failed validation.", path, errors);
        }

        return manifest;
    }
}
