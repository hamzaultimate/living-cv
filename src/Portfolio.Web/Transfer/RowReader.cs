using System.Text.Json;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Portfolio.Web.Services;
using Portfolio.Web.Uploads;

namespace Portfolio.Web.Transfer;

/// <summary>A value read from a JSON object: whether the file mentions the field at all (an absent field is left as it is on an update), and what it says.</summary>
public readonly record struct Got<T>(bool Present, T? Value)
{
    public static Got<T> Absent => new(false, default);
}

/// <summary>The changes one row will make, as steps over the row itself, plus the warnings that were raised while reading it.</summary>
public sealed class Patch<T>
{
    public List<Action<T>> Steps { get; } = [];
    public List<string> Warnings { get; } = [];

    /// <summary>The group the row ends up in, for a table that orders within groups.</summary>
    public string? Group { get; set; }

    /// <summary>Writes the field if the file mentions it.</summary>
    public void Set<V>(Got<V> got, Action<T, V?> write)
    {
        if (got.Present) Steps.Add(row => write(row, got.Value));
    }

    public void Add(Action<T> step) => Steps.Add(step);
}

/// <summary>What planning shares across every row: the words, the clock, the uploads folder, and what was ignored.</summary>
public sealed class PlanContext
{
    public required Func<string, EditorView> Views { get; init; }
    public required TimeProvider Clock { get; init; }
    public required UploadStore Uploads { get; init; }

    /// <summary>Properties in the file that nothing reads (any object): they are ignored and counted.</summary>
    public int UnknownProperties { get; set; }
}

/// <summary>
/// Reads one JSON object (a row of the file, or a block of the profile) with the rules of the editor for the same fields. Values are checked with
/// <see cref="FormCheck"/>, so the messages and the limits are the editors' own. A field the file does not mention is left alone (an update keeps it; a
/// new row needs the required ones); a field it names as null is cleared.
/// </summary>
public sealed class RowReader
{
    private readonly JsonElement _object;

    public RowReader(JsonElement obj, ModelStateDictionary state, FormCheck check, EditorView view, bool create, PlanContext context, IReadOnlyCollection<string> known)
    {
        _object = obj;
        State = state;
        Check = check;
        View = view;
        Label = ImportRules.Labels(view);
        Create = create;
        KeepsStored = !create;
        Context = context;
        foreach (var property in obj.EnumerateObject())
            if (!known.Contains(property.Name, StringComparer.Ordinal)) context.UnknownProperties++;
    }

    /// <summary>How a message names a field. The template's words by default.</summary>
    public Func<string, string> Label { get; init; }

    public ModelStateDictionary State { get; }
    public FormCheck Check { get; }
    public EditorView View { get; }
    public PlanContext Context { get; }

    /// <summary>True when the row being read already exists, so a value that is dropped leaves the stored one alone (a nested entry takes this from its row).</summary>
    public bool KeepsStored { get; init; }

    /// <summary>True for a row that does not exist yet: its required fields must all be in the file. An entry nested in a row (a link, an image) always needs all of its fields.</summary>
    public bool Create { get; }

    public bool Has(string name) => _object.TryGetProperty(name, out _);

    public JsonElement? Raw(string name) => _object.TryGetProperty(name, out var e) ? e : null;

    /// <summary>The errors recorded so far, as sentences.</summary>
    public List<string> Errors() => State.Where(e => e.Value?.Errors.Count > 0).SelectMany(e => e.Value!.Errors.Select(x => x.ErrorMessage)).ToList();

    private void NeedsValue(string name, string key, bool required)
    {
        if (Create && required) Check.Error(P(name), $"{Label(key)} is required.");
    }

    /// <summary>A string value, or null for JSON null. A value of another type is an error (and reads as absent).</summary>
    private bool TryString(string name, string key, out string? value)
    {
        value = null;
        if (!_object.TryGetProperty(name, out var e)) return false;
        switch (e.ValueKind)
        {
            case JsonValueKind.Null: return true;
            case JsonValueKind.String: value = e.GetString(); return true;
            default: Check.Error(P(name), $"{Label(key)} must be text."); return false;
        }
    }

    public bool TryBool(string name, string key, out bool value)
    {
        value = false;
        if (!_object.TryGetProperty(name, out var e)) return false;
        if (e.ValueKind is JsonValueKind.True or JsonValueKind.False) { value = e.GetBoolean(); return true; }
        Check.Error(P(name), $"{Label(key)} must be true or false.");
        return false;
    }

    /// <summary>A single-line plain-text field (<see cref="FormCheck.Text"/>).</summary>
    public Got<string?> Text(string name, string key, int max, bool required = false)
    {
        if (!TryString(name, key, out var raw))
        {
            if (!_object.TryGetProperty(name, out _)) NeedsValue(name, key, required);
            return Got<string?>.Absent;
        }
        return new Got<string?>(true, Check.Text(P(name), key, raw, max, required, RowLabel));
    }

