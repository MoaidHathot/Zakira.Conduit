using System.Text;
using System.Text.Json;

namespace Zakira.Conduit.Manifest;

/// <summary>
///     Narrow JSONC-aware patcher: rewrites a specific set of leaf string
///     values inside a JSON document while preserving every byte outside
///     those values &mdash; including comments, trailing commas, blank lines,
///     indentation, and trailing whitespace. Used by <c>conduit pin</c> /
///     <c>conduit update</c> when their mutation is purely "replace this
///     <c>commit</c> field with a new SHA".
/// </summary>
/// <remarks>
///     <para>
///         Strictly leaf-string-replace only. The patcher refuses to operate
///         (returns <see langword="null"/>) if the target value is missing,
///         is not a JSON string, or if any of the requested paths is
///         ambiguous &mdash; callers fall back to the full
///         <see cref="JsonNodeManifestWriter"/> rewrite, which loses trivia
///         but always succeeds.
///     </para>
///     <para>
///         The path syntax is the same JSON-pointer-ish shape the rest of
///         Conduit uses elsewhere in error messages: <c>entries[3].source.commit</c>.
///     </para>
/// </remarks>
public static class JsonValuePatcher
{
    /// <summary>One value-replacement request.</summary>
    public sealed record StringEdit(string Path, string NewValue);

    /// <summary>
    ///     Attempts to apply every edit. Returns the patched UTF-8 bytes on
    ///     success, or <see langword="null"/> when any edit failed to locate
    ///     a unique JSON string token at its target path.
    /// </summary>
    public static byte[]? TryPatch(byte[] sourceUtf8, IReadOnlyList<StringEdit> edits)
    {
        ArgumentNullException.ThrowIfNull(sourceUtf8);
        ArgumentNullException.ThrowIfNull(edits);

        if (edits.Count == 0)
        {
            return sourceUtf8;
        }

        // Walk the document once, recording every leaf string value's byte
        // span keyed by its path.
        var spans = new Dictionary<string, (int Start, int Length)>(StringComparer.Ordinal);
        try
        {
            CollectStringSpans(sourceUtf8, spans);
        }
        catch (JsonException)
        {
            return null;
        }

        // Resolve each requested edit to its (start, length) and bail if any
        // path doesn't have a matching string leaf.
        var ordered = new List<(int Start, int Length, string NewValue)>(edits.Count);
        foreach (var edit in edits)
        {
            if (!spans.TryGetValue(edit.Path, out var span))
            {
                return null;
            }

            ordered.Add((span.Start, span.Length, edit.NewValue));
        }

        // Apply highest-byte-offset first so earlier offsets stay valid.
        ordered.Sort((a, b) => b.Start.CompareTo(a.Start));

        var buffer = (byte[])sourceUtf8.Clone();
        foreach (var (start, length, newValue) in ordered)
        {
            // Splice in the replacement value (JSON-encoded so backslashes /
            // quotes inside the string survive). The opening and closing
            // double-quote are written by the encoder.
            var encoded = JsonEncodedText.Encode(newValue).EncodedUtf8Bytes.ToArray();
            // We're replacing the existing token INCLUDING its quotes, so we
            // need to wrap the encoded body in quote bytes (0x22) too.
            var replacement = new byte[encoded.Length + 2];
            replacement[0] = (byte)'"';
            Buffer.BlockCopy(encoded, 0, replacement, 1, encoded.Length);
            replacement[^1] = (byte)'"';

            buffer = Splice(buffer, start, length, replacement);
        }

        return buffer;
    }

    private static byte[] Splice(byte[] original, int start, int oldLength, byte[] replacement)
    {
        var result = new byte[original.Length - oldLength + replacement.Length];
        Buffer.BlockCopy(original, 0, result, 0, start);
        Buffer.BlockCopy(replacement, 0, result, start, replacement.Length);
        Buffer.BlockCopy(original, start + oldLength, result, start + replacement.Length, original.Length - start - oldLength);
        return result;
    }

