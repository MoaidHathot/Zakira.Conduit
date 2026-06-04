using System.Text.Json;
using Microsoft.Extensions.Logging;
using Zakira.Conduit.Manifest;
using Zakira.Conduit.Paths;
using Zakira.Conduit.Strategies.Skills;

namespace Zakira.Conduit.Cli.Commands;

/// <summary>
///     Implements <c>conduit skills probe</c>: scans a target directory for
///     known agent-harness layouts and prints what it finds. Useful before
///     adding a skills entry to a manifest to verify that the user's target
///     contains at least one harness.
/// </summary>
internal sealed class SkillsProbeCommandHandler
{
    private readonly IManifestLocator _locator;
    private readonly IManifestLoader _loader;
    private readonly IPathResolver _pathResolver;
    private readonly ConsoleStyle _style;
    private readonly ILogger<SkillsProbeCommandHandler> _logger;

    public SkillsProbeCommandHandler(
        IManifestLocator locator,
        IManifestLoader loader,
        IPathResolver pathResolver,
        ConsoleStyle style,
        ILogger<SkillsProbeCommandHandler> logger)
    {
        _locator = locator;
        _loader = loader;
        _pathResolver = pathResolver;
        _style = style;
        _logger = logger;
    }

    public async Task<int> InvokeAsync(
        string targetPath,
        string? manifest,
        OutputFormat output,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            Console.Error.WriteLine("conduit skills probe: <target> is required.");
            return 2;
        }

        // Use the manifest's strategies.skills.harnessRegistry overrides when
        // available, otherwise probe with the built-in registry alone. The
        // manifest is optional: probe is meant to be a quick utility.
        IReadOnlyDictionary<string, IReadOnlyList<string>> registry;
        string manifestDirOrCwd = Directory.GetCurrentDirectory();
        try
        {
            var path = _locator.Locate(manifest);
            _logger.LogDebug("Using manifest: {Path}", path);
            var model = await _loader.LoadAsync(path, cancellationToken).ConfigureAwait(false);
            registry = HarnessRegistry.Merge(model.Strategies?.Skills?.HarnessRegistry);
            manifestDirOrCwd = Path.GetDirectoryName(Path.GetFullPath(path)) ?? manifestDirOrCwd;
        }
        catch (ManifestException)
        {
            // No manifest, or a broken one. Fall back to the default registry;
            // probe is still useful for users without a conduit.json.
            registry = HarnessRegistry.Merge(null);
        }

        var resolved = _pathResolver.Resolve(targetPath, manifestDirOrCwd);
        var scanner = new HarnessScanner(registry);
        var matches = scanner.Scan(resolved, filter: null);

        if (output == OutputFormat.Json)
        {
            RenderJson(targetPath, resolved, registry, matches);
        }
        else
        {
            RenderText(targetPath, resolved, registry, matches);
        }

        return matches.Count == 0 ? 1 : 0;
    }

    private void RenderText(
        string requested,
        string resolved,
        IReadOnlyDictionary<string, IReadOnlyList<string>> registry,
        IReadOnlyList<HarnessMatch> matches)
    {
        Console.WriteLine(_style.Dim($"# target requested: {requested}"));
        Console.WriteLine(_style.Dim($"# target resolved : {resolved}"));
        Console.WriteLine(_style.Dim($"# registry entries: {registry.Count} ({string.Join(", ", registry.Keys.OrderBy(k => k, StringComparer.Ordinal))})"));
        Console.WriteLine();

        if (matches.Count == 0)
        {
            Console.WriteLine(_style.Yellow("No harnesses detected."));
            Console.WriteLine(_style.Dim("Tip: skills will fall back to writing directly into the target unless 'harness' is set to true or an explicit name list."));
            return;
        }

        Console.WriteLine($"Detected {matches.Count} harness location(s):");
        foreach (var match in matches)
        {
            Console.WriteLine($"  {_style.Green("+")} {_style.Bold(match.HarnessName)}  {_style.Dim($"({match.MatchedRelativePath})")}");
            Console.WriteLine($"      => {match.ResolvedSkillsDirectory}");
        }
    }

    private static void RenderJson(
        string requested,
        string resolved,
        IReadOnlyDictionary<string, IReadOnlyList<string>> registry,
        IReadOnlyList<HarnessMatch> matches)
    {
        var dto = new
        {
            requested,
            resolved,
            registry = registry.ToDictionary(kvp => kvp.Key, kvp => (object)kvp.Value),
            matches = matches.Select(m => new
            {
                harness = m.HarnessName,
                matched = m.MatchedRelativePath,
                directory = m.ResolvedSkillsDirectory,
            }),
        };

        Console.WriteLine(JsonSerializer.Serialize(dto, ManifestJson.WriteOptions));
    }
}
