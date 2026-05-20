using Microsoft.Extensions.Logging;
using Zakira.Conduit.Manifest;

namespace Zakira.Conduit.Sources.GitHub;

/// <summary>
///     <see cref="ISkillSourceFetcher"/> for <see cref="GitHubSkillSource"/>.
///     Downloads a zipball snapshot (no full git clone) and extracts it,
///     optionally producing one content unit per requested sub-path.
/// </summary>
public sealed class GitHubSkillSourceFetcher : ISkillSourceFetcher
{
    private readonly IGitHubArchiveDownloader _downloader;
    private readonly ILogger<GitHubSkillSourceFetcher> _logger;

    public GitHubSkillSourceFetcher(IGitHubArchiveDownloader downloader, ILogger<GitHubSkillSourceFetcher> logger)
    {
        ArgumentNullException.ThrowIfNull(downloader);
        ArgumentNullException.ThrowIfNull(logger);

        _downloader = downloader;
        _logger = logger;
    }

    /// <inheritdoc />
    public string SourceKind => GitHubSkillSource.TypeDiscriminator;

    /// <inheritdoc />
    public async Task<FetchedSource> FetchAsync(ISkillSource source, FetchContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(context);
        if (source is not GitHubSkillSource gh)
        {
            throw new ArgumentException($"Expected a {nameof(GitHubSkillSource)} but got '{source.GetType().Name}'.", nameof(source));
        }

        var ref0 = gh.ResolvedRef;
        var workRoot = Path.Combine(Path.GetTempPath(), "Zakira.Conduit", "fetch", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workRoot);

        var extractedRoot = Path.Combine(workRoot, "extracted");
        Directory.CreateDirectory(extractedRoot);

        try
        {
            _logger.LogInformation("Fetching {Slug} (ref={Ref}, paths=[{Paths}])",
                gh.Slug, ref0 ?? "<default>", string.Join(", ", gh.EffectivePaths));

            // 1. Download the zipball once.
            var archivePath = Path.Combine(workRoot, "archive.zip");
            await using (var archiveStream = File.Create(archivePath))
            {
                await _downloader.DownloadAsync(gh.Owner, gh.RepoName, ref0, archiveStream, cancellationToken).ConfigureAwait(false);
            }

            // 2. Extract everything once. (Zipballs are small repository snapshots.)
            await using (var archiveStream = File.OpenRead(archivePath))
            {
                var filesExtracted = ZipballExtractor.Extract(archiveStream, extractedRoot, subPath: null);
                _logger.LogDebug("Extracted {Count} files from {Slug}", filesExtracted, gh.Slug);

                if (filesExtracted == 0)
                {
                    throw new GitHubDownloadException(
                        $"Archive for '{gh.Slug}' contained no files.",
                        System.Net.HttpStatusCode.OK);
                }
            }

            File.Delete(archivePath);

            // 3. Build the content unit list.
            var effectivePaths = gh.EffectivePaths;
            var contents = new List<FetchedContent>(capacity: effectivePaths.Count == 0 ? 1 : effectivePaths.Count);

            if (effectivePaths.Count == 0)
            {
                // Whole repo, single unit.
                contents.Add(new FetchedContent(extractedRoot));
            }
            else
            {
                foreach (var subPath in effectivePaths)
                {
                    var normalized = subPath.Replace('\\', '/').Trim('/');
                    var resolved = Path.Combine(extractedRoot, normalized.Replace('/', Path.DirectorySeparatorChar));

                    if (!Directory.Exists(resolved))
                    {
                        throw new GitHubDownloadException(
                            $"Sub-path '{subPath}' was not found in archive for '{gh.Slug}'.",
                            System.Net.HttpStatusCode.OK);
                    }

                    var basename = Path.GetFileName(normalized);
                    contents.Add(new FetchedContent(resolved, basename));
                }
            }

            return new FetchedSource(
                contents: contents,
                source: source,
                resolvedRef: ref0,
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
}
