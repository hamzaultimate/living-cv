using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Portfolio.Web.Services;

/// <summary>
/// Validates the fields of a content editor form and records friendly errors under the field names the page binds (<c>Form.Title</c>).
/// Each method returns the cleaned value, so a form is checked and normalised in one pass. A field the template hides is never passed
/// through here, so a crafted post cannot write to it.
/// </summary>
public sealed class FormCheck
{
    private readonly ModelStateDictionary _state;
    private readonly EditorView _view;
    private readonly string _prefix;

    public FormCheck(ModelStateDictionary state, EditorView view, string prefix = "Form")
    {
        _state = state;
        _view = view;
        _prefix = prefix;
    }

    public bool Ok => _state.IsValid;

    /// <summary>True while no error has been recorded for <paramref name="property"/>.</summary>
    public bool IsValid(string property) => !(_state.TryGetValue($"{_prefix}.{property}", out var entry) && entry.Errors.Count > 0);

    public void Error(string property, string message) => _state.AddModelError($"{_prefix}.{property}", message);

    /// <summary>A single-line plain-text value.</summary>
    public string? Text(string property, string key, string? value, int max, bool required = false)
    {
        var label = _view.Label(key);
        var text = ContentRules.Trim(value);
        if (text is null)
        {
            if (required) Error(property, $"{label} is required.");
            return null;
        }
        if (text.Length > max) Error(property, $"{label} can be at most {max} characters.");
        else if (ContentRules.HasControlCharacters(text)) Error(property, $"{label} contains characters that are not allowed.");
        else if (ContentRules.HasMarkup(text)) Error(property, $"{label} is plain text: remove anything that looks like a tag, such as <b>.");
        return text;
    }

    /// <summary>A multi-line plain-text value (paragraphs keep their line breaks).</summary>
    public string? Paragraph(string property, string key, string? value, int max, bool required = false)
    {
        var label = _view.Label(key);
        var text = ContentRules.Paragraph(value);
        if (text is null)
        {
            if (required) Error(property, $"{label} is required.");
            return null;
        }
        if (text.Length > max) Error(property, $"{label} can be at most {max} characters.");
        else if (ContentRules.HasControlCharacters(text, multiline: true)) Error(property, $"{label} contains characters that are not allowed.");
        else if (ContentRules.HasMarkup(text)) Error(property, $"{label} is plain text: remove anything that looks like a tag, such as <b>.");
        return text;
    }

    /// <summary>One entry per line of a text area, each plain text.</summary>
    public List<string> Lines(string property, string key, string? value, int maxLines, int maxLength)
    {
        var label = _view.Label(key);
        var lines = ContentRules.Lines(value);
        if (lines.Count > maxLines) Error(property, $"{label}: at most {maxLines} lines.");
        else if (lines.Any(l => l.Length > maxLength)) Error(property, $"{label}: each line can be at most {maxLength} characters.");
        else if (lines.Any(l => ContentRules.HasControlCharacters(l))) Error(property, $"{label} contains characters that are not allowed.");
        else if (lines.Any(ContentRules.HasMarkup)) Error(property, $"{label} is plain text: remove anything that looks like a tag, such as <b>.");
        return lines;
    }

    /// <summary>An optional web address. A site-relative value is accepted only when it is the one already <paramref name="stored"/>.</summary>
    public string? Url(string property, string key, string? value, int max, string? stored = null)
    {
        var label = _view.Label(key);
        var text = ContentRules.Trim(value);
        if (text is null) return null;
        if (text.Length > max) Error(property, $"{label} can be at most {max} characters.");
        else if (!ContentRules.IsAcceptableUrl(text, stored)) Error(property, $"{label} must be a full web address starting with http:// or https://.");
        return text;
    }

    /// <summary>One of <paramref name="allowed"/>; anything else is refused, whatever the browser sent.</summary>
    public string? Choice(string property, string key, string? value, IEnumerable<string> allowed, bool required = true)
    {
        var text = ContentRules.Trim(value);
        if (text is null)
        {
            if (required) Error(property, $"{_view.Label(key)} is required.");
            return null;
        }
        if (!allowed.Contains(text, StringComparer.Ordinal)) Error(property, $"Choose one of the listed values for {_view.Label(key).ToLowerInvariant()}.");
        return text;
    }
}
