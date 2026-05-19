using System.IO.Compression;

namespace Zakira.Conduit.Core.UnitTests.TestHelpers;

/// <summary>
///     Builds an in-memory GitHub-style zipball: a single top-level folder
///     <c>{owner}-{repo}-{shortsha}/</c> containing the given entries.
/// </summary>
internal static class ZipballFactory
{
    public static byte[] CreateZipball(string topFolder, IReadOnlyDictionary<string, string> filesByRelativePath)
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            // Top-level directory entry, to match what GitHub emits.
            archive.CreateEntry(topFolder + "/");

            foreach (var (relPath, content) in filesByRelativePath)
            {
                var normalized = relPath.Replace('\\', '/').TrimStart('/');
                var entry = archive.CreateEntry($"{topFolder}/{normalized}");
                using var stream = entry.Open();
                using var writer = new StreamWriter(stream);
                writer.Write(content);
            }
        }

        return ms.ToArray();
    }
}
