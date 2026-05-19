using Microsoft.Extensions.Logging;
using Zakira.Conduit.Manifest;

namespace Zakira.Conduit.Cli.Commands;

/// <summary>
///     Implements the <c>list</c> command. Prints a one-line summary per entry.
/// </summary>
internal sealed class ListCommandHandler
{
    private readonly IManifestLocator _locator;
    private readonly IManifestLoader _loader;
    private readonly ILogger<ListCommandHandler> _logger;

    public ListCommandHandler(IManifestLocator locator, IManifestLoader loader, ILogger<ListCommandHandler> logger)
    {
        _locator = locator;
        _loader = loader;
        _logger = logger;
    }

    public async Task<int> InvokeAsync(string? manifest, CancellationToken cancellationToken)
    {
        string manifestPath;
        ConduitManifest model;
        try
        {
            manifestPath = _locator.Locate(manifest);
            _logger.LogDebug("Using manifest: {Path}", manifestPath);
            model = await _loader.LoadAsync(manifestPath, cancellationToken).ConfigureAwait(false);
        }
        catch (ManifestException ex)
        {
            ErrorRenderer.RenderManifestError(ex);
            return 2;
        }

        Console.WriteLine($"# {manifestPath}");
        Console.WriteLine($"# version={model.Version}, entries={model.Entries.Count}");
        Console.WriteLine();

        foreach (var entry in model.Entries)
        {
            var status = entry.Disabled ? " (disabled)" : string.Empty;
            var sourceSummary = entry.Source switch
            {
                GitHubSkillSource gh => SummarizeGitHub(gh),
                LocalDirectorySkillSource local => $"local:{local.Path}",
                _ => entry.Source.Kind,
            };

            Console.WriteLine($"- {entry.Name}{status}");
            Console.WriteLine($"    source : {sourceSummary}");
            Console.WriteLine($"    targets:");
            foreach (var target in entry.Targets)
            {
                Console.WriteLine($"      - {target}");
            }

            if (!string.IsNullOrWhiteSpace(entry.Description))
            {
                Console.WriteLine($"    note   : {entry.Description}");
            }
        }

        return 0;
    }

    private static string SummarizeGitHub(GitHubSkillSource gh)
    {
        var refPart = gh.Commit is not null ? $"@{gh.Commit}"
            : gh.Branch is not null ? $"@{gh.Branch}"
            : string.Empty;
        var pathPart = string.IsNullOrEmpty(gh.Path) ? string.Empty : $":{gh.Path}";
        return $"github:{gh.Slug}{pathPart}{refPart}";
    }
}
