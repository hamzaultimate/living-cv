using System.Text.Json;

namespace Portfolio.Web.Transfer;

/// <summary>A file that was read: its parsed JSON (the caller disposes it), or why the whole file was refused.</summary>
public sealed class ReadResult : IDisposable
{
    public JsonDocument? Document { get; init; }
    public string? Refusal { get; init; }
    public static ReadResult Refused(string reason) => new() { Refusal = reason };
    public void Dispose() => Document?.Dispose();
}

/// <summary>
/// Turns the uploaded bytes into JSON, or refuses the whole file. Everything is untrusted: the size is capped before and while reading, nesting is capped, a
/// property written twice in one object is refused (the parser would otherwise let the last one win, so two readers of one file could disagree), comments
/// and trailing commas are refused, and the file must name a schema version this build understands. Nothing here touches the database.
/// </summary>
public static class ImportFileReader
{
    private static readonly JsonDocumentOptions Options = new()
    {
        MaxDepth = TransferLimits.MaxJsonDepth,
        AllowDuplicateProperties = false,
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
    };

    /// <summary>The largest size, in words people use, for a message.</summary>
    public static string SizeLabel => $"{TransferLimits.MaxFileBytes / (1024 * 1024)} MB";

    public static async Task<ReadResult> ReadAsync(Stream? content, long declaredLength, CancellationToken ct)
    {
        if (content is null || declaredLength <= 0) return ReadResult.Refused("Choose a file to import.");
        if (declaredLength > TransferLimits.MaxFileBytes) return ReadResult.Refused($"That file is larger than {SizeLabel}, so it was not read.");

        // Read at most one byte more than the cap: a body that lied about its length is cut off here, not buffered.
        var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await content.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > TransferLimits.MaxFileBytes) return ReadResult.Refused($"That file is larger than {SizeLabel}, so it was not read.");
            buffer.Write(chunk, 0, read);
        }
        return Parse(buffer.ToArray());
    }

    /// <summary>Parses file bytes already in memory (the caller has applied the size cap).</summary>
    public static ReadResult Parse(byte[] bytes)
    {
        var start = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0; // a UTF-8 byte order mark, which some editors add
        if (bytes.Length - start == 0) return ReadResult.Refused("That file is empty.");
        // The parser reads a string's bytes lazily, so text that is not UTF-8 would only fail later, in the middle of a row. It is refused up front.
        if (!System.Text.Unicode.Utf8.IsValid(bytes.AsSpan(start))) return ReadResult.Refused("That file is not valid UTF-8 text (it may be saved in another encoding; save it as UTF-8).");

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(bytes.AsMemory(start), Options);
        }
        catch (JsonException ex)
        {
            return ReadResult.Refused(Describe(ex));
        }
        catch (InvalidOperationException)
        {
            // The parser reads the names of a repeated-property check as text, so half of a character written as an escape in a name surfaces here.
            return ReadResult.Refused(NotText);
        }

        var root = document.RootElement;
        var refusal = CheckHeader(root) ?? CheckText(root);
        if (refusal is not null)
        {
            document.Dispose();
            return ReadResult.Refused(refusal);
        }
        return new ReadResult { Document = document };
    }

    private static string Describe(JsonException ex)
    {
        // The parser's own text can quote part of the file, so the message is built from what kind of fault it was.
        var where = ex.LineNumber is { } line ? $" Near line {line + 1}." : "";
        if (ex.Message.Contains("maximum configured depth", StringComparison.Ordinal))
            return $"That file is nested more than {TransferLimits.MaxJsonDepth} levels deep, which an export never is, so it was not read.{where}";
        if (ex.Message.StartsWith("Duplicate property", StringComparison.Ordinal))
            return "That file has the same property twice in one object, so it is ambiguous and was not read.";
        return $"That file is not valid JSON (it may be cut off, or saved in another text encoding; use UTF-8).{where}";
    }

    /// <summary>
    /// Every string and property name must be readable text. JSON can escape half of a surrogate pair on its own (<c>"\ud800"</c>), which is not text in any encoding and which the
    /// parser only reports when the string is read, so a file that has one is refused here, once, instead of failing inside a row.
    /// </summary>
    private static string? CheckText(JsonElement root)
    {
        var pending = new Stack<JsonElement>();
        pending.Push(root);
        try
        {
            while (pending.Count > 0)
            {
                var element = pending.Pop();
                switch (element.ValueKind)
                {
                    case JsonValueKind.String:
                        _ = element.GetString();
                        break;
                    case JsonValueKind.Array:
                        foreach (var item in element.EnumerateArray()) pending.Push(item);
                        break;
                    case JsonValueKind.Object:
                        foreach (var property in element.EnumerateObject())
                        {
                            _ = property.Name;
                            pending.Push(property.Value);
                        }
                        break;
                }
            }
        }
        catch (InvalidOperationException)
        {
            return NotText;
        }
        return null;
    }

    private const string NotText = "That file has text that is not valid (for example half of a character written as an escape such as \\ud800), so it was not read.";

    private static string? CheckHeader(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return "That file is JSON, but not an export: the top level must be an object.";
        if (root.TryGetProperty("format", out var format) && !(format.ValueKind == JsonValueKind.String && format.GetString() == TransferLimits.Format))
            return $"That file says it is another kind of file, not an export (format \"{TransferLimits.Format}\").";
        if (!root.TryGetProperty("schemaVersion", out var version))
            return "That file has no schemaVersion, so it is not an export this version can read.";
        if (version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var number))
            return "The schemaVersion of that file must be a whole number.";
        if (number > TransferLimits.SchemaVersion)
            return $"That file has schemaVersion {number}, which is newer than this version understands (it reads {TransferLimits.SchemaVersion}). Update the site, or export again from an older one.";
        if (number != TransferLimits.SchemaVersion)
            return $"That file has schemaVersion {number}, which this version cannot read (it reads {TransferLimits.SchemaVersion}).";
        return null;
    }
}
