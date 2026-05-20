using Microsoft.Extensions.Logging;
using Zakira.Conduit.Manifest;
using Zakira.Conduit.Paths;

namespace Zakira.Conduit.Sources.Local;

/// <summary>
///     <see cref="ISkillSourceFetcher"/> for <see cref="LocalDirectorySkillSource"/>.
///     <para>
///         Source directories are reused directly &mdash; no copy is made and
///         no cleanup is performed on dispose &mdash; because
///         <see cref="Mirroring.IDirectoryMirror"/> only reads from the content
///         directories it is handed.
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

        var effective = local.EffectivePaths;
        if (effective.Count == 0)
        {
            throw new ArgumentException("Local source must specify at least one path.", nameof(source));
        }

        var contents = new List<FetchedContent>(effective.Count);

        foreach (var rawPath in effective)
        {
            var resolved = _pathResolver.Resolve(rawPath, context.ManifestDirectory);

            if (!Directory.Exists(resolved))
            {
                throw new LocalSourceNotFoundException($"Local source directory does not exist: '{resolved}' (resolved from '{rawPath}').", resolved);
            }

            _logger.LogInformation("Using local source: {Path}", resolved);

            var suggestedName = effective.Count > 1 ? Path.GetFileName(resolved.TrimEnd(Path.DirectorySeparatorChar)) : null;
            contents.Add(new FetchedContent(resolved, suggestedName));
        }

        // No cleanup callback: we don't own user directories.
        return Task.FromResult(new FetchedSource(
            contents: contents,
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
