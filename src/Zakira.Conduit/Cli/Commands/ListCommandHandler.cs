using System.Text.Json;
using Microsoft.Extensions.Logging;
using Zakira.Conduit.Manifest;

namespace Zakira.Conduit.Cli.Commands;

/// <summary>
///     Implements the <c>list</c> command.
/// </summary>
internal sealed class ListCommandHandler
{
    private readonly IManifestLocator _locator;
    private readonly IManifestLoader _loader;
    private readonly ConsoleStyle _style;
    private readonly ILogger<ListCommandHandler> _logger;

    public ListCommandHandler(IManifestLocator locator, IManifestLoader loader, ConsoleStyle style, ILogger<ListCommandHandler> logger)
    {
        _locator = locator;
        _loader = loader;
        _style = style;
        _logger = logger;
    }

    public async Task<int> InvokeAsync(string? manifest, OutputFormat output, CancellationToken cancellationToken)
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
            ErrorRenderer.RenderManifestError(ex, output);
            return 2;
        }

        if (output == OutputFormat.Json)
        {
            RenderJson(manifestPath, model);
        }
        else
        {
            RenderText(manifestPath, model);
        }

        return 0;
    }

    private void RenderText(string manifestPath, ConduitManifest model)
    {
        Console.WriteLine(_style.Dim($"# {manifestPath}"));
        Console.WriteLine(_style.Dim($"# version={model.Version}, entries={model.Entries.Count}"));
        Console.WriteLine();

        foreach (var entry in model.Entries)
        {
            var status = entry.Disabled ? _style.Yellow(" (disabled)") : string.Empty;
            var sourceSummary = entry.Source switch
            {
                GitHubSkillSource gh => SummarizeGitHub(gh),
                LocalDirectorySkillSource local => SummarizeLocal(local),
                AzdoSkillSource azdo => SummarizeAzdo(azdo),
                _ => entry.Source.Kind,
            };

            Console.WriteLine($"- {_style.Bold(entry.Name)}{status}");
            Console.WriteLine($"    source : {sourceSummary}");
            Console.WriteLine($"    targets:");
            foreach (var target in entry.Targets)
            {
                var aliased = string.IsNullOrWhiteSpace(target.As) ? string.Empty : $"  (as {target.As})";
                Console.WriteLine($"      - {target.Path}{aliased}");
            }

            if (!string.IsNullOrWhiteSpace(entry.Description))
            {
                Console.WriteLine($"    note   : {_style.Dim(entry.Description)}");
            }
        }
    }

    private static void RenderJson(string manifestPath, ConduitManifest model)
    {
        var dto = new
        {
            manifest = manifestPath,
            version = model.Version,
            entries = model.Entries.Select(e => new
            {
                name = e.Name,
                description = e.Description,
                disabled = e.Disabled,
                source = e.Source,
                targets = e.Targets,
            }),
        };

        var json = JsonSerializer.Serialize(dto, ManifestJson.WriteOptions);
        Console.WriteLine(json);
    }

    private static string SummarizeGitHub(GitHubSkillSource gh)
    {
        var refPart = gh.Commit is not null ? $"@{gh.Commit}"
            : gh.Branch is not null ? $"@{gh.Branch}"
            : string.Empty;

        var paths = gh.EffectivePaths;
        var pathPart = paths.Count switch
        {
            0 => string.Empty,
            1 => ":" + FormatPathSpec(paths[0]),
            _ => ":[" + string.Join(", ", paths.Select(FormatPathSpec)) + "]",
        };

        return $"github:{gh.Slug}{pathPart}{refPart}";
    }

    private static string SummarizeLocal(LocalDirectorySkillSource local)
    {
        var paths = local.EffectivePaths;
        return paths.Count switch
        {
            0 => "local:<no paths>",
            1 => "local:" + FormatPathSpec(paths[0]),
            _ => "local:[" + string.Join(", ", paths.Select(FormatPathSpec)) + "]",
        };
    }

    private static string FormatPathSpec(PathSpec spec) =>
        string.IsNullOrWhiteSpace(spec.As) ? spec.Path : $"{spec.Path} as {spec.As}";

    private static string SummarizeAzdo(AzdoSkillSource azdo)
    {
        var refPart = azdo.Commit is not null ? $"@{azdo.Commit}"
            : azdo.Tag is not null ? $"@tag:{azdo.Tag}"
            : azdo.Branch is not null ? $"@{azdo.Branch}"
            : string.Empty;

        var paths = azdo.EffectivePaths;
        var pathPart = paths.Count switch
        {
            0 => string.Empty,
            1 => ":" + FormatPathSpec(paths[0]),
            _ => ":[" + string.Join(", ", paths.Select(FormatPathSpec)) + "]",
        };

        return $"azdo:{azdo.Slug}{pathPart}{refPart}";
    }
}
