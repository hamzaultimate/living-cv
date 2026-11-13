using Portfolio.Data.Entities;
using Portfolio.Web.Services;

namespace Portfolio.Web.Transfer;

/// <summary>Rules for imported values that the editors do not need, because an editor never meets a value like it: addresses of files, and enum names written by hand.</summary>
public static class ImportRules
{
    /// <summary>Folders of the site's own static files. An address inside one is accepted as written, like the seed files use them; there is no other relative address.</summary>
    public static readonly IReadOnlyList<string> BundledFolders = ["/img/", "/proofs/"];

    /// <summary>True for a site-relative address inside one of the bundled folders (no dot segments, no scheme, no white space).</summary>
    public static bool IsBundled(string? value) =>
        value is not null && BundledFolders.Any(f => value.StartsWith(f, StringComparison.Ordinal)) && ContentRules.IsLocalPath(value);

    /// <summary>
    /// An image or file address from the file. A web address or a bundled file is kept as written. An uploaded file (<c>/uploads/...</c>) is kept only when that
    /// exact file is already in the uploads folder of this site: files are not part of an import, so a reference to one that is not here is dropped with a warning
    /// (the row still imports). Nothing else is accepted.
    /// </summary>
    public static string? Image(RowReader r, string property, string key, string? raw, int max)
    {
        var text = ContentRules.Trim(raw);
        if (text is null) return null;
        var label = r.View.Label(key);
        if (text.Length > max) { r.Check.Error(property, $"{label} can be at most {max} characters."); return null; }
        if (ContentRules.HasControlCharacters(text)) { r.Check.Error(property, $"{label} contains characters that are not allowed."); return null; }
        if (text.StartsWith("/uploads/", StringComparison.Ordinal))
        {
            if (r.Context.Uploads.TryResolve(text, out var path) && File.Exists(path)) return text;
            r.Warnings.Add($"{label}: the uploaded file it pointed at is not on this site (image files are not part of an export), so the image was left out.");
            return null;
        }
        if (IsBundled(text) || ContentRules.IsHttpUrl(text)) return text;
        r.Check.Error(property, $"{label} must be a web address starting with http:// or https://, a picture that ships with the site, or a file already uploaded here.");
        return null;
    }

    // Fields the editors have no control for: the words a message uses for them.
    private static readonly Dictionary<string, string> ExtraLabels = new(StringComparer.Ordinal)
    {
        ["relation"] = "Relationship", ["proofs"] = "Proofs", ["proofLabel"] = "Proof label", ["proofUrl"] = "Proof address", ["proofKind"] = "Proof type",
    };

    /// <summary>How a message names a field: the template's own words, or a neutral word for a field its editors do not show.</summary>
    public static Func<string, string> Labels(EditorView view) => key => ExtraLabels.TryGetValue(key, out var label) ? label : view.Label(key);

    /// <summary>The member of <typeparamref name="TEnum"/> named <paramref name="text"/> (any casing), or null.</summary>
    public static TEnum? EnumValue<TEnum>(string? text) where TEnum : struct, Enum =>
        Enum.TryParse<TEnum>(ContentRules.Trim(text), ignoreCase: true, out var value) && Enum.IsDefined(value) && !long.TryParse(text, out _) ? value : null;

    /// <summary>Proofs (links, images, files that back a claim): the file's list, held to the address rules. They have no editor yet, so the limits are the ones of a link.</summary>
    public const int MaxProofs = 20;
    public const int ProofLabelMax = 80;
    public const int ProofUrlMax = 300;
}
