using Microsoft.Extensions.Logging;
using Zakira.Conduit.Manifest;

namespace Zakira.Conduit.Sources.Azdo;

/// <summary>
///     <see cref="ISkillSourceFetcher"/> for <see cref="AzdoSkillSource"/>.
///     Resolves the requested ref to a commit SHA, then downloads one zip per
///     <c>scopePath</c> via the AzDO Items REST endpoint and extracts each
///     into its own content directory.
/// </summary>
public sealed class AzdoSkillSourceFetcher : ISkillSourceFetcher
{
    private readonly IAzdoRefResolver _refResolver;
    private readonly IAzdoItemsArchiveDownloader _downloader;
    private readonly ILogger<AzdoSkillSourceFetcher> _logger;

    public AzdoSkillSourceFetcher(
        IAzdoRefResolver refResolver,
        IAzdoItemsArchiveDownloader downloader,
        ILogger<AzdoSkillSourceFetcher> logger)
    {
        ArgumentNullException.ThrowIfNull(refResolver);
        ArgumentNullException.ThrowIfNull(downloader);
        ArgumentNullException.ThrowIfNull(logger);

        _refResolver = refResolver;
        _downloader = downloader;
        _logger = logger;
    }

    /// <inheritdoc />
    public string SourceKind => AzdoSkillSource.TypeDiscriminator;

    /// <inheritdoc />
    public async Task<FetchedSource> FetchAsync(ISkillSource source, FetchContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(context);
        if (source is not AzdoSkillSource azdo)
        {
            throw new ArgumentException($"Expected an {nameof(AzdoSkillSource)} but got '{source.GetType().Name}'.", nameof(source));
        }

        var workRoot = Path.Combine(Path.GetTempPath(), "Zakira.Conduit", "fetch", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workRoot);

        try
        {
            // 1. Resolve ref -> commit SHA (verbatim when already a commit).
            var refKind = azdo.ResolvedRefKind ?? "branch";
            var refValue = azdo.ResolvedRef
                ?? throw new AzdoApiException(
                    $"Azure DevOps source '{azdo.Slug}' has no branch/tag/commit configured.",
                    System.Net.HttpStatusCode.BadRequest);

            _logger.LogInformation("Fetching {Slug} (refKind={Kind}, ref={Ref}, paths=[{Paths}])",
                azdo.Slug, refKind, refValue, string.Join(", ", azdo.EffectivePaths.Select(p => p.Path)));

            var commitSha = await _refResolver.ResolveAsync(azdo, refValue, refKind, cancellationToken).ConfigureAwait(false);

            // 2. Short-circuit on SHA match (state-based cache; cheaper than ETag).
            if (!string.IsNullOrEmpty(context.PreviousEtag) &&
                string.Equals(context.PreviousEtag, commitSha, StringComparison.Ordinal))
            {
                Directory.Delete(workRoot, recursive: true);
                return FetchedSource.Unchanged(source, resolvedRef: commitSha, etag: commitSha);
            }

            // 3. Download + extract one zip per effective path.
            var effectivePaths = azdo.EffectivePaths;
            var contents = new List<FetchedContent>(capacity: effectivePaths.Count == 0 ? 1 : effectivePaths.Count);

            if (effectivePaths.Count == 0)
            {
                var extractedDir = Path.Combine(workRoot, "root");
                Directory.CreateDirectory(extractedDir);
                await DownloadAndExtractAsync(azdo, commitSha, scopePath: null, extractedDir, cancellationToken).ConfigureAwait(false);
                contents.Add(new FetchedContent(extractedDir));
            }
            else
            {
                for (var i = 0; i < effectivePaths.Count; i++)
                {
                    var subPath = effectivePaths[i];
                    var extractedDir = Path.Combine(workRoot, $"path-{i:D3}");
                    Directory.CreateDirectory(extractedDir);

                    await DownloadAndExtractAsync(azdo, commitSha, subPath.Path, extractedDir, cancellationToken).ConfigureAwait(false);

                    // AzDO returns the subtree rooted at scopePath. The basename of
                    // scopePath itself is the top-level entry inside the zip. We want
                    // contents directly under extractedDir to match the synchronizer's
                    // expectation that "this directory IS the content unit".
                    var nestedRoot = LocateScopeRoot(extractedDir, subPath.Path);

                    contents.Add(new FetchedContent(nestedRoot, subPath.ResolvedBasename));
                }
            }

            return new FetchedSource(
                contents: contents,
                source: source,
                resolvedRef: commitSha,
                etag: commitSha,
                notModified: false,
                cleanup: () =>
                {
                    try
                    {
                        if (Directory.Exists(workRoot))
                        {
                            Directory.Delete(workRoot, recursive: true);
                        }
                    }
                    catch (IOException ex)
                    {
                        _logger.LogWarning(ex, "Failed to clean up temp directory '{Dir}'", workRoot);
                    }

                    return ValueTask.CompletedTask;
                });
        }
        catch
        {
            try
            {
                if (Directory.Exists(workRoot))
                {
                    Directory.Delete(workRoot, recursive: true);
                }
            }
            catch
            {
                // best-effort
            }

            throw;
        }
    }

    private async Task DownloadAndExtractAsync(AzdoSkillSource source, string commitSha, string? scopePath, string destinationDir, CancellationToken cancellationToken)
    {
        var archivePath = Path.Combine(destinationDir, "..", Guid.NewGuid().ToString("N") + ".zip");

        await using (var archiveStream = File.Create(archivePath))
        {
            await _downloader.DownloadAsync(source, commitSha, scopePath, archiveStream, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            await using var archiveStream = File.OpenRead(archivePath);
            var count = AzdoZipExtractor.Extract(archiveStream, destinationDir);
            _logger.LogDebug("Extracted {Count} files from {Slug} scope='{Scope}'", count, source.Slug, scopePath ?? "/");

            if (count == 0)
            {
                throw new AzdoApiException(
                    $"Archive for '{source.Slug}' (scope='{scopePath ?? "/"}') contained no files. Was the path correct?",
                    System.Net.HttpStatusCode.OK);
            }
        }
        finally
        {
            try
            {
                File.Delete(archivePath);
            }
            catch
            {
                // best-effort
            }
        }
    }

    /// <summary>
    ///     AzDO zips for a scopePath contain entries rooted at the scope's
    ///     basename (e.g. scopePath=/skills/code-review -> zip contains
    ///     <c>code-review/...</c>). Step into that folder so the synchronizer
    ///     sees the actual file payload at the top level.
    /// </summary>
    private static string LocateScopeRoot(string extractedDir, string scopePath)
    {
        var normalized = scopePath.Replace('\\', '/').Trim('/');
        if (normalized.Length == 0)
        {
            return extractedDir;
        }

        var slash = normalized.LastIndexOf('/');
        var basename = slash < 0 ? normalized : normalized[(slash + 1)..];

        var candidate = Path.Combine(extractedDir, basename);
        if (Directory.Exists(candidate))
        {
            return candidate;
        }

        // Fallback: if there's exactly one top-level directory, use it.
        var topDirs = Directory.GetDirectories(extractedDir);
        if (topDirs.Length == 1 && Directory.GetFiles(extractedDir).Length == 0)
        {
            return topDirs[0];
        }

        return extractedDir;
    }
}
