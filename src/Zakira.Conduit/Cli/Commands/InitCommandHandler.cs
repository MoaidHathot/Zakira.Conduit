using System.Text.Json;
using Microsoft.Extensions.Logging;
using Zakira.Conduit.Hosting;
using Zakira.Conduit.Manifest;

namespace Zakira.Conduit.Cli.Commands;

/// <summary>
///     Implements the <c>init</c> command. Writes a starter manifest at the
///     resolved path (explicit, or <c>$XDG_CONFIG_HOME/conduit/conduit.json</c>).
/// </summary>
internal sealed class InitCommandHandler
{
    private readonly IEnvironment _environment;
    private readonly ILogger<InitCommandHandler> _logger;

    public InitCommandHandler(IEnvironment environment, ILogger<InitCommandHandler> logger)
    {
        _environment = environment;
        _logger = logger;
    }

    public async Task<int> InvokeAsync(string? manifest, bool force, CancellationToken cancellationToken)
    {
        var targetPath = ResolveTargetPath(manifest);

        if (File.Exists(targetPath) && !force)
        {
            Console.Error.WriteLine($"error: manifest already exists at '{targetPath}'. Use --force to overwrite.");
            return 2;
        }

        var directory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var manifestModel = CreateStarterManifest();

        await using (var stream = File.Create(targetPath))
        {
            await JsonSerializer.SerializeAsync(stream, manifestModel, ManifestJson.WriteOptions, cancellationToken).ConfigureAwait(false);
            // Trailing newline for tooling friendliness.
            await stream.WriteAsync("\n"u8.ToArray(), cancellationToken).ConfigureAwait(false);
        }

        _logger.LogInformation("Wrote starter manifest to {Path}", targetPath);
        Console.WriteLine($"Wrote starter manifest to {targetPath}");
        return 0;
    }

    private string ResolveTargetPath(string? manifest)
    {
        if (!string.IsNullOrWhiteSpace(manifest))
        {
            return Path.GetFullPath(manifest);
        }

        var xdg = _environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (!string.IsNullOrWhiteSpace(xdg))
        {
            return Path.Combine(xdg, ManifestNames.ConfigDirectoryName, ManifestNames.DefaultFileName);
        }

        var home = _environment.GetHomeDirectory();
        return Path.Combine(home, ".config", ManifestNames.ConfigDirectoryName, ManifestNames.DefaultFileName);
    }

    private static ConduitManifest CreateStarterManifest() =>
        new()
        {
            Version = ManifestNames.CurrentSchemaVersion,
            Schema = "https://raw.githubusercontent.com/anomalyco/Zakira.Conduit/main/schemas/conduit.schema.json",
            Entries =
            [
                new ConduitEntry
                {
                    Name = "example-skill",
                    Description = "Replace me. Each entry mirrors a remote skill source into one or more local target directories.",
                    Source = new GitHubSkillSource
                    {
                        Owner = "owner",
                        Repo = "repo",
                        Path = "path/inside/repo",
                        Branch = "main",
                    },
                    Targets =
                    [
                        "~/.config/agents/skills",
                    ],
                },
            ],
        };
}
