using Zakira.Conduit.Manifest;

namespace Zakira.Conduit.Sources;

/// <summary>
///     The result of fetching a remote source into a local working directory.
///     The directory lives inside a temp scratch area and is cleaned up when
///     the value is disposed.
/// </summary>
public sealed class FetchedSource : IAsyncDisposable
{
    private readonly Func<ValueTask>? _cleanup;
    private bool _disposed;

    /// <summary>
    ///     The local directory containing the fetched files. If the underlying
    ///     source supports sub-paths, this directory is already scoped to that
    ///     sub-path; callers should mirror its contents wholesale.
    /// </summary>
    public string ContentDirectory { get; }

    /// <summary>
    ///     Optional concrete ref that was resolved (e.g. commit SHA), for
    ///     logging/diagnostics. May be <see langword="null"/>.
    /// </summary>
    public string? ResolvedRef { get; }

    /// <summary>
    ///     The originating source, kept for diagnostics.
    /// </summary>
    public ISkillSource Source { get; }

    public FetchedSource(string contentDirectory, ISkillSource source, string? resolvedRef = null, Func<ValueTask>? cleanup = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(contentDirectory);
        ArgumentNullException.ThrowIfNull(source);

        ContentDirectory = contentDirectory;
        Source = source;
        ResolvedRef = resolvedRef;
        _cleanup = cleanup;
    }

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
