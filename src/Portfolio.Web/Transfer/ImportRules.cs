using System.Text.RegularExpressions;
using Portfolio.Data.Entities;
using Portfolio.Web.Services;

namespace Portfolio.Web.Transfer;

/// <summary>Rules for imported values that the editors do not need, because an editor never meets a value like it: addresses of files, and enum names written by hand.</summary>
public static partial class ImportRules
{
    /// <summary>Folders of the site's own static files. An address inside one is accepted as written, like the seed files use them; there is no other relative address.</summary>
    public static readonly IReadOnlyList<string> BundledFolders = ["/img/", "/proofs/"];

    /// <summary>True for a site-relative address inside one of the bundled folders (no dot segments, no scheme, no white space).</summary>
    public static bool IsBundled(string? value) =>
        value is not null && BundledFolders.Any(f => value.StartsWith(f, StringComparison.Ordinal)) && ContentRules.IsLocalPath(value);

    /// <summary>
    /// An image address from the file (a photo, a cover, a gallery picture): an uploaded file (<c>/uploads/...</c>) that is already in the uploads folder of this site, or a picture
    /// that ships with the site (<c>/img/...</c>, <c>/proofs/...</c>). Files are not part of an import, and the editors only ever store uploads for these fields, so a web address,
    /// an upload that is not here, or anything else is <b>dropped</b>: <paramref name="dropped"/> is true, a warning says so, and the caller treats the field as absent (the stored
    /// image is kept; a new row starts without one). Text that is not an address at all (control characters, over the limit) is an error, as for any field.
    /// </summary>
    public static string? Image(RowReader r, string property, string key, string? raw, int max, out bool dropped)
    {
        dropped = false;
        var text = ContentRules.Trim(raw);
        if (text is null) return null;
        var label = r.Label(key);
        if (text.Length > max) { r.Check.Error(property, $"{label} can be at most {max} characters."); return null; }
        if (ContentRules.HasControlCharacters(text)) { r.Check.Error(property, $"{label} contains characters that are not allowed."); return null; }
        if (text.StartsWith("/uploads/", StringComparison.Ordinal) && r.Context.Uploads.TryResolve(text, out var path) && File.Exists(path)) return text;
        if (IsBundled(text)) return text;

        dropped = true;
        var why = text.StartsWith("/uploads/", StringComparison.Ordinal)
            ? "the uploaded file it pointed at is not on this site (image files are not part of an export)"
            : "an image must be an uploaded file that is on this site or a picture that ships with the site, not another address";
        r.Warnings.Add($"{label}: {why}, so " + (r.KeepsStored ? "the image that is stored was kept." : "the image was left out."));
        return null;
    }

    [GeneratedRegex(@"/uploads/[0-9a-f]{32}\.(?:jpg|png|webp)", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex UploadPattern();

    /// <summary>The distinct <c>/uploads/...</c> files a text points at (an image in a post's Markdown), in order of appearance.</summary>
    public static IReadOnlyList<string> UploadReferences(string? text) =>
        string.IsNullOrEmpty(text) ? [] : UploadPattern().Matches(text).Select(m => m.Value).Distinct().ToList();

    /// <summary>Plain text for showing a name from the file: control characters turned into spaces, and invisible format characters (zero-width, bidirectional overrides) removed, so a name cannot reorder the page around it.</summary>
    public static string Plain(string? value) =>
        new((value ?? "").Where(c => char.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.Format).Select(c => char.IsControl(c) ? ' ' : c).ToArray());

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
