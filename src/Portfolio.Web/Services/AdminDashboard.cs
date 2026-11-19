namespace Portfolio.Web.Services;

public sealed record DashboardStatus(bool SetupPending, string TemplateName);

/// <summary>One thing still to do, with the page that fixes it.</summary>
public sealed record DashboardTask(string Text, string Url);

public sealed record DashboardCount(string Section, string Label, int Count, string Url, string? Detail);

/// <summary>
/// The dashboard's three blocks. A null block could not be read and is left out; an empty checklist means nothing is outstanding and is left out too.
/// </summary>
public sealed record DashboardModel(DashboardStatus? Status, IReadOnlyList<DashboardTask>? Checklist, IReadOnlyList<DashboardCount>? Counts)
{
    public static DashboardModel Empty { get; } = new(null, null, null);
}

/// <summary>Builds the dashboard from whatever could be read. Each block needs only its own inputs, so one failed read never takes the others with it.</summary>
public static class AdminDashboard
{
    public static DashboardModel Build(TemplateView? template, OnboardingState? state, string resumePath, ContentCounts? counts)
    {
        var status = template is not null && state is not null ? new DashboardStatus(state.FirstRun, template.Definition.Name) : null;

        IReadOnlyList<DashboardTask>? checklist = null;
        if (template is not null && state is not null && counts is not null)
        {
            var tasks = new List<DashboardTask>();
            if (state.FirstRun) tasks.Add(new("Finish the setup wizard", resumePath));
            if (string.IsNullOrWhiteSpace(state.Profile.PhotoUrl)) tasks.Add(new("Add a profile photo", "/admin/photo"));
            foreach (var section in EditorFields.Sections)
            {
                var view = new EditorView(template, section);
                if (view.SectionEnabled && counts.Entries.GetValueOrDefault(section) == 0)
                    tasks.Add(new($"Add your first {view.ItemLabelLower} to {view.SectionLabel}", $"/admin/content/{section}/edit"));
            }
            checklist = tasks;
        }

        IReadOnlyList<DashboardCount>? rows = null;
        if (template is not null && counts is not null)
        {
            rows = EditorFields.Sections.Select(section =>
            {
                var total = counts.Entries.GetValueOrDefault(section);
                var detail = section == SectionRegistry.Blog && total > 0
                    ? $"{total - counts.BlogDrafts} published, {counts.BlogDrafts} {(counts.BlogDrafts == 1 ? "draft" : "drafts")}"
                    : null;
                return new DashboardCount(section, new EditorView(template, section).SectionLabel, total, $"/admin/content/{section}", detail);
            }).ToList();
        }

        return new DashboardModel(status, checklist, rows);
    }
}
