using Microsoft.Extensions.Logging;

namespace Zakira.Conduit.Mirroring;

/// <summary>
///     <see cref="IDirectoryMirror"/> that writes content into a sibling
///     <c>.staging-&lt;guid&gt;</c> directory and then swaps the staging
///     directory into place atomically (where the filesystem allows).
/// </summary>
public sealed class AtomicDirectoryMirror : IDirectoryMirror
{
    private readonly ILogger<AtomicDirectoryMirror> _logger;

    public AtomicDirectoryMirror(ILogger<AtomicDirectoryMirror> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<int> MirrorAsync(string sourceDirectory, string targetDirectory, MirrorFilter? filter = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDirectory);

        if (!Directory.Exists(sourceDirectory))
        {
            throw new DirectoryNotFoundException($"Source directory does not exist: '{sourceDirectory}'.");
        }

        var parent = Path.GetDirectoryName(Path.GetFullPath(targetDirectory))
                     ?? throw new ArgumentException("Target path has no parent directory.", nameof(targetDirectory));

        Directory.CreateDirectory(parent);

        var leafName = Path.GetFileName(Path.GetFullPath(targetDirectory));
        var stagingDir = Path.Combine(parent, $".{leafName}.staging-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(stagingDir);
            var sourceRoot = Path.GetFullPath(sourceDirectory);
            var effectiveFilter = filter ?? MirrorFilter.MatchEverything;
            var copied = await CopyDirectoryAsync(sourceRoot, sourceRoot, stagingDir, effectiveFilter, cancellationToken).ConfigureAwait(false);

            // Swap: move current target aside, move staging into place, then delete the aside.
            string? aside = null;
            if (Directory.Exists(targetDirectory))
            {
                aside = Path.Combine(parent, $".{leafName}.replaced-{Guid.NewGuid():N}");
                Directory.Move(targetDirectory, aside);
            }

            Directory.Move(stagingDir, targetDirectory);

            if (aside is not null)
            {
                try
                {
                    Directory.Delete(aside, recursive: true);
                }
                catch (IOException ex)
                {
                    _logger.LogWarning(ex, "Failed to delete replaced directory '{Dir}'", aside);
                }
            }

            _logger.LogDebug("Mirrored {Count} files into '{Target}'", copied, targetDirectory);
            return copied;
        }
        catch
        {
            if (Directory.Exists(stagingDir))
            {
                try
                {
                    Directory.Delete(stagingDir, recursive: true);
                }
                catch (IOException)
                {
                    // best-effort
                }
            }

            throw;
        }
    }

    private static async Task<int> CopyDirectoryAsync(
        string sourceRoot,
        string sourceDir,
        string destDir,
        MirrorFilter filter,
        CancellationToken cancellationToken)
    {
        // Defer Directory.CreateDirectory(destDir) until we know at least one
        // file actually lands in it; otherwise filters that exclude an entire
        // subtree would leak empty mirror dirs. The caller pre-creates the
        // top-level staging dir, so this only affects nested directories.
        var ensureCreated = sourceDir == sourceRoot;
        if (ensureCreated)
        {
            Directory.CreateDirectory(destDir);
        }

        var count = 0;

        foreach (var entry in Directory.EnumerateFileSystemEntries(sourceDir, "*", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = Path.GetFileName(entry);
            var destEntry = Path.Combine(destDir, name);

            if (Directory.Exists(entry))
            {
                count += await CopyDirectoryAsync(sourceRoot, entry, destEntry, filter, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                if (!filter.MatchesEverything && !filter.ShouldInclude(GetRelativePosixPath(sourceRoot, entry)))
                {
                    continue;
                }

                if (!ensureCreated && !Directory.Exists(destDir))
                {
                    Directory.CreateDirectory(destDir);
                }

                await using (var src = File.OpenRead(entry))
                await using (var dst = File.Create(destEntry))
                {
                    await src.CopyToAsync(dst, cancellationToken).ConfigureAwait(false);
                }

                count++;
            }
        }

        return count;
    }

    private static string GetRelativePosixPath(string sourceRoot, string absoluteFilePath)
    {
        var relative = Path.GetRelativePath(sourceRoot, absoluteFilePath);
        return relative.Replace('\\', '/');
    }
}