    /// <summary>A multi-line plain-text field (<see cref="FormCheck.Paragraph"/>).</summary>
    public Got<string?> Paragraph(string name, string key, int max, bool required = false)
    {
        if (!TryString(name, key, out var raw))
        {
            if (!_object.TryGetProperty(name, out _)) NeedsValue(name, key, required);
            return Got<string?>.Absent;
        }
        return new Got<string?>(true, Check.Paragraph(P(name), key, raw, max, required));
    }

    /// <summary>A web address (http or https, or one of the site's own bundled files).</summary>
    public Got<string?> Url(string name, string key, int max)
    {
        if (!TryString(name, key, out var raw)) return Got<string?>.Absent;
        return new Got<string?>(true, Check.Url(P(name), key, raw, max, ImportRules.IsBundled(raw?.Trim()) ? raw?.Trim() : null));
    }

    /// <summary>A flag. Absent stays as it is.</summary>
    public Got<bool> Flag(string name, string key) => TryBool(name, key, out var v) ? new Got<bool>(true, v) : Got<bool>.Absent;

    /// <summary>The entries of a JSON array of strings (null entries read as empty). Anything else is an error.</summary>
    public bool TryStrings(string name, string key, out List<string?> items)
    {
        items = [];
        if (!_object.TryGetProperty(name, out var e)) return false;
        if (e.ValueKind == JsonValueKind.Null) return true;
        if (e.ValueKind != JsonValueKind.Array) { Check.Error(P(name), $"{Label(key)} must be a list."); return false; }
        foreach (var item in e.EnumerateArray())
        {
            if (item.ValueKind is not (JsonValueKind.String or JsonValueKind.Null)) { Check.Error(P(name), $"{Label(key)} must be a list of text entries."); return false; }
            items.Add(item.GetString());
        }
        return true;
    }

    /// <summary>A list of single-line entries (<see cref="FormCheck.Items"/>).</summary>
    public Got<List<string>> Lines(string name, string key, int maxItems, int maxLength)
    {
        if (!TryStrings(name, key, out var items)) return Got<List<string>>.Absent;
        return new Got<List<string>>(true, Check.Items(P(name), key, items, maxItems, maxLength));
    }

    /// <summary>The objects of a JSON array. Anything else is an error.</summary>
    public bool TryObjects(string name, string key, out List<JsonElement> items)
    {
        items = [];
        if (!_object.TryGetProperty(name, out var e)) return false;
        if (e.ValueKind == JsonValueKind.Null) return true;
        if (e.ValueKind != JsonValueKind.Array) { Check.Error(P(name), $"{Label(key)} must be a list."); return false; }
        foreach (var item in e.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) { Check.Error(P(name), $"{Label(key)} must be a list of entries, each with its own fields."); return false; }
            items.Add(item);
        }
        return true;
    }

    /// <summary>Reads an object nested in this one (a link, a gallery image, a metric) with the same checks: its fields are recorded under <paramref name="path"/> and its messages name <paramref name="rowLabel"/>.</summary>
    public RowReader Nested(JsonElement obj, string path, string rowLabel, IReadOnlyCollection<string> known) =>
        new(obj, State, Check, View, true, Context, known) { Path = path + ".", RowLabel = rowLabel, Warnings = Warnings, Label = Label, KeepsStored = KeepsStored };

    /// <summary>The prefix of the property names this reader records errors under (empty for a row).</summary>
    public string Path { get; init; } = "";

    /// <summary>Names the row in a message ("Metric 2"), for a reader of a nested entry.</summary>
    public string? RowLabel { get; init; }

    private string P(string name) => Path + name;

    /// <summary>A one-line image or file reference, held to the image rules (see <see cref="ImportRules.Image"/>).</summary>
    public Got<string?> Image(string name, string key, int max) => Image(name, key, max, out _);

    /// <summary>An image field. A reference that cannot be kept (see <see cref="ImportRules.Image"/>) reads as absent, <paramref name="dropped"/> true: it never clears a stored image, only a JSON null does.</summary>
    public Got<string?> Image(string name, string key, int max, out bool dropped)
    {
        dropped = false;
        if (!TryString(name, key, out var raw)) return Got<string?>.Absent;
        var value = ImportRules.Image(this, P(name), key, raw, max, out dropped);
        return dropped ? Got<string?>.Absent : new Got<string?>(true, value);
    }

    /// <summary>The value of <paramref name="name"/> as it should be compared as a key: trimmed text, or null when absent, not text or empty.</summary>
    public static string? Peek(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.String ? ContentRules.Trim(e.GetString()) : null;

    /// <summary>The warnings raised while reading this row (an image reference that was dropped, for example).</summary>
    public List<string> Warnings { get; init; } = [];
}
