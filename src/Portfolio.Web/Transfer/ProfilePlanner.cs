using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Portfolio.Data.Entities;
using Portfolio.Web.Services;

namespace Portfolio.Web.Transfer;

/// <summary>
/// Checks the profile part of a file the way the profile editors check their forms (identity, introduction, metrics, About, sections) and builds the one change
/// that <see cref="ProfileEditor.UpdateAsync"/> will write. The part is applied whole or not at all: any error refuses all of it, with every reason listed. A field the
/// file does not mention keeps its stored value. Nothing is written here, and the identity that the site reads through the profile provider only ever changes through the editor.
/// </summary>
public sealed class ProfilePlanner
{
    // The identity fields as the file names them: the form property, the words a message uses, and the limit.
    private sealed record Field(string Json, string Form, string Label, int Max, bool Required = false);

    private static readonly Field[] Identity =
    [
        new("ownerName", nameof(ProfileForm.OwnerName), "Name", SiteProfile.MaxLength.OwnerName, true),
        new("brandName", nameof(ProfileForm.BrandName), "Short name", SiteProfile.MaxLength.BrandName, true),
        new("role", nameof(ProfileForm.Role), "Role line", SiteProfile.MaxLength.Role),
        new("jobTitle", nameof(ProfileForm.JobTitle), "Title", SiteProfile.MaxLength.JobTitle),
        new("location", nameof(ProfileForm.Location), "Location", SiteProfile.MaxLength.Location),
        new("url", nameof(ProfileForm.Url), "Site address", SiteProfile.MaxLength.Url, true),
        new("googleAnalyticsId", nameof(ProfileForm.GoogleAnalyticsId), "Analytics measurement ID", SiteProfile.MaxLength.GoogleAnalyticsId),
        new("summary", nameof(ProfileForm.Summary), "Summary", ProfileForm.SummaryMax),
        new("phone", nameof(ProfileForm.Phone), "Phone", 40),
        new("whatsApp", nameof(ProfileForm.WhatsApp), "WhatsApp number", 20),
        new("email", nameof(ProfileForm.Email), "Email", ProfileForm.ContactMax),
        new("linkedIn", nameof(ProfileForm.LinkedIn), "LinkedIn link", ProfileForm.ContactMax),
        new("gitHub", nameof(ProfileForm.GitHub), "GitHub link", ProfileForm.ContactMax),
        new("youTube", nameof(ProfileForm.YouTube), "YouTube link", ProfileForm.ContactMax),
    ];

    private static readonly string[] Known =
    [
        "ownerName", "brandName", "role", "jobTitle", "url", "googleAnalyticsId", "summary", "location", "knowsAbout", "photoUrl", "photoUrlLight", "phone", "whatsApp", "email",
        "secondaryEmails", "linkedIn", "gitHub", "youTube", "templateKey", "accentColor", "hero", "metrics", "about", "sections",
    ];

    // Neutral words for the parts of the identity that no editor block names.
    private static readonly Dictionary<string, string> Words = new(StringComparer.Ordinal)
    {
        ["knowsAbout"] = "Topics", ["secondaryEmails"] = "Other email addresses", ["photoUrl"] = "Photo", ["photoUrlLight"] = "Photo for the light theme",
        ["templateKey"] = "Template", ["accentColor"] = "Accent colour",
    };

    private readonly ITemplateCatalog _catalog;
    private readonly TemplateResolver _resolver;

    public ProfilePlanner(ITemplateCatalog catalog, TemplateResolver resolver)
    {
        _catalog = catalog;
        _resolver = resolver;
    }

