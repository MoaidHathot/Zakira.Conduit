using Zakira.Conduit.Manifest;

namespace Zakira.Conduit.Cli.Commands;

/// <summary>
///     Pretty-prints a <see cref="ManifestException"/> with per-error detail.
/// </summary>
internal static class ErrorRenderer
{
    public static void RenderManifestError(ManifestException ex)
    {
        ArgumentNullException.ThrowIfNull(ex);

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
