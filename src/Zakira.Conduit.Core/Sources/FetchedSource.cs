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
///     Holds one or more <see cref="FetchedContent"/> units. The optional
///     cleanup callback is invoked when the value is disposed.
/// </summary>
public sealed class FetchedSource : IAsyncDisposable
{
    private readonly Func<ValueTask>? _cleanup;
    private bool _disposed;

    /// <summary>
    ///     The materialized content units. Always non-empty for a successful fetch.
    /// </summary>
    public IReadOnlyList<FetchedContent> Contents { get; }

    /// <summary>
    ///     Optional concrete ref that was resolved (e.g. commit SHA), for
    ///     logging/diagnostics. May be <see langword="null"/>.
    /// </summary>
    public string? ResolvedRef { get; }

    /// <summary>The originating source, kept for diagnostics.</summary>
    public ISkillSource Source { get; }

    public FetchedSource(IReadOnlyList<FetchedContent> contents, ISkillSource source, string? resolvedRef = null, Func<ValueTask>? cleanup = null)
    {
        ArgumentNullException.ThrowIfNull(contents);
        ArgumentNullException.ThrowIfNull(source);
        if (contents.Count == 0)
        {
            throw new ArgumentException("A fetched source must have at least one content unit.", nameof(contents));
        }

        Contents = contents;
        Source = source;
        ResolvedRef = resolvedRef;
        _cleanup = cleanup;
    }

    /// <summary>
    ///     Convenience factory for the single-unit case.
    /// </summary>
    public static FetchedSource FromSingleDirectory(string contentDirectory, ISkillSource source, string? resolvedRef = null, Func<ValueTask>? cleanup = null) =>
        new(new[] { new FetchedContent(contentDirectory) }, source, resolvedRef, cleanup);

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
