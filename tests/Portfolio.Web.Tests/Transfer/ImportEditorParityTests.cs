using System.Net;
using Portfolio.Web.Tests.Support;
using Portfolio.Web.Transfer;
using Xunit;

namespace Portfolio.Web.Tests.Transfer;

/// <summary>
/// The import repeats a few limits that the editors keep inside their own methods. These tests send the same value to the editor (a real post) and to the import (a real plan):
/// if the two ever disagree about a limit, one of them is wrong, and this fails.
/// </summary>
public class ImportEditorParityTests
{
    private sealed record Case(string Table, string EditorPath, string Field, int Max, Func<int, string, (Dictionary<string, string> Form, Dictionary<string, object?> Row)> Build);

    private static Dictionary<string, string> F(params (string Name, string Value)[] pairs) => pairs.ToDictionary(p => "Form." + p.Name, p => p.Value);

    private static Case Role(string field, int max) => new("experience", ContentFlow.Experience + "/edit", field, max, (i, value) =>
    {
        string Pick(string name, string fallback) => name == field ? value : fallback;
        return (F(("Company", Pick("company", "Co " + i)), ("Title", Pick("title", "Role " + i)), ("Start", "2020"), ("End", "2021"), ("Summary", Pick("summary", "Did things."))),
            new() { ["company"] = Pick("company", "Co " + i), ["title"] = Pick("title", "Role " + i), ["start"] = "2020", ["end"] = "2021", ["summary"] = Pick("summary", "Did things.") });
    });

    private static Case Project(string field, string formField, int max) => new("projects", ContentFlow.Projects + "/edit", field, max, (i, value) =>
    {
        string Pick(string name, string fallback) => name == field ? value : fallback;
        var form = F(("Title", Pick("title", "Project " + i)), ("Summary", Pick("summary", "A summary.")));
        var row = new Dictionary<string, object?> { ["title"] = Pick("title", "Project " + i), ["summary"] = Pick("summary", "A summary.") };
        if (field is not ("title" or "summary")) { form["Form." + formField] = value; row[field] = value; }
        return (form, row);
    });

    private static Case Cert(string field, int max) => new("certifications", ContentFlow.Certifications + "/edit", field, max, (i, value) =>
    {
        string Pick(string name, string fallback) => name == field ? value : fallback;
        return (F(("Name", Pick("name", "Cert " + i)), ("Issuer", Pick("issuer", "Board")), ("Status", "Earned"), ("Category", Pick("category", "Cat"))),
            new() { ["name"] = Pick("name", "Cert " + i), ["issuer"] = Pick("issuer", "Board"), ["status"] = "Earned", ["category"] = Pick("category", "Cat") });
    });

    private static Case Talk(string field, int max) => new("talks", ContentFlow.Talks + "/edit", field, max, (i, value) =>
    {
        string Pick(string name, string fallback) => name == field ? value : fallback;
        return (F(("Title", Pick("title", "Talk " + i)), ("Event", Pick("event", "Conf"))), new() { ["title"] = Pick("title", "Talk " + i), ["event"] = Pick("event", "Conf") });
    });

    private static Case Testimonial(string field, int max) => new("testimonials", ContentFlow.Testimonials + "/edit", field, max, (i, value) =>
    {
        string Pick(string name, string fallback) => name == field ? value : fallback;
        var quote = Pick("quote", "A quote that is long enough " + i);
        // The key is the author and the start of the quote: keep the attempts apart whichever field is under test.
        return (F(("Quote", quote), ("Author", Pick("author", "Person " + i)), ("AuthorTitle", Pick("authorTitle", "Role"))),
            new() { ["quote"] = quote, ["author"] = Pick("author", "Person " + i), ["authorTitle"] = Pick("authorTitle", "Role") });
    });

    private static Case Post(string field, string formField, int max) => new("blog", ContentFlow.Blog + "/edit", field, max, (i, value) =>
    {
        string Pick(string name, string fallback) => name == field ? value : fallback;
        return (F(("Title", Pick("title", "Post " + i)), ("Date", "2025-01-01"), ("Body", Pick("body", "Text.")), ("Description", Pick("description", "Short.")), ("Draft", "false")),
            new() { ["title"] = Pick("title", "Post " + i), ["date"] = "2025-01-01", ["body"] = Pick("body", "Text."), ["description"] = Pick("description", "Short.") });
    });

