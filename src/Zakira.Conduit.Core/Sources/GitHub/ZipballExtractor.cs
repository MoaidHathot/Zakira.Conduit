using System.IO.Compression;

namespace Zakira.Conduit.Sources.GitHub;

/// <summary>
///     Extracts a GitHub zipball archive. GitHub wraps everything in a single
///     top-level folder named like <c>owner-repo-shortsha/</c>; this extractor
///     strips that prefix and (optionally) further filters to a sub-path.
/// </summary>
internal static class ZipballExtractor
{
    /// <summary>
    ///     Extracts <paramref name="archive"/> into <paramref name="destinationDirectory"/>.
    ///     When <paramref name="subPath"/> is non-empty, only entries inside
    ///     that sub-path are extracted, and the sub-path becomes the root of
    ///     the destination.
    /// </summary>
    /// <returns>The number of files extracted.</returns>
    public static int Extract(Stream archive, string destinationDirectory, string? subPath)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);

        Directory.CreateDirectory(destinationDirectory);

        using var zip = new ZipArchive(archive, ZipArchiveMode.Read, leaveOpen: false);

        var normalizedSubPath = NormalizeSubPath(subPath);
        var fullDestination = Path.GetFullPath(destinationDirectory);
        var extractedFileCount = 0;

        foreach (var entry in zip.Entries)
        {
            var relative = StripFirstSegment(entry.FullName);
            if (relative is null)
            {
                continue;
            }

            // Skip entries outside the requested sub-path.
            if (normalizedSubPath is not null)
            {
                if (!relative.StartsWith(normalizedSubPath + '/', StringComparison.Ordinal) && !string.Equals(relative, normalizedSubPath, StringComparison.Ordinal))
                {
                    continue;
                }

                relative = relative.Length == normalizedSubPath.Length
                    ? string.Empty
                    : relative[(normalizedSubPath.Length + 1)..];
            }

            // Skip pure-directory entries with no relative payload (zipballs contain them).
            var isDirectoryEntry = entry.FullName.EndsWith('/');
            if (isDirectoryEntry || relative.Length == 0)
            {
                if (relative.Length > 0)
                {
                    var dirPath = Path.Combine(fullDestination, relative.Replace('/', Path.DirectorySeparatorChar));
                    GuardAgainstTraversal(fullDestination, dirPath);
                    Directory.CreateDirectory(dirPath);
                }

                continue;
            }

            var destPath = Path.Combine(fullDestination, relative.Replace('/', Path.DirectorySeparatorChar));
            GuardAgainstTraversal(fullDestination, destPath);

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

    private static string? StripFirstSegment(string entryFullName)
    {
        var slash = entryFullName.IndexOf('/');
        if (slash < 0)
        {
            // Top-level dummy entry (no second path component). Skip.
            return null;
        }

        return entryFullName[(slash + 1)..];
    }

    private static string? NormalizeSubPath(string? subPath)
    {
        if (string.IsNullOrWhiteSpace(subPath))
        {
            return null;
        }

        return subPath.Replace('\\', '/').Trim('/');
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
