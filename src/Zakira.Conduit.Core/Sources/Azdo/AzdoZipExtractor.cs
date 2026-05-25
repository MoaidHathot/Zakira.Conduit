using System.IO.Compression;

namespace Zakira.Conduit.Sources.Azdo;

/// <summary>
///     Extracts the zip stream returned by the AzDO Items API. Unlike GitHub
///     zipballs, AzDO zips do <em>not</em> wrap everything in an
///     <c>owner-repo-sha/</c> folder &mdash; the entries are rooted at the
///     <c>scopePath</c> (or repo root when scope is <c>/</c>).
/// </summary>
internal static class AzdoZipExtractor
{
    /// <summary>
    ///     Extracts <paramref name="archive"/> into <paramref name="destinationDirectory"/>.
    /// </summary>
    /// <returns>The number of files extracted.</returns>
    public static int Extract(Stream archive, string destinationDirectory)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);

        Directory.CreateDirectory(destinationDirectory);

        using var zip = new ZipArchive(archive, ZipArchiveMode.Read, leaveOpen: false);

        var fullDestination = Path.GetFullPath(destinationDirectory);
        var extractedFileCount = 0;

        foreach (var entry in zip.Entries)
        {
            var name = entry.FullName.Replace('\\', '/');
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            // AzDO sometimes emits a leading "/"; strip it so Path.Combine doesn't reroot.
            if (name.StartsWith('/'))
            {
                name = name[1..];
            }

            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            var isDirectoryEntry = name.EndsWith('/');
            var destPath = Path.Combine(fullDestination, name.Replace('/', Path.DirectorySeparatorChar));
            GuardAgainstTraversal(fullDestination, destPath);

            if (isDirectoryEntry)
            {
                Directory.CreateDirectory(destPath.TrimEnd(Path.DirectorySeparatorChar));
                continue;
            }

            var parent = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(parent))
            {
                Directory.CreateDirectory(parent);
            }

            using var entryStream = entry.Open();
            using var fileStream = File.Create(destPath);
            entryStream.CopyTo(fileStream);
            extractedFileCount++;
        }

        return extractedFileCount;
    }

    private static void GuardAgainstTraversal(string rootDirectory, string destPath)
    {
        var resolved = Path.GetFullPath(destPath);
        if (!resolved.StartsWith(rootDirectory + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
            !string.Equals(resolved, rootDirectory, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Archive entry '{destPath}' would escape extraction root '{rootDirectory}'.");
        }
    }
}
