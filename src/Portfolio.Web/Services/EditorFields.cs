namespace Portfolio.Web.Services;

/// <summary>One field of a content editor: its key in the manifest, the neutral wording used when the manifest says nothing, and whether a template may hide it.
/// <paramref name="WordingOnly"/> marks a key that only carries words (a heading, the name of a column): it is not a control of its own, so it can be relabelled but never hidden.</summary>
public sealed record EditorFieldInfo(string Key, string Label, string? Hint, bool Required, bool WordingOnly = false);

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

    public static readonly IReadOnlyList<EditorFieldInfo> Talks =
    [
        new("title", "Title", null, true),
        new("type", "Type", null, false),
        new("typeVideo", "Video", "The word for the first choice of the type field. Wording only: the field itself is hidden or shown by type.", false),
        new("typeTalk", "Talk", "The word for the second choice of the type field. Wording only.", false),
        new("event", "Event", "Where it was given or published.", false),
        new("date", "Date", "A date, or a month and year: 2025-05-14, 2025-05 or 2025. Entries are listed newest first by this date.", false),
        new("youtubeId", "Video", "A YouTube link or the 11 character video id. Shown as an embedded video.", false),
        new("url", "Link", "Where to watch or read more (http or https).", false),
    ];

    public static readonly IReadOnlyList<EditorFieldInfo> Testimonials =
    [
        new("quote", "Quote", "Plain text. Only with the permission of the person quoted.", true),
        new("author", "Name", null, true),
        new("authorTitle", "Role and organisation", "For example: Engineering Manager, Acme Corp.", false),
        new("sourceUrl", "Source link", "Where the original can be read (http or https).", false),
    ];

    public static readonly IReadOnlyList<EditorFieldInfo> Blog =
    [
        new("title", "Title", null, true),
        new("slug", "Web address name", "Lower-case letters, digits and hyphens. Leave empty to build it from the title. Changing it breaks links to the old address.", true),
        new("description", "Description", "One or two sentences shown on the list and in search results. Plain text.", false),
        new("date", "Date", "The publication date, as 2025-09-15. The list shows the newest first.", true),
        new("tags", "Tags", "Separated by commas, up to 8. Letters, digits, spaces and . + # & _ - only.", false),
        new("cover", "Cover image", "JPEG, PNG or WebP. Shown at the top of the post and as the share image.", false),
        new("body", "Text", "Markdown. Raw HTML is shown as text, not run. Use Preview to see the result before you save.", true),
        new("draft", "Keep as a draft", "A draft is saved but not shown anywhere on your site.", true),
    ];

    /// <summary>The home page introduction. <c>heading</c> is the wording of the page (its title is the label, the line under it the hint).</summary>
    public static readonly IReadOnlyList<EditorFieldInfo> HeroFields =
    [
        new("heading", "Home page introduction", "The first thing visitors read on your home page. Plain text. Changes apply as soon as you save.", false, true),
        new("eyebrow", "Eyebrow", "A short line above the headline, for example your role. Plain text.", false),
        new("headline", "Headline", null, true),
        new("headlineAccent", "Headline (highlighted part)", "Shown after the headline in the accent colour.", false),
        new("lede", "Introduction", "One or two sentences under the headline.", false),
    ];

    public static readonly IReadOnlyList<EditorFieldInfo> MetricsFields =
    [
        new("heading", "Headline metrics", "Short figures shown under the introduction on your home page. Only publish figures you can stand behind. Changes apply as soon as you save.", false, true),
        new("value", "Value", "For example 20k or 99.9.", true),
        new("unit", "Unit", "For example rps or %.", false),
        new("label", "Label", "What the figure measures.", false),
    ];

    public static readonly IReadOnlyList<EditorFieldInfo> AboutFields =
    [
        new("heading", "About page", "Plain text. Changes apply as soon as you save.", false, true),
        new("headline", "Headline", "The main heading of the page.", true),
        new("paragraphs", "Paragraphs", "Separate paragraphs with a blank line.", false),
        new("valueProps", "Value cards", "Short cards shown above the text.", false),
        new("valuePropTitle", "Title", null, false, true),
        new("valuePropBody", "Text", null, false, true),
        new("currentlyNote", "\"Currently\" note", "What you are working on or looking for at the moment.", false),
        new("focus", "Focus areas", "One per line.", false),
    ];

    /// <summary>The fields of a section, or an empty list for a section without an editor.</summary>
    public static IReadOnlyList<EditorFieldInfo> For(string section) => section switch
    {
        ProfileParts.Hero => HeroFields,
        ProfileParts.Metrics => MetricsFields,
        ProfileParts.About => AboutFields,
        SectionRegistry.Experience => Experience,
        SectionRegistry.Skills => Skills,
        SectionRegistry.Projects => Projects,
        SectionRegistry.Certifications => Certifications,
        SectionRegistry.Talks => Talks,
        SectionRegistry.Testimonials => Testimonials,
        SectionRegistry.Blog => Blog,
        _ => [],
    };

    /// <summary>The sections that have a content editor, in the order the editors are listed.</summary>
    public static readonly IReadOnlyList<string> Sections =
        [SectionRegistry.Experience, SectionRegistry.Skills, SectionRegistry.Projects, SectionRegistry.Certifications, SectionRegistry.Talks, SectionRegistry.Testimonials, SectionRegistry.Blog];
}

/// <summary>The profile's own editable parts. Unlike the content sections they are not lists of rows in a table: each is a block of text on the profile row.
/// Their keys are the keys of the <c>editor</c> manifest block (<c>hero</c>, <c>metrics</c>, <c>about</c>) and of the routes under <c>/admin/profile</c>.</summary>
public static class ProfileParts
{
    public const string Hero = "hero";
    public const string Metrics = "metrics";
    public const string About = SectionRegistry.About;

    public static readonly IReadOnlyList<string> All = [Hero, Metrics, About];

    // Limits of the text blocks (the profile stores them as JSON, so no column length applies).
    public const int HeroMax = 300;
    public const int LedeMax = 800;
    public const int MetricRows = 8;
    public const int MetricValueMax = 20;
    public const int MetricUnitMax = 20;
    public const int MetricLabelMax = 80;
    public const int MaxParagraphs = 10;
    public const int ParagraphMax = 2000;
    public const int CardRows = 6;
    public const int CardTitleMax = 120;
    public const int CardBodyMax = 500;
    public const int MaxFocus = 30;
    public const int FocusMax = 120;
}
