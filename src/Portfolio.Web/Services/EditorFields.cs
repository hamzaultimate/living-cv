namespace Portfolio.Web.Services;

/// <summary>One field of a content editor: its key in the manifest, the neutral wording used when the manifest says nothing, and whether a template may hide it.</summary>
public sealed record EditorFieldInfo(string Key, string Label, string? Hint, bool Required);

/// <summary>
/// The closed list of fields each content editor has, with neutral default wording. A template changes the wording or hides an optional
/// field through <c>editor</c> in its manifest (see <see cref="TemplateEditor"/>); nothing here names a profession. Fields an entry cannot
/// exist without (<see cref="EditorFieldInfo.Required"/>) can be relabelled but never hidden.
/// </summary>
public static class EditorFields
{
    public static readonly IReadOnlyList<EditorFieldInfo> Experience =
    [
        new("company", "Organisation", null, true),
        new("title", "Job title", null, true),
        new("start", "Start", "A year, or a month and year: 2021, Mar 2021 or 2021-03.", true),
        new("end", "End", "Leave empty for a role that is current. Not earlier than the start.", false),
        new("current", "This is my current role", null, false),
        new("summary", "Summary", "A few sentences. Plain text.", true),
        new("highlights", "Highlights", "One per line, up to 12.", false),
        new("tech", "Tools and technologies", "One per line, up to 20.", false),
    ];

    public static readonly IReadOnlyList<EditorFieldInfo> Skills =
    [
        new("name", "Name", null, true),
        new("group", "Group", null, true),
    ];

    public static readonly IReadOnlyList<EditorFieldInfo> Projects =
    [
        new("title", "Title", null, true),
        new("slug", "Web address name", "Lower-case letters, digits and hyphens. Leave empty to build it from the title. Changing it breaks links to the old address.", true),
        new("role", "Your role", null, false),
        new("org", "Organisation", null, false),
        new("period", "Period", "For example 2023 or 2021-2022. Plain text.", false),
        new("summary", "Summary", "One or two sentences shown on the cards and in search results. Plain text.", true),
        new("category", "Category", "Used by the filter on the listing page.", false),
        new("problem", "Problem", "Plain text.", false),
        new("approach", "Approach", "Plain text.", false),
        new("impact", "Impact", "One result per line, up to 8.", false),
        new("tech", "Tools and technologies", "One per line, up to 20.", false),
        new("links", "Links", "A label and a web address (http or https), up to 6.", false),
        new("featured", "Show on the home page", null, false),
        new("heroImage", "Cover image", "JPEG, PNG or WebP. Shown on cards and at the top of the page.", false),
        new("screenshots", "Gallery images", "Added one at a time after the first save.", false),
    ];

    public static readonly IReadOnlyList<EditorFieldInfo> Certifications =
    [
        new("name", "Name", null, true),
        new("issuer", "Issuer", null, true),
        new("status", "Status", null, true),
        new("date", "Date", "A year, or a month and year: 2024, Mar 2024 or 2024-03. An earned entry cannot be dated in the future.", false),
        new("category", "Category", null, false),
        new("credentialUrl", "Credential link", "Where someone can verify it (http or https).", false),
        new("certificateUrl", "Certificate link", "A PDF or image of the certificate (http or https).", false),
        new("badgeImageUrl", "Badge image link", "An image address (http or https).", false),
    ];

    /// <summary>The fields of a section, or an empty list for a section without an editor.</summary>
    public static IReadOnlyList<EditorFieldInfo> For(string section) => section switch
    {
        SectionRegistry.Experience => Experience,
        SectionRegistry.Skills => Skills,
        SectionRegistry.Projects => Projects,
        SectionRegistry.Certifications => Certifications,
        _ => [],
    };

    /// <summary>The sections that have a content editor, in the order the editors are listed.</summary>
    public static readonly IReadOnlyList<string> Sections =
        [SectionRegistry.Experience, SectionRegistry.Skills, SectionRegistry.Projects, SectionRegistry.Certifications];
}
