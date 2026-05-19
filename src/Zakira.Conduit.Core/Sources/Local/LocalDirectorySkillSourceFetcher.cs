using Microsoft.Extensions.Logging;
using Zakira.Conduit.Manifest;
using Zakira.Conduit.Paths;

namespace Zakira.Conduit.Sources.Local;

/// <summary>
///     <see cref="ISkillSourceFetcher"/> for <see cref="LocalDirectorySkillSource"/>.
///     <para>
///         The source directory is reused directly &mdash; no copy is made and
///         no cleanup is performed on dispose &mdash; because the
///         <see cref="Mirroring.IDirectoryMirror"/> only reads from the content
///         directory it is handed.
///     </para>
/// </summary>
public sealed class LocalDirectorySkillSourceFetcher : ISkillSourceFetcher
{
    private readonly IPathResolver _pathResolver;
    private readonly ILogger<LocalDirectorySkillSourceFetcher> _logger;

    public LocalDirectorySkillSourceFetcher(IPathResolver pathResolver, ILogger<LocalDirectorySkillSourceFetcher> logger)
    {
        ArgumentNullException.ThrowIfNull(pathResolver);
        ArgumentNullException.ThrowIfNull(logger);

        _pathResolver = pathResolver;
        _logger = logger;
    }

    /// <inheritdoc />
    public string SourceKind => LocalDirectorySkillSource.TypeDiscriminator;

    /// <inheritdoc />
    public Task<FetchedSource> FetchAsync(ISkillSource source, FetchContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(context);

        if (source is not LocalDirectorySkillSource local)
        {
            throw new ArgumentException($"Expected a {nameof(LocalDirectorySkillSource)} but got '{source.GetType().Name}'.", nameof(source));
        }

        var resolved = _pathResolver.Resolve(local.Path, context.ManifestDirectory);

        if (!Directory.Exists(resolved))
        {
            throw new LocalSourceNotFoundException($"Local source directory does not exist: '{resolved}' (resolved from '{local.Path}').", resolved);
        }

        _logger.LogInformation("Using local source: {Path}", resolved);

        // No cleanup callback: we don't own the user's directory.
        return Task.FromResult(new FetchedSource(
            contentDirectory: resolved,
            source: source,
            resolvedRef: null,
            cleanup: null));
    }
}

/// <summary>
///     Raised when a configured local source directory does not exist.
/// </summary>
public sealed class LocalSourceNotFoundException : Exception
{
    /// <summary>The resolved absolute path that was searched.</summary>
    public string ResolvedPath { get; }

    public LocalSourceNotFoundException(string message, string resolvedPath, Exception? innerException = null)
        : base(message, innerException)
    {
        ResolvedPath = resolvedPath;
    }
}
