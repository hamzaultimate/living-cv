using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Portfolio.Data;
using Portfolio.Data.Entities;
using Portfolio.Web.Services;
using Portfolio.Web.Uploads;

namespace Portfolio.Web.Transfer;

/// <summary>A preview, or the reason the whole file was refused.</summary>
public sealed record PlanOutcome(ImportPlan? Plan, string? Refusal);

/// <summary>
/// Works out what an import would do and writes nothing: every table is matched against what is stored by its key, every row is checked with the rules of its
/// editor, and the result is a plan that the confirm step can write later. The database is only read (no tracking, no saves), so a preview changes nothing.
/// A file that is structurally wrong (a table that is not a list, more rows than the cap) is refused whole; a row that is wrong is refused alone.
/// </summary>
public sealed class ImportPlanner
{
    private static readonly string[] TopLevel = ["schemaVersion", "format", "exportedAt", "profile", "content", "uploads"];
    private static readonly string[] Tables =
        [SectionRegistry.Experience, SectionRegistry.Skills, SectionRegistry.Projects, SectionRegistry.Certifications, SectionRegistry.Talks, SectionRegistry.Testimonials, SectionRegistry.Blog];

    private readonly PortfolioDbContext _db;
    private readonly TemplateResolver _resolver;
    private readonly ITemplateCatalog _catalog;
    private readonly ISiteProfileProvider _provider;
    private readonly ProfilePlanner _profiles;
    private readonly UploadStore _uploads;
    private readonly TimeProvider _clock;

    public ImportPlanner(PortfolioDbContext db, TemplateResolver resolver, ITemplateCatalog catalog, ISiteProfileProvider provider, UploadStore uploads, TimeProvider clock)
    {
        _db = db;
        _resolver = resolver;
        _catalog = catalog;
        _provider = provider;
        _profiles = new ProfilePlanner(catalog, resolver);
        _uploads = uploads;
        _clock = clock;
    }

    public async Task<PlanOutcome> PlanAsync(JsonElement root, string fileName, bool applyProfile, string sessionKey, CancellationToken ct)
    {
        var content = root.TryGetProperty("content", out var contentElement) ? contentElement : (JsonElement?)null;
        if (content is { } c && c.ValueKind is not (JsonValueKind.Object or JsonValueKind.Null)) return Refused("\"content\" in that file must be an object holding the lists of entries.");
        if (root.TryGetProperty("profile", out var profileElement) && profileElement.ValueKind is not (JsonValueKind.Object or JsonValueKind.Null)) return Refused("\"profile\" in that file must be an object.");
        if (root.TryGetProperty("uploads", out var uploads))
        {
            if (uploads.ValueKind is not (JsonValueKind.Array or JsonValueKind.Null)) return Refused("\"uploads\" in that file must be a list.");
            if (uploads.ValueKind == JsonValueKind.Array && uploads.GetArrayLength() > TransferLimits.MaxUploadEntries) return Refused($"\"uploads\" in that file lists more than {TransferLimits.MaxUploadEntries:N0} files.");
        }

        // Every list is checked against its cap before anything is read from it.
        var lists = new Dictionary<string, JsonElement>();
        if (content is { ValueKind: JsonValueKind.Object } body)
            foreach (var table in Tables)
            {
                if (!body.TryGetProperty(table, out var list) || list.ValueKind == JsonValueKind.Null) continue;
                if (list.ValueKind != JsonValueKind.Array) return Refused($"\"{table}\" in that file must be a list of entries.");
                var cap = TransferLimits.MaxRows(table);
                if (list.GetArrayLength() > cap) return Refused($"\"{table}\" in that file has {list.GetArrayLength():N0} entries; at most {cap:N0} are accepted in one import.");
                lists[table] = list;
            }

        var profile = await _db.SiteProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.Id == SiteProfile.SingletonId, ct)
                      ?? SiteProfileMapper.ToEntity(await _provider.GetAsync(ct));
        var options = SiteProfileMapper.ToOptions(profile);
        var current = _resolver.Resolve(options);

        // The words are the template's: the file's own when its profile is being applied and names an installed template, the site's otherwise.
        var words = current;
        if (applyProfile && profileElement.ValueKind == JsonValueKind.Object && RowReader.Peek(profileElement, "templateKey") is { } key && _catalog.TryGet(key, out _))
            words = _resolver.Resolve(options, key);

        var context = new PlanContext { Views = section => new EditorView(words, section), Clock = _clock, Uploads = _uploads };
        var report = new ProfileReport { Label = new EditorView(words, ProfileParts.Transfer).Label("profile"), Requested = applyProfile, InFile = profileElement.ValueKind == JsonValueKind.Object };
        var plan = new ImportPlan
        {
            SessionKey = sessionKey, FileName = fileName, ApplyProfile = applyProfile, Profile = report, Words = words,
        };

        // The profile planner works on (and changes) its own untracked copy: this one, which nothing else reads after the plan is made.
        if (applyProfile && report.InFile) plan.ProfileChange = _profiles.Plan(profileElement, profile, words, context, report);

        plan.Tables.Add(await PlanTableAsync(new ExperienceSpec(), lists, context, ct));
        plan.Tables.Add(await PlanTableAsync(new SkillSpec(), lists, context, ct));
        plan.Tables.Add(await PlanTableAsync(new ProjectSpec(), lists, context, ct));
        plan.Tables.Add(await PlanTableAsync(new CertificationSpec(), lists, context, ct));
        plan.Tables.Add(await PlanTableAsync(new TalkSpec(), lists, context, ct));
        plan.Tables.Add(await PlanTableAsync(new TestimonialSpec(), lists, context, ct));
        plan.Tables.Add(await PlanTableAsync(new PostSpec(), lists, context, ct));

