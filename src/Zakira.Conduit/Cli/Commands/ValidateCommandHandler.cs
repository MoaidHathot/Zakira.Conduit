using Microsoft.Extensions.Logging;
using Zakira.Conduit.Manifest;

namespace Zakira.Conduit.Cli.Commands;

/// <summary>
///     Implements the <c>validate</c> command. Performs no IO against remote sources.
/// </summary>
internal sealed class ValidateCommandHandler
{
    private readonly IManifestLocator _locator;
    private readonly IManifestLoader _loader;
    private readonly ILogger<ValidateCommandHandler> _logger;

    public ValidateCommandHandler(IManifestLocator locator, IManifestLoader loader, ILogger<ValidateCommandHandler> logger)
    {
        _locator = locator;
        _loader = loader;
        _logger = logger;
    }

    public async Task<int> InvokeAsync(string? manifest, CancellationToken cancellationToken)
    {
        string manifestPath;
        try
        {
            manifestPath = _locator.Locate(manifest);
            _logger.LogDebug("Validating manifest: {Path}", manifestPath);
            _ = await _loader.LoadAsync(manifestPath, cancellationToken).ConfigureAwait(false);
        }
        catch (ManifestException ex)
        {
            ErrorRenderer.RenderManifestError(ex);
            return 2;
        }

        Console.WriteLine($"OK - {manifestPath}");
        return 0;
    }
}
