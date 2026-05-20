using Microsoft.Extensions.Logging;
using Zakira.Conduit.Manifest;

namespace Zakira.Conduit.Cli.Commands;

/// <summary>
///     Implements the <c>validate</c> command.
/// </summary>
internal sealed class ValidateCommandHandler
{
    private readonly IManifestLocator _locator;
    private readonly IManifestLoader _loader;
    private readonly ConsoleStyle _style;
    private readonly ILogger<ValidateCommandHandler> _logger;

    public ValidateCommandHandler(IManifestLocator locator, IManifestLoader loader, ConsoleStyle style, ILogger<ValidateCommandHandler> logger)
    {
        _locator = locator;
        _loader = loader;
        _style = style;
        _logger = logger;
    }

    public async Task<int> InvokeAsync(string? manifest, OutputFormat output, CancellationToken cancellationToken)
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
            ErrorRenderer.RenderManifestError(ex, output);
            return 2;
        }

        if (output == OutputFormat.Json)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(new { ok = true, manifest = manifestPath }, ManifestJson.WriteOptions);
            Console.WriteLine(json);
        }
        else
        {
            Console.WriteLine($"{_style.Green("OK")} - {manifestPath}");
        }

        return 0;
    }
}
