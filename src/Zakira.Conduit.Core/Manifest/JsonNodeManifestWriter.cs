using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Zakira.Conduit.Manifest;

/// <summary>
///     Default <see cref="IManifestWriter"/>. Loads the on-disk manifest as a
///     <see cref="JsonNode"/> tree so field order and unknown properties are
///     preserved across the rewrite, then commits a new file via the standard
///     temp-file + rename pattern. A <c>.bak</c> copy is left next to the
///     original so manual recovery is one shell command away.
/// </summary>
public sealed class JsonNodeManifestWriter : IManifestWriter
{
    private static readonly JsonNodeOptions NodeOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>The suffix appended to the original manifest for the backup copy.</summary>
    public const string BackupSuffix = ".bak";

    /// <inheritdoc />
    public async Task<string> RewriteAsync(string manifestPath, Action<JsonObject> mutate, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);
        ArgumentNullException.ThrowIfNull(mutate);

        if (!File.Exists(manifestPath))
        {
            throw new ManifestException($"Manifest file not found: '{manifestPath}'.", manifestPath);
        }

        var fullPath = Path.GetFullPath(manifestPath);
        var directory = Path.GetDirectoryName(fullPath)
                        ?? throw new ArgumentException("Manifest path has no parent directory.", nameof(manifestPath));

        // 1. Parse the existing file into a mutable JSON tree.
        var raw = await File.ReadAllTextAsync(fullPath, cancellationToken).ConfigureAwait(false);

        JsonNode? rootNode;
        try
        {
            rootNode = JsonNode.Parse(raw, NodeOptions, DocumentOptions);
        }
        catch (JsonException ex)
        {
            throw new ManifestException($"Manifest file '{fullPath}' is not valid JSON: {ex.Message}", fullPath, innerException: ex);
        }

        if (rootNode is not JsonObject root)
        {
            throw new ManifestException($"Manifest file '{fullPath}' must contain a top-level JSON object.", fullPath);
        }

        // 2. Let the caller mutate the tree.
        mutate(root);

        // 3. Backup the original.
        var backupPath = fullPath + BackupSuffix;
        File.Copy(fullPath, backupPath, overwrite: true);

        // 4. Write atomically.
        var tempPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.tmp-{Guid.NewGuid():N}");
        try
        {
            var serialized = root.ToJsonString(WriteOptions);
            await File.WriteAllTextAsync(tempPath, serialized + "\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken).ConfigureAwait(false);
            File.Move(tempPath, fullPath, overwrite: true);
        }
        catch
        {
            try { if (File.Exists(tempPath)) { File.Delete(tempPath); } } catch { /* best-effort */ }
            throw;
        }

        return backupPath;
    }

    /// <inheritdoc />
    public async Task<(bool Patched, string? BackupPath)> ReplaceStringLeavesAsync(
        string manifestPath,
        IReadOnlyList<JsonValuePatcher.StringEdit> edits,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);
        ArgumentNullException.ThrowIfNull(edits);

        if (edits.Count == 0)
        {
            return (false, null);
        }

        if (!File.Exists(manifestPath))
        {
            throw new ManifestException($"Manifest file not found: '{manifestPath}'.", manifestPath);
        }

        var fullPath = Path.GetFullPath(manifestPath);
        var directory = Path.GetDirectoryName(fullPath)
                        ?? throw new ArgumentException("Manifest path has no parent directory.", nameof(manifestPath));

        var sourceBytes = await File.ReadAllBytesAsync(fullPath, cancellationToken).ConfigureAwait(false);
        var patched = JsonValuePatcher.TryPatch(sourceBytes, edits);
        if (patched is null)
        {
            return (false, null);
        }

        // Bytes are unchanged when every requested value already equalled its
        // proposed new value. Skip the disk write so we don't churn timestamps
        // for a no-op pin/update.
        if (patched.Length == sourceBytes.Length && patched.AsSpan().SequenceEqual(sourceBytes))
        {
            return (true, null);
        }

        var backupPath = fullPath + BackupSuffix;
        File.Copy(fullPath, backupPath, overwrite: true);

        var tempPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.tmp-{Guid.NewGuid():N}");
        try
        {
            await File.WriteAllBytesAsync(tempPath, patched, cancellationToken).ConfigureAwait(false);
            File.Move(tempPath, fullPath, overwrite: true);
        }
        catch
        {
            try { if (File.Exists(tempPath)) { File.Delete(tempPath); } } catch { /* best-effort */ }
            throw;
        }

        return (true, backupPath);
    }
}
