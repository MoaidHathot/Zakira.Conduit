using Microsoft.Extensions.Logging;
using Zakira.Conduit.Manifest;

namespace Zakira.Conduit.Sources.GitHub;

/// <summary>
///     <see cref="ISkillSourceFetcher"/> for <see cref="GitHubSkillSource"/>.
///     Downloads a zipball snapshot (no full git clone) and extracts it,
///     optionally filtered to a sub-path of the repository.
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

        var contentDir = Path.Combine(workRoot, "content");
        Directory.CreateDirectory(contentDir);

        try
        {
            _logger.LogInformation("Fetching {Slug} (ref={Ref}, path={Path})", gh.Slug, ref0 ?? "<default>", gh.Path ?? "<root>");

            var archivePath = Path.Combine(workRoot, "archive.zip");
            await using (var archiveStream = File.Create(archivePath))
            {
                await _downloader.DownloadAsync(gh.Owner, gh.Repo, ref0, archiveStream, cancellationToken).ConfigureAwait(false);
            }

            await using (var archiveStream = File.OpenRead(archivePath))
            {
                var filesExtracted = ZipballExtractor.Extract(archiveStream, contentDir, gh.Path);
                _logger.LogDebug("Extracted {Count} files from {Slug}", filesExtracted, gh.Slug);

                if (!Directory.Exists(contentDir) || !Directory.EnumerateFileSystemEntries(contentDir).Any())
                {
                    throw new GitHubDownloadException(
                        gh.Path is null
                            ? $"Archive for '{gh.Slug}' contained no files."
                            : $"Sub-path '{gh.Path}' was not found in archive for '{gh.Slug}'.",
                        System.Net.HttpStatusCode.OK);
                }
            }

            File.Delete(archivePath);

            return new FetchedSource(
                contentDirectory: contentDir,
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