        foreach (var property in root.EnumerateObject()) if (!TopLevel.Contains(property.Name, StringComparer.Ordinal)) context.UnknownProperties++;
        if (content is { ValueKind: JsonValueKind.Object } known)
            foreach (var property in known.EnumerateObject()) if (!Tables.Contains(property.Name, StringComparer.Ordinal)) context.UnknownProperties++;
        plan.UnknownProperties = context.UnknownProperties;
        if (uploads.ValueKind == JsonValueKind.Array && uploads.GetArrayLength() > 0)
            plan.FileNotes.Add($"The file lists {uploads.GetArrayLength():N0} image files. Image files are not part of an export and are not imported: a reference to a file that is already on this site is kept, any other image reference is left out.");
        if (!applyProfile && report.InFile)
            plan.FileNotes.Add($"The file has a {report.Label.ToLowerInvariant()}. It is ignored because \"also apply\" was not ticked.");
        if (applyProfile && !report.InFile)
            plan.FileNotes.Add($"The file has no {report.Label.ToLowerInvariant()}, so there is nothing to apply.");
        if (plan.UnknownProperties > 0)
            plan.FileNotes.Add($"{plan.UnknownProperties:N0} {(plan.UnknownProperties == 1 ? "property" : "properties")} in the file {(plan.UnknownProperties == 1 ? "is" : "are")} not read by this version and {(plan.UnknownProperties == 1 ? "was" : "were")} ignored.");
        return new PlanOutcome(plan, null);
    }

    private static PlanOutcome Refused(string message) => new(null, message);

    private async Task<ITablePlan> PlanTableAsync<T>(TableSpec<T> spec, Dictionary<string, JsonElement> lists, PlanContext context, CancellationToken ct) where T : class, IStampedContent
    {
        var view = context.Views(spec.Section);
        var plan = new TablePlan<T>
        {
            Spec = spec,
            View = view,
            Report = new SectionReport { Section = spec.Section, Label = view.SectionLabel, ItemLabel = view.ItemLabel },
        };
        if (!lists.TryGetValue(spec.Section, out var list)) return plan;

        var stored = await spec.Stored(_db).ToListAsync(ct);
        // No unique index covers most keys, so two stored rows could share one; the first (lowest id) is the one a file row matches.
        var byKey = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in stored.OrderBy(r => r.Id)) byKey.TryAdd(spec.KeyOf(row), row);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var position = 0;
        foreach (var element in list.EnumerateArray())
        {
            position++;
            var fallbackName = $"{view.ItemLabel} {position}";
            if (element.ValueKind != JsonValueKind.Object)
            {
                plan.Report.Rows.Add(new RowReport(position, fallbackName, RowStatus.Refused, [$"{view.ItemLabel} {position} is not an entry with fields."], []));
                continue;
            }

            var name = Display(spec.NameOf(element) ?? fallbackName);
            var key = spec.KeyOf(element);
            if (key is not null && !seen.Add(key))
            {
                plan.Report.Rows.Add(new RowReport(position, name, RowStatus.Refused,
                    [$"An earlier {view.ItemLabelLower} in the file has the same {spec.KeyWords(view)}. The first one is imported; this one is not."], []));
                continue;
            }

            T? match = key is not null && byKey.TryGetValue(key, out var found) ? found : null;
            var state = new Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary();
            var reader = new RowReader(element, state, new FormCheck(state, ImportRules.Labels(view), ""), view, create: match is null, context, spec.Known);
            var patch = new Patch<T>();
            spec.Fill(reader, match, patch);

            var errors = reader.Errors();
            if (errors.Count > 0)
            {
                plan.Report.Rows.Add(new RowReport(position, name, RowStatus.Refused, errors, reader.Warnings));
                continue;
            }

            var status = RowStatus.Create;
            if (match is not null)
            {
                // Applied to the planner's own untracked copy of the stored row: if the export of the result is the same, there is nothing to write.
                var before = spec.Fingerprint(match);
                foreach (var step in patch.Steps) step(match);
                status = spec.Fingerprint(match) == before ? RowStatus.Unchanged : RowStatus.Update;
            }
            plan.Report.Rows.Add(new RowReport(position, name, status, [], reader.Warnings));
            if (key is not null) plan.FileOrder.Add((key, patch.Group));
            if (status == RowStatus.Unchanged) continue;
            plan.Rows.Add(new PlannedRow<T>
            {
                Position = position, Name = name, Key = key ?? "", Status = status, RowId = match?.Id, SeenStamp = match?.ConcurrencyStamp,
                Steps = patch.Steps, Warnings = reader.Warnings, Group = patch.Group,
            });
        }

        if (spec.FollowsFileOrder && plan.Rows.Count > 0 && list.GetArrayLength() > 1)
            plan.Report.Notes.Add($"The order of the {view.SectionLabel.ToLowerInvariant()} in the file sets their order on the site; any not in the file follow after them.");
        return plan;
    }

    /// <summary>A name from the file, as short and plain as a preview needs it.</summary>
    private static string Display(string name)
    {
        var t = new string(name.Select(c => char.IsControl(c) ? ' ' : c).ToArray()).Trim();
        return t.Length <= 80 ? t : t[..79] + "…";
    }
}