    private static Case Skill(string field, int max) => new("skills", ContentFlow.Skills + "/edit", field, max, (i, value) =>
    {
        string Pick(string name, string fallback) => name == field ? value : fallback;
        return (F(("Name", Pick("name", "Skill " + i)), ("Group", Pick("group", "Group"))), new() { ["name"] = Pick("name", "Skill " + i), ["group"] = Pick("group", "Group") });
    });

    public static IEnumerable<object[]> Cases() =>
        new[]
        {
            Skill("name", 100), Skill("group", 60),
            Role("company", 200), Role("title", 200), Role("summary", 1500),
            Project("title", "Title", 200), Project("summary", "Summary", 600), Project("role", "Role", 200), Project("org", "Org", 200), Project("period", "Period", 60), Project("category", "Category", 80),
            Project("problem", "Problem", 3000), Project("approach", "Approach", 3000),
            Cert("name", 200), Cert("issuer", 160), Cert("category", 80),
            Talk("title", 200), Talk("event", 200),
            Testimonial("quote", 2000), Testimonial("author", 120), Testimonial("authorTitle", 200),
            Post("title", "Title", 200), Post("description", "Description", 300), Post("body", "Body", 60_000),
        }.Select(c => new object[] { c.Table + "." + c.Field, c });

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task TheEditorAndTheImport_AgreeOnTheLimit_OfEveryField(string name, object boxed)
    {
        var c = (Case)boxed;
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        var attempt = 0;

        async Task<bool> EditorAcceptsAsync(int length)
        {
            var (form, _) = c.Build(++attempt, new string('a', length));
            var response = await ContentFlow.PostAsync(owner, c.EditorPath, form);
            return response.StatusCode == HttpStatusCode.Redirect;
        }

        async Task<bool> ImportAcceptsAsync(int length)
        {
            var (_, row) = c.Build(++attempt + 100, new string('a', length));
            var plan = await ImportHarness.PlanOkAsync(app, ImportHarness.Table(c.Table, row));
            // The value may name a row the editor has just written (a title, a name): then the import matches it, and what counts is that the row was not refused.
            return ImportHarness.OneRow(plan, c.Table).Status != RowStatus.Refused;
        }

        var editorAtLimit = await EditorAcceptsAsync(c.Max);
        var importAtLimit = await ImportAcceptsAsync(c.Max);
        var editorOver = await EditorAcceptsAsync(c.Max + 1);
        var importOver = await ImportAcceptsAsync(c.Max + 1);

        Assert.True(editorAtLimit, $"{name}: the editor refuses {c.Max} characters, so this test cannot tell");
        Assert.True(importAtLimit, $"{name}: the import refuses {c.Max} characters, which the editor accepts");
        Assert.False(editorOver, $"{name}: the editor accepts {c.Max + 1} characters, so this test cannot tell");
        Assert.False(importOver, $"{name}: the import accepts {c.Max + 1} characters, which the editor refuses");
    }

    [Fact]
    public async Task TheListLimits_AgreeToo_ImpactTechHighlightsAndLinks()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        var n = 0;

        async Task<bool> EditorAsync(string path, Dictionary<string, string> form) => (await ContentFlow.PostAsync(owner, path, form)).StatusCode == HttpStatusCode.Redirect;
        async Task<bool> ImportAsync(string table, Dictionary<string, object?> row) => ImportHarness.OneRow(await ImportHarness.PlanOkAsync(app, ImportHarness.Table(table, row)), table).Status == RowStatus.Create;
        string Lines(int count, int length) => string.Join("\n", Enumerable.Range(0, count).Select(i => new string((char)('a' + i % 26), length)));
        string[] Items(int count, int length) => Enumerable.Range(0, count).Select(i => new string((char)('a' + i % 26), length)).ToArray();