    private static void CollectStringSpans(byte[] sourceUtf8, Dictionary<string, (int Start, int Length)> sink)
    {
        var options = new JsonReaderOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        var reader = new Utf8JsonReader(sourceUtf8, options);
        var pathStack = new List<PathFrame>();

        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.StartObject:
                    pathStack.Add(PathFrame.ForObject());
                    break;

                case JsonTokenType.EndObject:
                    if (pathStack.Count == 0 || !pathStack[^1].IsObject)
                    {
                        throw new JsonException("Unbalanced JSON object braces.");
                    }
                    pathStack.RemoveAt(pathStack.Count - 1);
                    AfterValueComplete(pathStack);
                    break;

                case JsonTokenType.StartArray:
                    pathStack.Add(PathFrame.ForArray());
                    break;

                case JsonTokenType.EndArray:
                    if (pathStack.Count == 0 || pathStack[^1].IsObject)
                    {
                        throw new JsonException("Unbalanced JSON array brackets.");
                    }
                    pathStack.RemoveAt(pathStack.Count - 1);
                    AfterValueComplete(pathStack);
                    break;

                case JsonTokenType.PropertyName:
                    if (pathStack.Count == 0 || !pathStack[^1].IsObject)
                    {
                        throw new JsonException("Property name outside an object.");
                    }
                    var frame = pathStack[^1];
                    frame.PropertyName = reader.GetString();
                    pathStack[^1] = frame;
                    break;

                case JsonTokenType.String:
                    var stringPath = BuildPath(pathStack);
                    if (stringPath is not null)
                    {
                        var start = (int)reader.TokenStartIndex;
                        var length = ScanStringTokenLength(sourceUtf8, start);
                        sink[stringPath] = (start, length);
                    }
                    AfterValueComplete(pathStack);
                    break;

                case JsonTokenType.Number:
                case JsonTokenType.True:
                case JsonTokenType.False:
                case JsonTokenType.Null:
                    AfterValueComplete(pathStack);
                    break;
            }
        }
    }

    /// <summary>
    ///     Bookkeeping run after a value has been fully consumed (whether
    ///     scalar or container). When the now-top frame is an object we
    ///     clear its current property name; when it's an array we bump the
    ///     index so the next element gets the correct one.
    /// </summary>
    private static void AfterValueComplete(List<PathFrame> stack)
    {
        if (stack.Count == 0)
        {
            return;
        }

        var top = stack[^1];
        if (top.IsObject)
        {
            top.PropertyName = null;
        }
        else
        {
            top.ArrayIndex++;
        }
        stack[^1] = top;
    }

    /// <summary>
    ///     Returns the JSON string token's byte length (including the opening
    ///     and closing double-quote). Walks forward from <paramref name="start"/>
    ///     handling backslash escapes; never enters another string because
    ///     opening-quote / closing-quote are matched only once.
    /// </summary>
    private static int ScanStringTokenLength(byte[] sourceUtf8, int start)
    {
        if (start >= sourceUtf8.Length || sourceUtf8[start] != (byte)'"')
        {
            throw new JsonException($"Expected JSON string opening quote at offset {start}.");
        }

        var i = start + 1;
        while (i < sourceUtf8.Length)
        {
            var b = sourceUtf8[i];
            if (b == (byte)'\\' && i + 1 < sourceUtf8.Length)
            {
                // Skip the escaped character. A backslash-u escape consumes
                // 5 more bytes, but the inner ones aren't quotes either way
                // so just advancing past the next byte is enough to dodge
                // accidental quote-matching.
                i += 2;
                continue;
            }

            if (b == (byte)'"')
            {
                return i - start + 1;
            }

            i++;
        }

        throw new JsonException($"Unterminated JSON string starting at offset {start}.");
    }

    private static string? BuildPath(List<PathFrame> stack)
    {
        // Path is built from frames left-to-right. Each object frame
        // contributes its current property name; each array frame contributes
        // its current element index. Both reflect the value about to be
        // visited at the leaf, which is exactly the state we want recorded.
        var sb = new StringBuilder();
        for (var i = 0; i < stack.Count; i++)
        {
            var f = stack[i];
            if (f.IsObject)
            {
                if (f.PropertyName is null)
                {
                    // Object whose property name hasn't been read yet -
                    // unreachable inside Utf8JsonReader's grammar for the
                    // leaf-string case.
                    return null;
                }

                if (sb.Length > 0)
                {
                    sb.Append('.');
                }
                sb.Append(f.PropertyName);
            }
            else
            {
                sb.Append('[').Append(f.ArrayIndex).Append(']');
            }
        }

        return sb.Length == 0 ? null : sb.ToString();
    }

    private struct PathFrame
    {
        public bool IsObject;
        public string? PropertyName;
        public int ArrayIndex;

        public static PathFrame ForObject() => new() { IsObject = true, ArrayIndex = 0 };
        public static PathFrame ForArray() => new() { IsObject = false, ArrayIndex = 0 };
    }
}
