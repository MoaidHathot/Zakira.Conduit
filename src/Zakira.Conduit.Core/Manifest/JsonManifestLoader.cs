using System.Text.Json;
using Zakira.Conduit.Paths;
using Zakira.Conduit.Sources.Inference;
using Zakira.Conduit.Strategies;

namespace Zakira.Conduit.Manifest;

/// <summary>
///     Default <see cref="IManifestLoader"/> backed by <see cref="JsonSerializer"/>.
/// </summary>
public sealed class JsonManifestLoader : IManifestLoader
{
    private readonly SourceInferenceCoordinator? _inferenceCoordinator;
    private readonly IPathResolver? _pathResolver;
    private readonly IPlanStrategyRegistry? _strategyRegistry;

    public JsonManifestLoader()
        : this(inferenceCoordinator: null, pathResolver: null, strategyRegistry: null)
    {
    }

    public JsonManifestLoader(SourceInferenceCoordinator? inferenceCoordinator)
        : this(inferenceCoordinator, pathResolver: null, strategyRegistry: null)
    {
    }

    public JsonManifestLoader(SourceInferenceCoordinator? inferenceCoordinator, IPathResolver? pathResolver)
        : this(inferenceCoordinator, pathResolver, strategyRegistry: null)
    {
    }

    public JsonManifestLoader(
        SourceInferenceCoordinator? inferenceCoordinator,
        IPathResolver? pathResolver,
        IPlanStrategyRegistry? strategyRegistry)
    {
        _inferenceCoordinator = inferenceCoordinator;
        _pathResolver = pathResolver;
        _strategyRegistry = strategyRegistry;
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
        // validation runs. The validator never sees a UriBasedSource.
        if (_inferenceCoordinator is not null)
        {
            try
            {
                manifest = _inferenceCoordinator.Rewrite(manifest);
            }
            catch (SourceInferenceException ex)
            {
                throw new ManifestException($"Manifest file '{path}' has a source that could not be inferred: {ex.Message}", path, innerException: ex);
            }
        }

        // Static (no-IO) structural validation first.
        var errors = new List<string>(ManifestValidator.Validate(manifest, _strategyRegistry));

        // Resolved-path validation: catches collisions the static check
        // misses because it doesn't expand '~', env vars, or relative paths.
        // Skipped when no IPathResolver is registered (library consumers
        // building their own pipeline are responsible for path resolution).
        if (_pathResolver is not null && _strategyRegistry is not null)
        {
            var resolvedValidator = new ResolvedDestinationValidator(_pathResolver, _strategyRegistry);
            foreach (var error in resolvedValidator.Validate(manifest, path))
            {
                errors.Add(error);
            }
        }

        if (errors.Count > 0)
        {
            throw new ManifestException($"Manifest file '{path}' failed validation.", path, errors);
        }

        return manifest;
    }
}
