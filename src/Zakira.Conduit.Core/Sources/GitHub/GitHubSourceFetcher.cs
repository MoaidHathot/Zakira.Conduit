using Microsoft.Extensions.Logging;
using Zakira.Conduit.Manifest;
using Zakira.Conduit.Sources.GitHub.Credentials;

namespace Zakira.Conduit.Sources.GitHub;

/// <summary>
///     <see cref="ISourceFetcher"/> for <see cref="GitHubSource"/>.
///     Downloads a zipball snapshot (no full git clone) and extracts it,
///     optionally producing one content unit per requested sub-path.
/// </summary>
public sealed class GitHubSourceFetcher : ISourceFetcher
{
    private readonly IGitHubArchiveDownloader _downloader;
    private readonly ChainedGitHubCredentialProvider _credentials;
    private readonly ILogger<GitHubSourceFetcher> _logger;

    public GitHubSourceFetcher(IGitHubArchiveDownloader downloader, ChainedGitHubCredentialProvider credentials, ILogger<GitHubSourceFetcher> logger)
    {
        ArgumentNullException.ThrowIfNull(downloader);
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentNullException.ThrowIfNull(logger);

        _downloader = downloader;
        _credentials = credentials;
        _logger = logger;
    }

    /// <inheritdoc />
    public string SourceKind => GitHubSource.TypeDiscriminator;

    /// <inheritdoc />
    public async Task<FetchedSource> FetchAsync(ISource source, FetchContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(context);
        if (source is not GitHubSource gh)
        {
            throw new ArgumentException($"Expected a {nameof(GitHubSource)} but got '{source.GetType().Name}'.", nameof(source));
        }

        var ref0 = gh.ResolvedRef;
        var workRoot = Path.Combine(Path.GetTempPath(), "Zakira.Conduit", "fetch", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workRoot);

        var extractedRoot = Path.Combine(workRoot, "extracted");
        Directory.CreateDirectory(extractedRoot);

        try
        {
            _logger.LogInformation("Fetching {Slug} (ref={Ref}, paths=[{Paths}], etagHint={Etag})",
                gh.Slug, ref0 ?? "<default>", string.Join(", ", gh.EffectivePaths.Select(p => p.Path)), context.PreviousEtag ?? "<none>");

            // Resolve the auth header from the per-source chain once and reuse.
            var authHeader = await _credentials.TryGetAsync(gh, cancellationToken).ConfigureAwait(false);

            // 1. Download the zipball once, optionally with If-None-Match.
            var archivePath = Path.Combine(workRoot, "archive.zip");
            GitHubDownloadResult downloadResult;
            await using (var archiveStream = File.Create(archivePath))
            {
                downloadResult = await _downloader.DownloadAsync(
                    gh.Owner,
                    gh.RepoName,
                    ref0,
                    archiveStream,
                    context.PreviousEtag,
                    authHeader,
                    cancellationToken).ConfigureAwait(false);
            }

            // 2a. Short-circuit on 304: cleanup the (empty) temp dir and
            //     return an Unchanged sentinel for the synchronizer.
            if (downloadResult.NotModified)
            {
                Directory.Delete(workRoot, recursive: true);
                return FetchedSource.Unchanged(source, resolvedRef: null, etag: downloadResult.Etag);
            }

            // 2b. Peek the resolved short SHA from the zipball wrapper folder
            //     (useful when the caller asked for a branch).
            string? resolvedShortSha = null;
            await using (var archiveStream = File.OpenRead(archivePath))
            {
                try
                {
                    resolvedShortSha = ZipballExtractor.PeekResolvedShortSha(archiveStream);
                }
                catch
                {
                    // Best-effort: continue with whatever ref the caller gave us.
                }
            }

            // 3. Extract everything once. (Zipballs are small repository snapshots.)
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

            // 4. Build the content unit list.
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
                    var normalized = subPath.Path.Replace('\\', '/').Trim('/');
                    var resolved = Path.Combine(extractedRoot, normalized.Replace('/', Path.DirectorySeparatorChar));

                    if (!Directory.Exists(resolved))
                    {
                        throw new GitHubDownloadException(
                            $"Sub-path '{subPath.Path}' was not found in archive for '{gh.Slug}'.",
                            System.Net.HttpStatusCode.OK);
                    }

                    contents.Add(new FetchedContent(resolved, subPath.ResolvedBasename));
                }
            }

            // Prefer the extracted short SHA when present; otherwise echo back
            // whatever ref the user provided. Commit pins stay verbatim.
            var resolvedRefForState = !string.IsNullOrEmpty(gh.Commit)
                ? gh.Commit
                : (resolvedShortSha ?? ref0);

            return new FetchedSource(
                contents: contents,
                source: source,
                resolvedRef: resolvedRefForState,
                etag: downloadResult.Etag,
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
}
