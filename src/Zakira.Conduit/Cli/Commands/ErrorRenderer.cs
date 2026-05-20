using System.Text.Json;
using Zakira.Conduit.Manifest;

namespace Zakira.Conduit.Cli.Commands;

/// <summary>
///     Renders a <see cref="ManifestException"/> in the requested output format.
///     Text mode writes a human-readable summary to stderr; JSON mode emits a
///     stable error envelope to stdout so callers can <c>jq</c> the failure too.
/// </summary>
internal static class ErrorRenderer
{
    public static void RenderManifestError(ManifestException ex, OutputFormat output = OutputFormat.Text)
    {
        ArgumentNullException.ThrowIfNull(ex);

        if (output == OutputFormat.Json)
        {
            var dto = new
            {
                ok = false,
                manifest = ex.ManifestPath,
                error = ex.Message,
                details = ex.Errors,
            };
            var json = JsonSerializer.Serialize(dto, ManifestJson.WriteOptions);
            Console.WriteLine(json);
            return;
        }

        Console.Error.WriteLine("error: " + ex.Message);
        if (ex.ManifestPath is not null)
        {
            Console.Error.WriteLine($"  manifest: {ex.ManifestPath}");
        }

        if (ex.Errors.Count > 0)
        {
            foreach (var line in ex.Errors)
            {
                Console.Error.WriteLine($"  - {line}");
            }
        }
    }
}