    /// <summary>
    /// Reads <paramref name="part"/> against the stored <paramref name="current"/> profile (which this call may change in memory: it is the planner's own untracked copy).
    /// Fills <paramref name="report"/> and returns the change to write, or null when the part is refused or changes nothing.
    /// </summary>
    public ProfilePlan? Plan(JsonElement part, SiteProfile current, TemplateView words, PlanContext ctx, ProfileReport report)
    {
        if (part.ValueKind != JsonValueKind.Object)
        {
            report.Status = RowStatus.Refused;
            report.Reasons.Add($"The {report.Label.ToLowerInvariant()} in the file must be an object with fields.");
            return null;
        }

        var seenStamp = current.ConcurrencyStamp;
        var state = new ModelStateDictionary();
        var warnings = report.Warnings;
        string Word(string key) => Words.TryGetValue(key, out var w) ? w : Identity.FirstOrDefault(f => f.Json == key)?.Label ?? key;
        var identityCheck = new FormCheck(state, Word, "");
        var anyView = ctx.Views(ProfileParts.Hero);
        var r = new RowReader(part, state, identityCheck, anyView, false, ctx, Known) { Label = Word, Warnings = warnings };

        // The template first: the words, the accent and the sections are all read against the template the profile will have (the caller built <paramref name="words"/> from it).
        var targetDefinition = words.Definition;
        string? newTemplateKey = null;
        var template = r.Text("templateKey", "templateKey", SiteProfile.MaxLength.TemplateKey);
        if (template.Present && template.Value is not null)
        {
            if (_catalog.TryGet(template.Value, out var known)) { if (!string.Equals(known.Key, current.TemplateKey, StringComparison.Ordinal)) newTemplateKey = known.Key; }
            else if (identityCheck.IsValid("templateKey")) identityCheck.Error("templateKey", $"The template \"{template.Value}\" is not installed on this site, so the {report.Label.ToLowerInvariant()} was not applied. A template is never guessed.");
        }

        // Identity: the editor's own form, so its rules (required fields, formats, lengths) are the ones that apply.
        var form = ProfileForm.From(current);
        var identity = Read(r, form);

        var photo = r.Image("photoUrl", "photoUrl", SiteProfile.MaxLength.PhotoUrl);
        var photoLight = r.Image("photoUrlLight", "photoUrlLight", SiteProfile.MaxLength.PhotoUrlLight);

        string? accent = current.AccentColor;
        var accentGot = r.Text("accentColor", "accentColor", SiteProfile.MaxLength.AccentColor);
        if (accentGot.Present)
        {
            if (accentGot.Value is null) accent = null;
            else if (targetDefinition.Theme.Accents.Any(a => string.Equals(a.Key, accentGot.Value, StringComparison.Ordinal))) accent = accentGot.Value;
            else if (identityCheck.IsValid("accentColor")) warnings.Add($"The accent colour \"{accentGot.Value}\" is not one of the colours of this template, so it was left as it is.");
        }

        var hero = ReadHero(part, state, ctx, warnings);
        var metrics = ReadMetrics(part, state, ctx, warnings);
        var about = ReadAbout(part, state, ctx, warnings, current);
        var sections = ReadSections(part, state, words, ctx, targetDefinition, current, report);

        var errors = state.Where(e => e.Value?.Errors.Count > 0).SelectMany(e => e.Value!.Errors.Select(x => x.ErrorMessage)).ToList();
        if (errors.Count > 0)
        {
            report.Status = RowStatus.Refused;
            report.Reasons.AddRange(errors);
            return null;
        }

        var before = Snapshot(current);
        var oldTemplate = _catalog.Get(current.TemplateKey).Name;
        var oldKey = current.TemplateKey;
        void Apply(SiteProfile p)
        {
            identity(p);
            if (photo.Present) p.PhotoUrl = photo.Value;
            if (photoLight.Present) p.PhotoUrlLight = photoLight.Value;
            if (newTemplateKey is not null) p.TemplateKey = newTemplateKey;
            p.AccentColor = accent;
            hero?.Invoke(p);
            metrics?.Invoke(p);
            about?.Invoke(p);
            sections?.Invoke(p);
        }

        Apply(current);
        // The template and the owner's section choices must leave something in the menu, as when the owner switches a template or saves the sections page.
        var after = _resolver.Resolve(SiteProfileMapper.ToOptions(current));
        if (!SectionSettings.HasMenuSection(after))
        {
            var item = new EditorView(words, ProfileParts.Sections).ItemLabel;
            report.Status = RowStatus.Refused;
            report.Reasons.Add($"Applying the {report.Label.ToLowerInvariant()} would leave no \"{item}\" switched on that appears in the menu, so none of it was applied.");
            return null;
        }

        var changes = Describe(before, Snapshot(current), ctx);
        if (newTemplateKey is not null && newTemplateKey != oldKey) changes.Insert(0, $"Template: {oldTemplate} to {targetDefinition.Name}");
        report.Changes.AddRange(changes);
        report.Status = changes.Count == 0 ? RowStatus.Unchanged : RowStatus.Update;
        return changes.Count == 0 ? null : new ProfilePlan { SeenStamp = seenStamp, Apply = Apply };
    }

