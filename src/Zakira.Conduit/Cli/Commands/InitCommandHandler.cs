using System.Text.Json;
using Microsoft.Extensions.Logging;
using Zakira.Conduit.Hosting;
using Zakira.Conduit.Manifest;

namespace Zakira.Conduit.Cli.Commands;

/// <summary>
///     Implements the <c>init</c> command. Writes a starter manifest at the
///     resolved path (explicit, or <c>$XDG_CONFIG_HOME/Zakira.Conduit/conduit.json</c>).
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

    public async Task<int> InvokeAsync(string? manifest, bool force, bool interactive, CancellationToken cancellationToken)
    {
        var targetPath = ResolveTargetPath(manifest);

        if (File.Exists(targetPath) && !force)
        {
            Console.Error.WriteLine($"error: manifest already exists at '{targetPath}'. Use --force to overwrite.");
            return 2;
        }

        if (interactive && (Console.IsInputRedirected || Console.IsOutputRedirected))
        {
            Console.Error.WriteLine("error: --interactive requires an attached TTY (stdin and stdout must not be redirected).");
            return 2;
        }

        var directory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var manifestModel = interactive
            ? BuildInteractiveManifest(targetPath, cancellationToken)
            : CreateStarterManifest();

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
        var raw = ResolveRawPath(manifest);
        return Path.GetFullPath(raw);
    }

    private string ResolveRawPath(string? manifest)
    {
        if (!string.IsNullOrWhiteSpace(manifest))
        {
            return manifest;
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
            Schema = "https://raw.githubusercontent.com/MoaidHathot/Zakira.Conduit/main/schemas/conduit.schema.json",
            Entries =
            [
                new ConduitEntry
                {
                    Name = "example-skill",
                    Description = "Replace me. Each entry mirrors a remote skill source into one or more local target directories.",
                    Source = new GitHubSkillSource
                    {
                        Repo = "owner/repo",
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

    /// <summary>
    ///     Walks the user through creating one starter entry. The resulting
    ///     manifest is still hand-editable afterwards; this is just to lower
    ///     the empty-page friction for first-time users.
    /// </summary>
    private static ConduitManifest BuildInteractiveManifest(string targetPath, CancellationToken cancellationToken)
    {
        Console.WriteLine();
        Console.WriteLine($"Interactive init - writing to {targetPath}");
        Console.WriteLine("(Press Ctrl+C at any time to cancel without writing anything.)");
        Console.WriteLine();

        var kind = Choose(
            prompt: "Source kind",
            options: ["github", "local"],
            defaultOption: "github",
            cancellationToken: cancellationToken);

        ISkillSource source;
        string nameSuggestion;

        if (kind == "local")
        {
            var path = Prompt("Source directory (absolute, or relative to the manifest)", "./skills", cancellationToken);
            nameSuggestion = SuggestEntryNameFromLocal(path);
            source = new LocalDirectorySkillSource { Path = path };
        }
        else
        {
            var repo = Prompt("GitHub repository (owner/repo or full URL)", "owner/repo", cancellationToken);
            var subPath = PromptOptional("Sub-path inside the repo to mirror (leave empty for the whole repo)", cancellationToken);
            var branch = PromptOptional("Branch (leave empty for the default branch)", cancellationToken);

            nameSuggestion = SuggestEntryNameFromGitHub(repo, subPath);
            source = new GitHubSkillSource
            {
                Repo = repo,
                Path = string.IsNullOrWhiteSpace(subPath) ? null : subPath,
                Branch = string.IsNullOrWhiteSpace(branch) ? null : branch,
            };
        }

        var name = Prompt("Entry name (used as the destination sub-directory)", nameSuggestion, cancellationToken);
        var target = Prompt("Target directory", "$XDG_CONFIG_HOME/agents/skills", cancellationToken);

        return new ConduitManifest
        {
            Version = ManifestNames.CurrentSchemaVersion,
            Schema = "https://raw.githubusercontent.com/MoaidHathot/Zakira.Conduit/main/schemas/conduit.schema.json",
            Entries =
            [
                new ConduitEntry
                {
                    Name = name,
                    Source = source,
                    Targets = [target],
                },
            ],
        };
    }

    private static string SuggestEntryNameFromGitHub(string repo, string? subPath)
    {
        if (!string.IsNullOrWhiteSpace(subPath))
        {
            var normalized = subPath.Replace('\\', '/').TrimEnd('/');
            var slash = normalized.LastIndexOf('/');
            return Sanitize(slash < 0 ? normalized : normalized[(slash + 1)..]);
        }

        if (GitHubRepoReference.TryParse(repo, out _, out var name, out _))
        {
            return Sanitize(name);
        }

        return "my-skill";
    }

    private static string SuggestEntryNameFromLocal(string path)
    {
        var normalized = path.Replace('\\', '/').TrimEnd('/');
        var slash = normalized.LastIndexOf('/');
        var candidate = slash < 0 ? normalized : normalized[(slash + 1)..];
        return Sanitize(candidate);
    }

    private static string Sanitize(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "my-skill";
        }

        var clean = new string([.. raw.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.')]);
        return string.IsNullOrEmpty(clean) ? "my-skill" : clean;
    }

    private static string Prompt(string label, string defaultValue, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Console.Write($"{label} [{defaultValue}]: ");
            var input = Console.ReadLine();
            if (input is null)
            {
                throw new OperationCanceledException("Input stream closed.");
            }

            var trimmed = input.Trim();
            if (trimmed.Length == 0)
            {
                return defaultValue;
            }

            return trimmed;
        }
    }

    private static string? PromptOptional(string label, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Console.Write($"{label}: ");
        var input = Console.ReadLine();
        if (input is null)
        {
            return null;
        }

        var trimmed = input.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static string Choose(string prompt, IReadOnlyList<string> options, string defaultOption, CancellationToken cancellationToken)
    {
        var rendered = string.Join("/", options.Select(o => o == defaultOption ? o.ToUpperInvariant() : o));
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Console.Write($"{prompt} [{rendered}]: ");
            var input = Console.ReadLine();
            if (input is null)
            {
                throw new OperationCanceledException("Input stream closed.");
            }

            var trimmed = input.Trim();
            if (trimmed.Length == 0)
            {
                return defaultOption;
            }

            var match = options.FirstOrDefault(o => string.Equals(o, trimmed, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match;
            }

            Console.Error.WriteLine($"  '{trimmed}' is not one of: {string.Join(", ", options)}. Try again.");
        }
    }
}