        foreach (var (count, length, expected) in new[] { (8, 200, true), (9, 200, false), (8, 201, false) })
        {
            var title = "Impact " + ++n;
            Assert.Equal(expected, await EditorAsync(ContentFlow.Projects + "/edit", F(("Title", title), ("Summary", "S."), ("ImpactText", Lines(count, length)))));
            Assert.Equal(expected, await ImportAsync("projects", new() { ["title"] = title + " import", ["summary"] = "S.", ["impact"] = Items(count, length) }));
        }
        foreach (var (count, length, expected) in new[] { (20, 60, true), (21, 60, false), (20, 61, false) })
        {
            var title = "Tech " + ++n;
            Assert.Equal(expected, await EditorAsync(ContentFlow.Projects + "/edit", F(("Title", title), ("Summary", "S."), ("TechText", Lines(count, length)))));
            Assert.Equal(expected, await ImportAsync("projects", new() { ["title"] = title + " import", ["summary"] = "S.", ["tech"] = Items(count, length) }));
        }
        foreach (var (count, length, expected) in new[] { (12, 300, true), (13, 300, false), (12, 301, false) })
        {
            var company = "Highlights " + ++n;
            Assert.Equal(expected, await EditorAsync(ContentFlow.Experience + "/edit", F(("Company", company), ("Title", "T"), ("Start", "2020"), ("End", "2021"), ("Summary", "S."), ("HighlightsText", Lines(count, length)))));
            Assert.Equal(expected, await ImportAsync("experience", new() { ["company"] = company + " import", ["title"] = "T", ["start"] = "2020", ["end"] = "2021", ["summary"] = "S.", ["highlights"] = Items(count, length) }));
        }
    }

    [Fact]
    public async Task TheEditorsAndTheImport_AgreeOnTheirOtherRules_DatesSlugsStatusesAndAddresses()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        var n = 0;

        async Task<bool> EditorAsync(string path, Dictionary<string, string> form) => (await ContentFlow.PostAsync(owner, path, form)).StatusCode == HttpStatusCode.Redirect;
        async Task<bool> ImportAsync(string table, Dictionary<string, object?> row) => ImportHarness.OneRow(await ImportHarness.PlanOkAsync(app, ImportHarness.Table(table, row)), table).Status == RowStatus.Create;

        foreach (var (start, end) in new[] { ("2020", "2021"), ("Mar 2021", "2021-03"), ("last year", "2021"), ("2022", "2021"), ("2020", "soon"), ("1900", "2021"), ("2020-02-30", "2021") })
        {
            var company = "Dates " + ++n;
            var editor = await EditorAsync(ContentFlow.Experience + "/edit", F(("Company", company), ("Title", "T"), ("Start", start), ("End", end), ("Summary", "S.")));
            var import = await ImportAsync("experience", new() { ["company"] = company + " i", ["title"] = "T", ["start"] = start, ["end"] = end, ["summary"] = "S." });
            Assert.True(editor == import, $"dates {start} to {end}: editor {editor}, import {import}");
        }
        foreach (var slug in new[] { "fine-slug", "Mixed-Case", "has space", "a--b", "-lead", "trail-", "new", "admin", "api", "x/y", "ünï", new string('a', 120), new string('a', 121) })
        {
            var title = "Slug " + ++n;
            var editor = await EditorAsync(ContentFlow.Projects + "/edit", F(("Title", title), ("Summary", "S."), ("Slug", slug)));
            var plan = ImportHarness.OneRow(await ImportHarness.PlanOkAsync(app, ImportHarness.Table("projects", new Dictionary<string, object?> { ["title"] = title, ["summary"] = "S.", ["slug"] = slug })), "projects");
            // The editor lower-cases a typed name, and so does the import; both then check it the same way.
            Assert.True(editor == (plan.Status != RowStatus.Refused), $"slug '{slug}': editor {editor}, import {plan.Status} {ImportHarness.Why(plan)}");
        }
        foreach (var status in new[] { "Earned", "InProgress", "Planned", "earned", "Pending", "" })
        {
            var name = "Status " + ++n;
            var editor = await EditorAsync(ContentFlow.Certifications + "/edit", F(("Name", name), ("Issuer", "I"), ("Status", status)));
            var import = await ImportAsync("certifications", new() { ["name"] = name + " i", ["issuer"] = "I", ["status"] = status });
            Assert.True(editor == import || status == "earned", $"status '{status}': editor {editor}, import {import}"); // the import also takes any casing of a real name
        }
        foreach (var url in new[] { "https://example.com/x", "http://example.com", "javascript:alert(1)", "data:text/html,x", "//evil.example", "ftp://example.com", "http://u:p@example.com", "/relative", "", "example.com" })
        {
            var author = "Url " + ++n;
            var editor = await EditorAsync(ContentFlow.Testimonials + "/edit", F(("Quote", "A quote that is long enough " + n), ("Author", author), ("SourceUrl", url)));
            var import = await ImportAsync("testimonials", new() { ["quote"] = "A quote that is long enough " + n + " i", ["author"] = author + " i", ["sourceUrl"] = url });
            Assert.True(editor == import, $"address '{url}': editor {editor}, import {import}");
        }
    }
}