    /// <summary>Reads the identity fields into <paramref name="form"/> (started from the stored values) and returns the write for them.</summary>
    private Action<SiteProfile> Read(RowReader r, ProfileForm form)
    {
        foreach (var field in Identity)
        {
            var got = r.Text(field.Json, field.Json, field.Max, field.Required);
            if (!got.Present) continue;
            typeof(ProfileForm).GetProperty(field.Form)!.SetValue(form, field.Required ? got.Value ?? "" : got.Value);
        }
        var topics = r.Lines("knowsAbout", "knowsAbout", ProfileForm.MaxListItems, ProfileForm.ItemMax);
        if (topics.Present) form.KnowsAboutText = string.Join("\n", topics.Value!);
        var others = r.Lines("secondaryEmails", "secondaryEmails", 5, ProfileForm.ContactMax);
        if (others.Present) form.SecondaryEmailsText = string.Join("\n", others.Value!);

        // The editor's format rules (web address, email, phone, analytics id) run over the same values. A field that already failed a check above is not reported twice.
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(form, new ValidationContext(form), results, validateAllProperties: true);
        foreach (var result in results)
        {
            foreach (var member in result.MemberNames.DefaultIfEmpty(""))
            {
                var field = Identity.FirstOrDefault(f => f.Form == member);
                var json = field?.Json ?? member switch { nameof(ProfileForm.KnowsAboutText) => "knowsAbout", nameof(ProfileForm.SecondaryEmailsText) => "secondaryEmails", _ => "" };
                if (json.Length > 0 && !r.Check.IsValid(json)) continue;
                var label = field?.Label ?? (json.Length > 0 ? Words[json] : "Profile");
                r.Check.Error(json, (result.ErrorMessage ?? "Not valid.").Replace(member, label, StringComparison.Ordinal));
            }
        }
        return form.ApplyTo;
    }

    private static Action<SiteProfile>? ReadHero(JsonElement part, ModelStateDictionary state, PlanContext ctx, List<string> warnings)
    {
        if (!part.TryGetProperty("hero", out var element)) return null;
        var view = ctx.Views(ProfileParts.Hero);
        if (element.ValueKind != JsonValueKind.Object) { state.AddModelError("hero", $"{view.Heading} must be an object with fields."); return null; }
        var r = Block(element, state, view, ctx, warnings, "hero", ["eyebrow", "headline", "headlineAccent", "lede"]);
        var eyebrow = r.Text("eyebrow", "eyebrow", ProfileParts.HeroMax);
        var headline = r.Text("headline", "headline", ProfileParts.HeroMax, required: true);
        var accent = r.Text("headlineAccent", "headlineAccent", ProfileParts.HeroMax);
        var lede = r.Text("lede", "lede", ProfileParts.LedeMax);
        return p => p.Hero = new ProfileHero
        {
            Eyebrow = eyebrow.Present ? eyebrow.Value : p.Hero.Eyebrow,
            Headline = headline.Present ? headline.Value : p.Hero.Headline,
            HeadlineAccent = accent.Present ? accent.Value : p.Hero.HeadlineAccent,
            Lede = lede.Present ? lede.Value : p.Hero.Lede,
        };
    }

    private static RowReader Block(JsonElement element, ModelStateDictionary state, EditorView view, PlanContext ctx, List<string> warnings, string path, string[] known) =>
        new(element, state, new FormCheck(state, ImportRules.Labels(view), ""), view, false, ctx, known) { Path = path + ".", Warnings = warnings };

