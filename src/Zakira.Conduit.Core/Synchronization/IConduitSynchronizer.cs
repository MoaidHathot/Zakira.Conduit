using Zakira.Conduit.Manifest;

namespace Zakira.Conduit.Synchronization;

/// <summary>
///     The end-to-end conduit sync engine.
/// </summary>
public interface IConduitSynchronizer
{
    /// <summary>
    ///     Synchronizes every selected entry in <paramref name="manifest"/>.
    /// </summary>
    /// <param name="manifest">The validated manifest.</param>
    /// <param name="manifestPath">The on-disk path of the manifest, used to root relative target paths.</param>
    /// <param name="options">Sync options (entry filter, dry-run, ...).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<SyncReport> SyncAsync(ConduitManifest manifest, string manifestPath, SyncOptions options, CancellationToken cancellationToken = default);
}
