using Zakira.Conduit.Manifest;

namespace Zakira.Conduit.Sources;

/// <summary>
///     One materialized piece of content produced by an <see cref="ISkillSourceFetcher"/>.
///     A single source may produce several of these &mdash; for example a GitHub
///     source with multiple <c>paths</c>, or a local source pointing at several
///     directories.
/// </summary>
/// <param name="ContentDirectory">
///     Absolute path to the directory whose contents the mirror should copy.
/// </param>
/// <param name="SuggestedDestinationName">
///     A hint used when the entry mirrors more than one content unit: the
///     basename that should be used inside each target directory. Ignored when
///     the entry has exactly one content unit (the entry name wins in that case).
/// </param>
public sealed record FetchedContent(string ContentDirectory, string? SuggestedDestinationName = null);

/// <summary>
///     The result of fetching a remote source into local working directories.
///     Holds one or more <see cref="FetchedContent"/> units &mdash; or none, when
///     <see cref="NotModified"/> is <see langword="true"/>. The optional cleanup
///     callback is invoked when the value is disposed.
/// </summary>
public sealed class FetchedSource : IAsyncDisposable
{
    private readonly Func<ValueTask>? _cleanup;
    private bool _disposed;

    /// <summary>
    ///     The materialized content units. Non-empty for a successful fetch;
    ///     empty when <see cref="NotModified"/> is <see langword="true"/>.
    /// </summary>
    public IReadOnlyList<FetchedContent> Contents { get; }

    /// <summary>
    ///     <see langword="true"/> when the fetcher determined that the source
    ///     is unchanged since the last fetch (e.g. an HTTP 304 against the
    ///     supplied ETag). Callers should skip mirroring in this case.
    /// </summary>
    public bool NotModified { get; }

    /// <summary>
    ///     Optional concrete ref that was resolved (e.g. commit SHA), for
    ///     logging/diagnostics. May be <see langword="null"/>.
    /// </summary>
    public string? ResolvedRef { get; }

    /// <summary>
    ///     Optional cache validator returned by the source (typically an HTTP
    ///     ETag). When set, callers should store it for the next sync.
    /// </summary>
    public string? Etag { get; }

    /// <summary>The originating source, kept for diagnostics.</summary>
    public ISkillSource Source { get; }

    public FetchedSource(
        IReadOnlyList<FetchedContent> contents,
        ISkillSource source,
        string? resolvedRef = null,
        string? etag = null,
        bool notModified = false,
        Func<ValueTask>? cleanup = null)
    {
        ArgumentNullException.ThrowIfNull(contents);
        ArgumentNullException.ThrowIfNull(source);
        if (!notModified && contents.Count == 0)
        {
            throw new ArgumentException("A fetched source must have at least one content unit unless NotModified is true.", nameof(contents));
        }

        Contents = contents;
        Source = source;
        ResolvedRef = resolvedRef;
        Etag = etag;
        NotModified = notModified;
        _cleanup = cleanup;
    }

    /// <summary>
    ///     Convenience factory for the single-unit case.
    /// </summary>
    public static FetchedSource FromSingleDirectory(string contentDirectory, ISkillSource source, string? resolvedRef = null, string? etag = null, Func<ValueTask>? cleanup = null) =>
        new(new[] { new FetchedContent(contentDirectory) }, source, resolvedRef, etag, notModified: false, cleanup);

    /// <summary>
    ///     Convenience factory for the "nothing changed" case.
    /// </summary>
    public static FetchedSource Unchanged(ISkillSource source, string? resolvedRef = null, string? etag = null) =>
        new(Array.Empty<FetchedContent>(), source, resolvedRef, etag, notModified: true, cleanup: null);

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_cleanup is not null)
        {
            await _cleanup().ConfigureAwait(false);
        }
    }
}