    private static Action<SiteProfile>? ReadMetrics(JsonElement part, ModelStateDictionary state, PlanContext ctx, List<string> warnings)
    {
        if (!part.TryGetProperty("metrics", out var element)) return null;
        var view = ctx.Views(ProfileParts.Metrics);
        if (element.ValueKind == JsonValueKind.Null) return p => p.Metrics = [];
        if (element.ValueKind != JsonValueKind.Array) { state.AddModelError("metrics", $"{view.Heading} must be a list."); return null; }
        var rows = element.EnumerateArray().ToList();
        if (rows.Count > ProfileParts.MetricRows) { state.AddModelError("metrics", $"At most {ProfileParts.MetricRows} rows in {view.Heading}."); return null; }

        var showLabel = view.Visible("label");
        var metrics = new List<ProfileMetric>();
        for (var i = 0; i < rows.Count; i++)
        {
            var name = $"{view.ItemLabel} {i + 1}";
            if (rows[i].ValueKind != JsonValueKind.Object) { state.AddModelError($"metrics[{i}]", $"{name} must be an entry with fields."); continue; }
            if (IsBlank(rows[i], "value", "unit", "label")) continue; // a row with nothing in it is dropped, as the editor drops it
            var check = new FormCheck(state, ImportRules.Labels(view), "");
            var r = new RowReader(rows[i], state, check, view, true, ctx, ["value", "unit", "label"]) { Path = $"metrics[{i}].", RowLabel = name, Warnings = warnings };
            var value = r.Text("value", "value", ProfileParts.MetricValueMax, required: true);
            var unit = r.Text("unit", "unit", ProfileParts.MetricUnitMax);
            var label = r.Text("label", "label", ProfileParts.MetricLabelMax, required: showLabel);
            metrics.Add(new ProfileMetric { Value = value.Value ?? "", Unit = unit.Value, Label = label.Value });
        }
        return p => p.Metrics = metrics.Select(m => new ProfileMetric { Value = m.Value, Unit = m.Unit, Label = m.Label }).ToList();
    }

    private static Action<SiteProfile>? ReadAbout(JsonElement part, ModelStateDictionary state, PlanContext ctx, List<string> warnings, SiteProfile current)
    {
        if (!part.TryGetProperty("about", out var element)) return null;
        var view = ctx.Views(ProfileParts.About);
        if (element.ValueKind != JsonValueKind.Object) { state.AddModelError("about", $"{view.Heading} must be an object with fields."); return null; }
        var r = Block(element, state, view, ctx, warnings, "about", ["headline", "paragraphs", "valueProps", "currentlyNote", "focus"]);
        var check = r.Check;

        // The headline is required once the page has one, as in its editor.
        var headline = r.Text("headline", "headline", ProfileParts.HeroMax, required: !string.IsNullOrWhiteSpace(current.About.Headline));
        var note = r.Text("currentlyNote", "currentlyNote", ProfileParts.HeroMax);
        var focus = r.Lines("focus", "focus", ProfileParts.MaxFocus, ProfileParts.FocusMax);

        Got<List<string>> paragraphs = Got<List<string>>.Absent;
        if (r.TryStrings("paragraphs", "paragraphs", out var rawParagraphs))
        {
            var list = new List<string>();
            foreach (var (raw, i) in rawParagraphs.Select((x, i) => (x, i)))
                if (check.Paragraph($"about.paragraphs[{i}]", "paragraphs", raw, ProfileParts.ParagraphMax) is { } text) list.Add(text);
            if (list.Count > ProfileParts.MaxParagraphs) check.Error("about.paragraphs", $"{view.Label("paragraphs")}: at most {ProfileParts.MaxParagraphs} paragraphs.");
            paragraphs = new Got<List<string>>(true, list);
        }

        Got<List<ProfileValueProp>> cards = Got<List<ProfileValueProp>>.Absent;
        if (r.TryObjects("valueProps", "valueProps", out var rawCards))
        {
            var list = new List<ProfileValueProp>();
            if (rawCards.Count > ProfileParts.CardRows) check.Error("about.valueProps", $"At most {ProfileParts.CardRows} rows in {view.Heading}.");
            else
                for (var i = 0; i < rawCards.Count; i++)
                {
                    if (IsBlank(rawCards[i], "title", "body")) continue;
                    var row = r.Nested(rawCards[i], $"about.valueProps[{i}]", $"{view.ItemLabel} {i + 1}", ["title", "body"]);
                    var title = row.Text("title", "valuePropTitle", ProfileParts.CardTitleMax, required: true);
                    var body = row.Text("body", "valuePropBody", ProfileParts.CardBodyMax, required: true);
                    list.Add(new ProfileValueProp { Title = title.Value ?? "", Body = body.Value ?? "" });
                }
            cards = new Got<List<ProfileValueProp>>(true, list);
        }

        return p =>
        {
            var old = p.About;
            p.About = new ProfileAbout
            {
                Headline = headline.Present ? headline.Value : old.Headline,
                Paragraphs = paragraphs.Present ? paragraphs.Value!.ToList() : old.Paragraphs,
                ValueProps = cards.Present ? cards.Value!.Select(c => new ProfileValueProp { Title = c.Title, Body = c.Body }).ToList() : old.ValueProps,
                CurrentlyNote = note.Present ? note.Value : old.CurrentlyNote,
                Focus = focus.Present ? focus.Value!.ToList() : old.Focus,
            };
        };
    }

    private Action<SiteProfile>? ReadSections(JsonElement part, ModelStateDictionary state, TemplateView template, PlanContext ctx, TemplateDefinition definition, SiteProfile current, ProfileReport report)
    {
        if (!part.TryGetProperty("sections", out var element)) return null;
        var view = new EditorView(template, ProfileParts.Sections);
        if (element.ValueKind == JsonValueKind.Null) return null;
        if (element.ValueKind != JsonValueKind.Array) { state.AddModelError("sections", $"{view.Heading} must be a list."); return null; }
        var entries = element.EnumerateArray().ToList();
        if (entries.Count > TransferLimits.MaxSectionSettings) { state.AddModelError("sections", $"{view.Heading}: at most {TransferLimits.MaxSectionSettings} entries."); return null; }

        var fromFile = new Dictionary<string, SectionSetting>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < entries.Count; i++)
        {
            if (entries[i].ValueKind != JsonValueKind.Object) { state.AddModelError($"sections[{i}]", $"{view.ItemLabel} {i + 1} must be an entry with fields."); continue; }
            var check = new FormCheck(state, ImportRules.Labels(view), "");
            var r = new RowReader(entries[i], state, check, view, true, ctx, ["key", "label", "enabled", "shown", "sortOrder"]) { Path = $"sections[{i}].", RowLabel = $"{view.ItemLabel} {i + 1}" };
            var key = r.Text("key", "key", 40, required: true);
            var label = r.Text("label", "label", ProfileParts.SectionLabelMax);
            var enabled = r.Flag("enabled", "enabled");
            var shown = r.Raw("shown") is { ValueKind: JsonValueKind.True or JsonValueKind.False } s ? s.GetBoolean() : (bool?)null;
            if (r.Raw("shown") is { } rawShown && rawShown.ValueKind is not (JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null)) check.Error($"sections[{i}].shown", $"{view.ItemLabel} {i + 1}: \"shown\" must be true, false or null.");
            var order = r.Raw("sortOrder") is { ValueKind: JsonValueKind.Number } n && n.TryGetInt32(out var number) && number is >= 0 and <= 1000 ? number : 0;
            if (r.Raw("sortOrder") is { } rawOrder && rawOrder.ValueKind is not (JsonValueKind.Number or JsonValueKind.Null)) check.Error($"sections[{i}].sortOrder", $"{view.ItemLabel} {i + 1}: \"sortOrder\" must be a number.");
            if (key.Value is null) continue;

            // A key the template does not list is inert: it is reported and not stored, so it can never switch anything on.
            if (SectionSettings.Find(definition, key.Value) is null) { report.InertSections.Add(Printable(key.Value)); continue; }
            if (fromFile.ContainsKey(key.Value)) { report.Warnings.Add($"\"{Printable(key.Value)}\" is listed twice in the sections; the first entry is used."); continue; }
            fromFile[key.Value] = new SectionSetting { Key = SectionSettings.Find(definition, key.Value)!.Key, Label = label.Value, Enabled = !enabled.Present || enabled.Value, Shown = shown, SortOrder = order };
        }
        if (!state.IsValid) return null;

        // A section the file does not mention keeps the setting it has. The rows are every section the template lists, as the sections page draws them: the order the settings
        // give (positions first, the rest in template order).
        SectionSetting? Effective(TemplateSection s) =>
            fromFile.TryGetValue(s.Key, out var f) ? f : current.Sections.LastOrDefault(x => string.Equals(x.Key, s.Key, StringComparison.OrdinalIgnoreCase));
        var rows = definition.Sections
            .Select((s, index) => (Section: s, Index: index, File: Effective(s)))
            .OrderBy(x => x.File is { SortOrder: > 0 } ? x.File.SortOrder : int.MaxValue).ThenBy(x => x.Index)
            .Select(x => new SectionRowForm
            {
                Key = x.Section.Key,
                Label = x.File?.Label,
                Enabled = x.Section.Required || (x.File?.Shown ?? (x.Section.Enabled && x.File?.Enabled != false)),
            }).ToList();
        var labelShown = view.Visible("label");
        var sectionCheck = new FormCheck(state, ImportRules.Labels(view), "");
        if (!SectionSettings.Validate(sectionCheck, view, definition, rows, labelShown, out var labels)) return null;

        var listed = definition.Sections.Select(s => s.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var stored = definition.Sections.Select(Effective).Where(x => x is not null).Select(x => x!)
            .Concat(current.Sections.Where(s => !listed.Contains(s.Key)).Select(s => new SectionSetting { Key = s.Key, Label = s.Label, Enabled = s.Enabled, Shown = s.Shown, SortOrder = s.SortOrder })).ToList();
        var built = SectionSettings.Build(definition, rows, labels, labelShown, stored);
        return p => p.Sections = built.Select(s => new SectionSetting { Key = s.Key, Label = s.Label, Enabled = s.Enabled, Shown = s.Shown, SortOrder = s.SortOrder }).ToList();
    }

    /// <summary>True when every one of <paramref name="names"/> is absent, null or white space in <paramref name="row"/>. A value of another type is not blank: it is an error for the field's own check to report.</summary>
    private static bool IsBlank(JsonElement row, params string[] names) =>
        names.All(n => !row.TryGetProperty(n, out var e) || e.ValueKind == JsonValueKind.Null || (e.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(e.GetString())));

    /// <summary>A key from the file, safe to show: only short plain keys are echoed.</summary>
    private static string Printable(string key) => key.Length <= 40 && key.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_') ? key : "an entry with an unusable key";

    private sealed record Shot(ProfileForm Details, string Hero, string Metrics, string About, string Sections, string Photo, string Accent);

    private static Shot Snapshot(SiteProfile p)
    {
        var details = ProfileForm.From(p);
        details.Stamp = null;
        return new Shot(details,
            JsonSerializer.Serialize(new ExportHero(p.Hero.Eyebrow, p.Hero.Headline, p.Hero.HeadlineAccent, p.Hero.Lede), ExportJson.Options),
            JsonSerializer.Serialize(p.Metrics.Select(m => new ExportMetric(m.Value, m.Unit, m.Label)), ExportJson.Options),
            JsonSerializer.Serialize(new ExportAbout(p.About.Headline, p.About.Paragraphs, p.About.ValueProps.Select(v => new ExportCard(v.Title, v.Body)).ToList(), p.About.CurrentlyNote, p.About.Focus), ExportJson.Options),
            JsonSerializer.Serialize(p.Sections.Select(s => new ExportSection(s.Key, s.Label, s.Enabled, s.Shown, s.SortOrder)), ExportJson.Options),
            p.PhotoUrl + "|" + p.PhotoUrlLight, p.AccentColor ?? "");
    }

    /// <summary>What differs between two states of the profile, in sentences (the template's words for each block).</summary>
    private static List<string> Describe(Shot a, Shot b, PlanContext ctx)
    {
        var changes = new List<string>();
        // An empty value and no value are the same thing (the form writes one as the other), so they are not a change.
        static string Norm(object? value) => value?.ToString() ?? "";
        var fields = Identity.Where(f => Norm(typeof(ProfileForm).GetProperty(f.Form)!.GetValue(a.Details)) != Norm(typeof(ProfileForm).GetProperty(f.Form)!.GetValue(b.Details))).Select(f => f.Label).ToList();
        if (Norm(a.Details.KnowsAboutText) != Norm(b.Details.KnowsAboutText)) fields.Add(Words["knowsAbout"]);
        if (Norm(a.Details.SecondaryEmailsText) != Norm(b.Details.SecondaryEmailsText)) fields.Add(Words["secondaryEmails"]);
        if (Norm(a.Photo) != Norm(b.Photo)) fields.Add(Words["photoUrl"]);
        if (a.Accent != b.Accent) fields.Add(Words["accentColor"]);
        if (fields.Count > 0) changes.Add("Changed: " + string.Join(", ", fields));
        if (a.Hero != b.Hero) changes.Add($"Changed: {ctx.Views(ProfileParts.Hero).Heading}");
        if (a.Metrics != b.Metrics) changes.Add($"Changed: {ctx.Views(ProfileParts.Metrics).Heading}");
        if (a.About != b.About) changes.Add($"Changed: {ctx.Views(ProfileParts.About).Heading}");
        if (a.Sections != b.Sections) changes.Add($"Changed: {ctx.Views(ProfileParts.Sections).Heading}");
        return changes;
    }
}
