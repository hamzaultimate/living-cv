# Admin Redesign PR B: Template Picker Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the two template pickers (the wizard's radio-card list at `/admin/welcome` and the standalone card list at `/admin/template`) with one shared picker: a names box on the right, the selected template's live preview in the centre and a details strip under it. Delivers acceptance criteria 10 to 14 of `docs/superpowers/specs/2026-11-15-admin-redesign-design.md` (section 5).

**Architecture:** A pure `TemplatePickerBuilder` turns the catalog, the selected template, its preview pages and the untrusted `show`/`tab`/`size` values into a `TemplatePickerModel` (grouped names with tags, preview links, details). One data-driven partial, `_TemplatePicker.cshtml`, renders that model inside one card. Each host page (the admin Template page and wizard step 1) builds the model, then adds its own action button and notes with `with { Action = ..., Notes = ... }`. Selection, tabs and the width switch are plain links, so the picker needs no script. The names box is a `<details>` that CSS keeps open beside the preview on wide screens and collapses above it below 40rem.

**Tech Stack:** .NET 10, ASP.NET Core Razor Pages, xUnit, hand-written `admin.css`. No script and no build step are added.

## Global Constraints

Copied from the spec (sections 2, 3 and 5), plus repo rules.

- The admin CSP (`AdminAuth.cs`) is `default-src 'none'; style-src 'self'; img-src 'self'`, with `script-src 'self'` only where `AllowsScript` permits. No inline script and no inline style. The preview iframe needs `frame-src 'self'` on exactly the pages that show it.
- `admin.css` is one hand-written file with light and dark colour tokens. No CSS build step is added.
- Template wording drives labels. Nothing in the picker names a profession or a section in code; it reads the manifest (`TemplateDefinition`).
- Every form must work without script. Selection and preview use links and form buttons only.
- `show`, `tab` and `size` stay untrusted query values that only ever select a known catalog entry, a known preview page, or one of two widths. An unknown `show` falls back to the current template.
- Out of scope: the public site, the content editors, authentication, the template manifests, the wizard's other three steps and its step indicator (PR C), `OnboardingService`.
- Repo rules (`CONTRIBUTING.md`): admin pages inherit `AdminPageModel`, state changes are POST, match the surrounding code style, `dotnet build` stays warning-clean (baseline: 16 pre-existing NuGet advisory warnings NU1902/NU1903, not ours) and `dotnet test` stays green.
- Git rules: work only in this worktree, `E:\Repos\living-cv\.claude\worktrees\admin-redesign-pr-b`, on branch `feature/admin-redesign-pr-b` (created from `origin/develop` at `6f86f53`, which already contains PR A; the upstream was unset on purpose). Do not push and do not open a PR. **Never stage `src/Portfolio.Web/wwwroot/css/app.css`**: the build regenerates it and it shows as modified; always `git add` explicit paths. Do not switch branches in `E:\Repos\living-cv` (other sessions use that folder).
- Commit messages end with `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`.

## Decisions and spec interpretations (flag these in review)

1. **Wizard POST contract is unchanged.** The wizard still posts a field named `Key` to `/admin/welcome`, so `WizardFlow.PickTemplateAsync` and the security tests keep working. Only the markup around it changes: the radio is replaced by one hidden `Key` plus a button.
2. **Where the action button sits.** Spec 5.1 does not place it. It goes in the centre header beside the template name so it is visible without scrolling. Notes and the "before you publish" guidance go in the details strip.
3. **Names box is first in the DOM, right on screen.** CSS grid areas place it on the right. Keyboard and screen-reader order is names, then preview, which is the order a person acts in. Below 40rem the same order is the visual order.
4. **Narrow-screen collapse uses `::details-content`.** The names box is a `<details>` rendered closed. At 40rem and wider, inside `@supports selector(::details-content)`, CSS forces its content visible and hides the summary. A browser without `::details-content` keeps the summary visible and the list collapsed behind it: still fully usable, just one click more. No script is involved.
5. **The thumbnail is a visible link under the frame** (owner decision after the Task 2 review; spec 5.1 said "fallback content"). The first version put an `<img>` inside the `<iframe>`, which no browser displays. The picker now shows `Preview not loading? See a static picture of the home page.` under the frame, linking (new tab) to `/admin/welcome/thumb/{key}`, only for a template that ships a `preview.png`. Nothing is rendered inside the `<iframe>`. The `.adm-thumb*` styles and the `<img>` are gone from the picker; Task 3 removes the unused CSS.
6. **Headings.** The page `h1` is unchanged. The picker adds one visually hidden `h2` ("Templates") over the names box, profession headings as `h3`, and the selected template's name as `h2`. Criterion 7 (one `h1` per page) still holds.
7. **Wizard "selected" tag.** "Selected" marks the draft's template only. With no draft (first run) nothing is tagged, the preview shows the default template and the button reads "Use Software and continue". A draft on the shown template gives "Keep X and continue".
8. **Admin host, refused switch.** The button is disabled with a short reason beside it, and the existing "Cannot be switched to yet:" note stays (tests pin its text). The POST still refuses on its own; the disabled button is a convenience.
9. **`Review.cshtml.cs` keeps its own copy of the preview-pages expression.** PR C rebuilds that page; touching it here would widen the diff for no gain.
10. **Preview height is provisional.** The iframe height is `clamp(16rem, calc(100vh - 28rem), 44rem)` so criterion 10 (no page scroll at 1280 by 800) can hold. Visual fit cannot be asserted in a test, so Task 4 Step 3 checks it in a browser and may tune that one number.
11. **Old CSS is removed in Task 3,** once the wizard stops using it: `.adm-templates*`, `.adm-template[...]`, `.adm-pick*`. The two tests that name `.adm-pick` are updated to `.adm-choice`, which the accent radios still use.

## File Structure

Create:

| File | Responsibility |
|---|---|
| `src/Portfolio.Web/Services/TemplatePicker.cs` | Records (`TemplatePickerModel`, `PickerEntry`, `PickerGroup`, `PickerPage`, `PickerDetails`, `PickerNote`, `PickerAction`, `TemplatePickerInput`) and `TemplatePickerBuilder` (`Select`, `PagesOf`, `Build`). No HTML, no I/O beyond the catalog. |
| `src/Portfolio.Web/Pages/Admin/_TemplatePicker.cshtml` | The shared markup, driven only by `TemplatePickerModel`. |
| `tests/Portfolio.Web.Tests/Templates/TemplatePickerBuilderTests.cs` | Unit: grouping, tags, fallback, tab and size allow-lists, links, details, thumbnail. |
| `tests/Portfolio.Web.Tests/Admin/TemplatePickerPageTests.cs` | Integration: both hosts, every catalog template, tags, action buttons, refusal, CSP, CSS contract. |

Modify:

| File | Change |
|---|---|
| `src/Portfolio.Web/Pages/Admin/Template.cshtml` and `.cs` | Use the picker; keep the `h1` and lede; notes and Switch action built in the page model. |
| `src/Portfolio.Web/Pages/Admin/Welcome/Template.cshtml` and `.cs` | Use the picker; Use/Keep action; same POST handler contract. |
| `src/Portfolio.Web/Security/AdminAuth.cs:158-159` | `frame-src 'self'` also on exactly `/admin/welcome`. |
| `src/Portfolio.Web/wwwroot/css/admin.css` | Add picker styles; remove the old card-list and radio-card styles. |
| `docs/template-authoring-guide.md` | Two sentences about the thumbnail and the switch button. |
| Existing tests | `TemplateSwitchTests`, `OnboardingFlowTests`, `ArchetypeTemplateFlowTests`, `OnboardingAccessibilityAccentTests` (listed per task). |

Commands used throughout (run from the worktree root):

- One class: `dotnet test tests/Portfolio.Web.Tests --filter "FullyQualifiedName~TemplatePickerBuilderTests"`
- Whole suite: `dotnet test Portfolio.slnx`
- Build: `dotnet build Portfolio.slnx`

---

### Task 1: `TemplatePickerBuilder` and its model (criteria 11, 12, and the grouping and details of 10)

**Files:**
- Create: `src/Portfolio.Web/Services/TemplatePicker.cs`
- Test: `tests/Portfolio.Web.Tests/Templates/TemplatePickerBuilderTests.cs`

**Interfaces:**
- Produces (Tasks 2 and 3 call these exactly):
  - `TemplatePickerBuilder.Select(ITemplateCatalog catalog, string? show, string fallbackKey) -> TemplateDefinition`
  - `TemplatePickerBuilder.PagesOf(TemplateView view) -> IReadOnlyList<(string Path, string Label)>`
  - `TemplatePickerBuilder.Build(ITemplateCatalog catalog, TemplatePickerInput input) -> TemplatePickerModel`
  - `record TemplatePickerInput(string BasePath, TemplateDefinition Selected, IReadOnlyList<(string Path, string Label)> Pages, string? Tab, string? Size, string? MarkedKey, string MarkLabel)`
  - `record TemplatePickerModel(TemplateDefinition Selected, string? SelectedTag, IReadOnlyList<PickerGroup> Groups, IReadOnlyList<PickerPage> Pages, string PreviewPath, string PreviewUrl, bool Mobile, string DesktopUrl, string MobileUrl, string? ThumbUrl, PickerDetails Details)` with init properties `PickerAction? Action` and `IReadOnlyList<PickerNote> Notes` (default empty).
  - `record PickerGroup(string Profession, IReadOnlyList<PickerEntry> Entries)`, `record PickerEntry(string Key, string Name, string Url, bool IsSelected, string? Tag)`, `record PickerPage(string Path, string Label, string Url, bool IsCurrent)`, `record PickerDetails(IReadOnlyList<string> Pages, string? ItemLabel, bool HasSample, string? Guidance)`, `record PickerNote(string Heading, string Text, string? LinkUrl = null, string? LinkText = null)`, `record PickerAction(string Page, string Key, string Label, string? DisabledReason = null)`.

- [ ] **Step 1: Write the failing tests**

Create `tests/Portfolio.Web.Tests/Templates/TemplatePickerBuilderTests.cs`:

```csharp
using Portfolio.Web.Configuration;
using Portfolio.Web.Services;
using Portfolio.Web.Tests.Support;
using Xunit;

namespace Portfolio.Web.Tests.Templates;

/// <summary>The picker model: grouping, tags, the untrusted show/tab/size values, links and details. No HTML is involved.</summary>
public class TemplatePickerBuilderTests
{
    private static readonly TemplateCatalog Catalog = new(Path.Combine(AppContext.BaseDirectory, "Templates"));
    private static readonly IReadOnlyList<(string Path, string Label)> Pages = [("/", "Home"), ("/about", "About"), ("/blog", "Blog")];

    private static TemplatePickerModel Build(string key = "software", string? tab = null, string? size = null, string? marked = "software", string mark = "In use", ITemplateCatalog? catalog = null)
    {
        catalog ??= Catalog;
        return TemplatePickerBuilder.Build(catalog, new TemplatePickerInput("/admin/template", catalog.Get(key), Pages, tab, size, marked, mark));
    }

    [Fact]
    public void EveryTemplate_AppearsOnce_UnderItsProfession_WithTheDefaultFirst()
    {
        var model = Build();
        var keys = model.Groups.SelectMany(g => g.Entries).Select(e => e.Key).ToList();

        Assert.Equal(Catalog.All.Select(t => t.Key).OrderBy(k => k), keys.OrderBy(k => k));
        Assert.Equal(keys.Count, keys.Distinct().Count());
        Assert.Equal(Catalog.All.Select(t => t.Profession).Distinct(StringComparer.OrdinalIgnoreCase), model.Groups.Select(g => g.Profession));
        Assert.Equal("software", model.Groups[0].Entries[0].Key);
        foreach (var group in model.Groups)
            Assert.All(group.Entries, e => Assert.Equal(group.Profession, Catalog.Get(e.Key).Profession, ignoreCase: true));
    }

    [Fact]
    public void OnlyTheMarkedTemplate_GetsTheTag_AndOnlyTheSelectedOneIsSelected()
    {
        var model = Build("healthcare", marked: "software");
        var entries = model.Groups.SelectMany(g => g.Entries).ToList();

        Assert.Equal(["software"], entries.Where(e => e.Tag == "In use").Select(e => e.Key));
        Assert.Equal(["healthcare"], entries.Where(e => e.IsSelected).Select(e => e.Key));
        Assert.Null(model.SelectedTag);
        Assert.Equal("In use", Build("software").SelectedTag);
        Assert.All(Build(marked: null).Groups.SelectMany(g => g.Entries), e => Assert.Null(e.Tag));
        Assert.Equal("Selected", Build("healthcare", marked: "healthcare", mark: "Selected").SelectedTag);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no-such-template")]
    [InlineData("../software")]
    [InlineData("\"><script>")]
    public void AnUnknownShow_FallsBackToTheGivenTemplate(string? show) =>
        Assert.Equal("healthcare", TemplatePickerBuilder.Select(Catalog, show, "healthcare").Key);

    [Fact]
    public void AKnownShow_SelectsThatTemplate_AndAnUnknownFallbackFallsBackToTheDefault()
    {
        Assert.Equal("design-ux", TemplatePickerBuilder.Select(Catalog, "design-ux", "software").Key);
        Assert.Equal(TemplateCatalog.DefaultKey, TemplatePickerBuilder.Select(Catalog, null, "also-unknown").Key);
    }

    [Theory]
    [InlineData("/blog", "/blog")]
    [InlineData("/about", "/about")]
    [InlineData(null, "/")]
    [InlineData("", "/")]
    [InlineData("//evil.example/x", "/")]
    [InlineData("/not-a-page", "/")]
    [InlineData("https://evil.example/", "/")]
    public void TheTab_OnlyEverSelectsAPageOfTheList(string? tab, string expected)
    {
        var model = Build("healthcare", tab);

        Assert.Equal(expected, model.PreviewPath);
        Assert.Equal($"{expected}?preview=healthcare", model.PreviewUrl);
        Assert.Equal([expected], model.Pages.Where(p => p.IsCurrent).Select(p => p.Path));
    }

    [Theory]
    [InlineData("mobile", true)]
    [InlineData("desktop", false)]
    [InlineData(null, false)]
    [InlineData("MOBILE", false)]
    [InlineData("huge", false)]
    public void TheSize_IsOneOfTwoWidths_AndEveryLinkKeepsIt(string? size, bool mobile)
    {
        var model = Build("healthcare", "/blog", size);
        var word = mobile ? "mobile" : "desktop";

        Assert.Equal(mobile, model.Mobile);
        Assert.All(model.Groups.SelectMany(g => g.Entries), e => Assert.EndsWith($"&size={word}", e.Url));
        Assert.All(model.Pages, p => Assert.EndsWith($"&size={word}", p.Url));
        Assert.Equal("/admin/template?show=healthcare&tab=%2Fblog&size=desktop", model.DesktopUrl);
        Assert.Equal("/admin/template?show=healthcare&tab=%2Fblog&size=mobile", model.MobileUrl);
    }

    [Fact]
    public void TheLinks_AreBuiltFromTheBasePath_AndEscapeTheirValues()
    {
        var model = Build("healthcare");

        Assert.Equal("/admin/template?show=design-ux&size=desktop", model.Groups.SelectMany(g => g.Entries).Single(e => e.Key == "design-ux").Url);
        Assert.Equal("/admin/template?show=healthcare&tab=%2Fabout&size=desktop", model.Pages.Single(p => p.Path == "/about").Url);

        var wizard = TemplatePickerBuilder.Build(Catalog, new TemplatePickerInput("/admin/welcome", Catalog.Get("healthcare"), Pages, null, null, null, "Selected"));
        Assert.StartsWith("/admin/welcome?show=", wizard.Groups[0].Entries[0].Url);
    }

    [Fact]
    public void TheDetails_ComeFromTheManifest_ForEveryTemplate()
    {
        foreach (var t in Catalog.All)
        {
            var details = Build(t.Key).Details;
            var expectedPages = t.Sections.Where(s => s.Enabled && s.Key != SectionRegistry.Contact).Select(s => s.Label).Append("contact");

            Assert.Equal(expectedPages, details.Pages);
            Assert.Equal(t.Sections.FirstOrDefault(s => s.Key == SectionRegistry.Projects && s.Enabled)?.ItemLabel, details.ItemLabel);
            Assert.Equal(StarterContent.Offers(t), details.HasSample);
            Assert.Equal(t.Guidance, details.Guidance);
        }
        Assert.Equal("Initiative", Build("healthcare").Details.ItemLabel);
        Assert.Contains("patient-identifiable", Build("healthcare").Details.Guidance);
        Assert.True(string.IsNullOrWhiteSpace(Build("software").Details.Guidance));
    }

    [Fact]
    public void TheThumbnailUrl_IsOnlySetForATemplateThatShipsOne()
    {
        Assert.Equal("/admin/welcome/thumb/healthcare", Build("healthcare").ThumbUrl);

        var dir = TemplateDirs.WithExtra("plain", n => n["name"] = "Plain"); // this copy ships no preview.png for "plain"
        try
        {
            var catalog = new TemplateCatalog(dir);
            Assert.Null(Build("plain", catalog: catalog).ThumbUrl);
            Assert.Equal("/admin/welcome/thumb/software", Build("software", catalog: catalog).ThumbUrl);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void PagesOf_StartsAtHome_AndEveryLabelStartsWithACapital()
    {
        var view = new TemplateResolver(Catalog).Resolve(new SiteOptions { TemplateKey = "software" }, null);

        var pages = TemplatePickerBuilder.PagesOf(view);

        Assert.Equal(("/", "Home"), pages[0]);
        Assert.Equal(pages.Count, pages.Select(p => p.Path).Distinct().Count());
        Assert.All(pages, p => Assert.True(char.IsUpper(p.Label[0]), p.Label));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Portfolio.Web.Tests --filter "FullyQualifiedName~TemplatePickerBuilderTests"`
Expected: build FAIL, `The type or namespace name 'TemplatePickerBuilder' could not be found` (and the other new records).

- [ ] **Step 3: Write the implementation**

Create `src/Portfolio.Web/Services/TemplatePicker.cs`:

```csharp
namespace Portfolio.Web.Services;

/// <summary>One name in the picker's names box. <see cref="Tag"/> is "In use" or "Selected" for the template the host marks, else null.</summary>
public sealed record PickerEntry(string Key, string Name, string Url, bool IsSelected, string? Tag);

public sealed record PickerGroup(string Profession, IReadOnlyList<PickerEntry> Entries);

/// <summary>A preview tab. <see cref="Path"/> is always one of the pages the host passed in, never a caller's string.</summary>
public sealed record PickerPage(string Path, string Label, string Url, bool IsCurrent);

/// <summary>What the details strip shows, all from the manifest.</summary>
public sealed record PickerDetails(IReadOnlyList<string> Pages, string? ItemLabel, bool HasSample, string? Guidance);

/// <summary>A callout under the preview: a bold heading, a sentence and an optional link.</summary>
public sealed record PickerNote(string Heading, string Text, string? LinkUrl = null, string? LinkText = null);

/// <summary>The one button a host offers for the selected template. It is disabled, with the reason beside it, when <see cref="DisabledReason"/> is set.</summary>
public sealed record PickerAction(string Page, string Key, string Label, string? DisabledReason = null);

public sealed record TemplatePickerInput(
    string BasePath,
    TemplateDefinition Selected,
    IReadOnlyList<(string Path, string Label)> Pages,
    string? Tab,
    string? Size,
    string? MarkedKey,
    string MarkLabel);

public sealed record TemplatePickerModel(
    TemplateDefinition Selected,
    string? SelectedTag,
    IReadOnlyList<PickerGroup> Groups,
    IReadOnlyList<PickerPage> Pages,
    string PreviewPath,
    string PreviewUrl,
    bool Mobile,
    string DesktopUrl,
    string MobileUrl,
    string? ThumbUrl,
    PickerDetails Details)
{
    /// <summary>Set by the host with <c>with { Action = ... }</c>; null means no button (the template is already in use).</summary>
    public PickerAction? Action { get; init; }

    public IReadOnlyList<PickerNote> Notes { get; init; } = [];
}

/// <summary>
/// Builds the model behind the shared template picker. <c>show</c>, <c>tab</c> and <c>size</c> come from the query string and are untrusted:
/// they only ever pick a catalog template, one of the preview pages the host supplied, or one of two widths.
/// </summary>
public static class TemplatePickerBuilder
{
    private const string Desktop = "desktop";
    private const string Mobile = "mobile";

    /// <summary>The template to show: the catalog entry named by <paramref name="show"/>, else <paramref name="fallbackKey"/> (the default template when that is unknown too).</summary>
    public static TemplateDefinition Select(ITemplateCatalog catalog, string? show, string fallbackKey) =>
        catalog.TryGet(show, out var chosen) ? chosen : catalog.Get(fallbackKey);

    /// <summary>The preview tabs for a resolved template: Home, then the menu's pages in the order a visitor sees them.</summary>
    public static IReadOnlyList<(string Path, string Label)> PagesOf(TemplateView view) =>
        [("/", "Home"), .. view.NavLinks(mobile: true).Select(l => (l.Path, l.Path == "/cv" ? "CV" : TemplateView.Cap(l.Label)))];

    public static TemplatePickerModel Build(ITemplateCatalog catalog, TemplatePickerInput input)
    {
        var selected = input.Selected;
        var mobile = string.Equals(input.Size, Mobile, StringComparison.Ordinal);
        var size = mobile ? Mobile : Desktop;
        var previewPath = input.Pages.Select(p => p.Path).FirstOrDefault(p => p == input.Tab) ?? "/";
        var show = Uri.EscapeDataString(selected.Key);

        string Link(string path, string width) => $"{input.BasePath}?show={show}&tab={Uri.EscapeDataString(path)}&size={width}";

        var groups = catalog.All
            .GroupBy(t => t.Profession, StringComparer.OrdinalIgnoreCase)
            .Select(g => new PickerGroup(g.Key, g.Select(t => new PickerEntry(
                    t.Key,
                    t.Name,
                    $"{input.BasePath}?show={Uri.EscapeDataString(t.Key)}&size={size}",
                    t.Key == selected.Key,
                    t.Key == input.MarkedKey ? input.MarkLabel : null)).ToList()))
            .ToList();

        var pages = input.Pages.Select(p => new PickerPage(p.Path, p.Label, $"{input.BasePath}?show={show}&tab={Uri.EscapeDataString(p.Path)}&size={size}", p.Path == previewPath)).ToList();

        var details = new PickerDetails(
            selected.Sections.Where(s => s.Enabled && s.Key != SectionRegistry.Contact).Select(s => s.Label).Append("contact").ToList(),
            selected.Sections.FirstOrDefault(s => s.Key == SectionRegistry.Projects && s.Enabled)?.ItemLabel,
            StarterContent.Offers(selected),
            selected.Guidance);

        return new TemplatePickerModel(
            selected,
            selected.Key == input.MarkedKey ? input.MarkLabel : null,
            groups,
            pages,
            previewPath,
            $"{previewPath}?{RequestTemplate.PreviewParameter}={show}",
            mobile,
            Link(previewPath, Desktop),
            Link(previewPath, Mobile),
            catalog.GetPreviewImagePath(selected.Key) is not null ? $"/admin/welcome/thumb/{show}" : null,
            details);
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Portfolio.Web.Tests --filter "FullyQualifiedName~TemplatePickerBuilderTests"`
Expected: PASS, 25 tests (7 facts plus 18 theory rows), 0 failed, 0 skipped.

- [ ] **Step 5: Commit**

```bash
git add src/Portfolio.Web/Services/TemplatePicker.cs tests/Portfolio.Web.Tests/Templates/TemplatePickerBuilderTests.cs
git commit -m "feat(admin): template picker model and builder with allow-listed show, tab and size

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Shared partial, picker styles and the admin Template host (criteria 10, 11, 12, 13 on `/admin/template`)

**Files:**
- Create: `src/Portfolio.Web/Pages/Admin/_TemplatePicker.cshtml`
- Create: `tests/Portfolio.Web.Tests/Admin/TemplatePickerPageTests.cs`
- Modify: `src/Portfolio.Web/Pages/Admin/Template.cshtml` (whole file)
- Modify: `src/Portfolio.Web/Pages/Admin/Template.cshtml.cs` (whole file)
- Modify: `src/Portfolio.Web/wwwroot/css/admin.css` (add after line 98, the `.adm-preview-mobile` rule)
- Modify test: `tests/Portfolio.Web.Tests/Templates/TemplateSwitchTests.cs:261-262`

**Interfaces:**
- Consumes: everything Task 1 produces.
- Produces: the partial `/Pages/Admin/_TemplatePicker.cshtml` with `@model TemplatePickerModel`. Hosts include it with `<partial name="/Pages/Admin/_TemplatePicker.cshtml" model="Model.Picker" />`. It renders: a card `div.adm-picker`; a `<details class="adm-picker-names">` holding `<nav aria-label="Templates">`; the selected name as `<h2 id="picker-heading">`; the action as a POST form with a hidden `Key` and a button (the form carries the antiforgery token through `asp-page`); the preview tabs, width links and iframe; the details strip; the notes. Task 3 relies on those exact ids and strings.

- [ ] **Step 1: Write the failing tests and update the one old assertion**

Create `tests/Portfolio.Web.Tests/Admin/TemplatePickerPageTests.cs`:

```csharp
using System.Net;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Portfolio.Web.Services;
using Portfolio.Web.Tests.Support;
using Xunit;

namespace Portfolio.Web.Tests.Admin;

/// <summary>The shared template picker on /admin/template: every template selectable and previewable, tags, the Switch action and its refusal.</summary>
public class TemplatePickerAdminPageTests
{
    private static readonly TemplateCatalog Catalog = new(Path.Combine(AppContext.BaseDirectory, "Templates"));

    public static IEnumerable<object[]> Keys => Catalog.All.Select(t => new object[] { t.Key });

    private static string Plain(string html) => WebUtility.HtmlDecode(html);

    [Theory]
    [MemberData(nameof(Keys))]
    public async Task EveryTemplate_CanBeSelectedAndPreviewed_WithItsOwnDetails(string key)
    {
        var t = Catalog.Get(key);
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();

        var html = Plain(await owner.GetStringAsync($"/admin/template?show={key}"));

        Assert.Contains($"<h2 id=\"picker-heading\">{t.Name}", html);
        Assert.Contains($"src=\"/?preview={key}\"", html);
        Assert.Contains($"{t.Profession} · {TemplateView.Cap(t.Archetype)} layout", html);
        Assert.Contains(t.Description, html);
        Assert.Contains($"<strong>Pages:</strong> {string.Join(", ", t.Sections.Where(s => s.Enabled && s.Key != SectionRegistry.Contact).Select(s => s.Label))}, contact", html);
        if (string.IsNullOrWhiteSpace(t.Guidance)) Assert.DoesNotContain("Before you publish", html);
        else Assert.Contains(t.Guidance, html);
    }

    [Fact]
    public async Task TheNamesBox_ListsEveryTemplateByProfession_AndTagsTheOneInUse()
    {
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();

        var html = Plain(await owner.GetStringAsync("/admin/template?show=healthcare&size=mobile"));

        Assert.Equal(Catalog.All.Count, Regex.Matches(html, @"href=""/admin/template\?show=[a-z-]+&size=mobile""").Count);
        foreach (var profession in Catalog.All.Select(t => t.Profession).Distinct()) Assert.Contains($">{profession}</h3>", html);
        Assert.Single(Regex.Matches(html, @"</a><span class=""adm-pill"">In use</span>"));
        Assert.Contains("<a href=\"/admin/template?show=healthcare&size=mobile\" aria-current=\"true\">Healthcare</a>", html);
        Assert.Contains("<details class=\"adm-picker-names\">", html);
        Assert.Contains("<nav aria-label=\"Templates\">", html);
    }

    [Fact]
    public async Task TheSwitchButton_NamesTheTemplate_AndIsAbsentForTheOneInUse()
    {
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();

        var other = Plain(await owner.GetStringAsync("/admin/template?show=healthcare"));
        var current = Plain(await owner.GetStringAsync("/admin/template"));

        Assert.Contains(">Switch to Healthcare</button>", other);
        Assert.DoesNotMatch(@"<button\b[^>]*\bdisabled\b[^>]*>Switch to", other);
        Assert.Single(Regex.Matches(other, "name=\"Key\""));
        Assert.Contains("Nothing is saved or visible to visitors until you choose &quot;Switch to Healthcare&quot;", await owner.GetStringAsync("/admin/template?show=healthcare"));
        Assert.DoesNotContain("Switch to", current);
        Assert.Contains("In use", current);
    }

    [Fact]
    public async Task WhenASwitchWouldEmptyTheMenu_TheButtonIsDisabled_AndGivesAReason()
    {
        // "dormant" lists only about and contact, both off by default: with only the blog on, nothing can be kept there.
        var dir = TemplateDirs.WithExtra("dormant", node =>
        {
            node["name"] = "Dormant";
            node["sections"] = JsonNode.Parse("""[ { "key": "about", "label": "about", "enabled": false }, { "key": "contact", "label": "contact", "enabled": false } ]""");
        });
        try
        {
            using var app = AdminApp.WithTemplates(dir);
            var owner = await ContentFlow.BlankSiteAsync(app, "software");
            await owner.GetAsync("/admin");
            await SectionsFlow.SaveAsync(owner, r => r.Select(x => x with { Enabled = x.Key == "blog" }).ToList());

            var html = Plain(await owner.GetStringAsync("/admin/template?show=dormant"));

            Assert.Matches(@"<button\b[^>]*\bdisabled\b[^>]*>Switch to Dormant</button>", html);
            Assert.Contains("aria-describedby=\"picker-action-reason\"", html);
            Assert.Contains("id=\"picker-action-reason\"", html);
            Assert.Contains("<strong>Cannot be switched to yet:</strong>", html);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task ThePreviewFrame_HasATitle_AndTheThumbnailAsItsFallbackContent()
    {
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();

        var html = Plain(await owner.GetStringAsync("/admin/template?show=healthcare"));

        Assert.Matches(@"<iframe class=""adm-preview adm-picker-frame""[^>]* title=""Preview of the Healthcare template""", html);
        Assert.Contains("src=\"/admin/welcome/thumb/healthcare\"", html);
    }
}

/// <summary>admin.css is one hand-written file; the picker's layout rules must exist and must not rely on inline style.</summary>
public class TemplatePickerCssTests
{
    private static string Css()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "Portfolio.Web", "wwwroot", "css", "admin.css"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir.FullName, "src", "Portfolio.Web", "wwwroot", "css", "admin.css"));
    }

    [Fact]
    public void ThePickerStyles_PutTheNamesOnTheRight_CollapseThemBelowFortyRem_AndKeepThemOpenBesideThePreviewAbove()
    {
        var css = Css();

        Assert.Contains(".adm-picker {", css);
        Assert.Contains("grid-template-areas: \"detail names\"", css);
        Assert.Contains("grid-template-areas: \"names\" \"detail\"", css);
        Assert.Contains("@supports selector(::details-content)", css);
        Assert.Contains(".adm-picker-names::details-content { content-visibility: visible;", css);
        Assert.Matches(new Regex(@"@media \(max-width: 40rem\) \{\s*\.adm-picker \{"), css);
        Assert.Contains("overflow-y: auto", css); // the names box scrolls inside itself
    }
}
```

In `tests/Portfolio.Web.Tests/Templates/TemplateSwitchTests.cs` replace lines 261-262:

```csharp
        Assert.Contains("Use Healthcare", html);
        Assert.DoesNotContain("Use Software", html);
```

with:

```csharp
        Assert.Contains("Switch to Healthcare", html);
        Assert.DoesNotContain("Switch to Software", html);
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Portfolio.Web.Tests --filter "FullyQualifiedName~TemplatePickerAdminPageTests|FullyQualifiedName~TemplatePickerCssTests|FullyQualifiedName~TemplateSwitchTests"`
Expected: FAIL. The picker tests fail on missing `picker-heading` and `adm-picker` markup; `TheSwitcher_ShowsTheGuidanceForSensitiveTemplates_AndWhatStaysHidden` fails on `Switch to Healthcare`. Other `TemplateSwitchTests` still pass.

- [ ] **Step 3: Add the picker styles**

In `src/Portfolio.Web/wwwroot/css/admin.css`, directly after the line `.adm-preview-mobile { width: 390px; max-width: 100%; margin: 0 auto; }` (line 98), insert:

```css

/* Template picker (admin/template and setup wizard step 1): names box on the right, live preview and details in the centre.
   The names box comes first in the markup (so keyboard order is choose, then look) and is placed on the right by the grid. */
.adm-picker { display: grid; grid-template-columns: minmax(0, 1fr) 16rem; grid-template-areas: "detail names"; gap: var(--adm-space-5); align-items: start; padding: var(--adm-space-4); }
.adm-picker-names { grid-area: names; position: sticky; top: var(--adm-space-3); max-height: calc(100vh - 12rem); overflow-y: auto; padding: var(--adm-space-3); border: 1px solid var(--adm-border); border-radius: var(--adm-radius-sm); background: var(--adm-bg); }
.adm-picker-names summary { cursor: pointer; font-weight: 600; }
.adm-picker-names ul { list-style: none; margin: 0 0 var(--adm-space-3); padding: 0; }
.adm-picker-names li { margin: 0; }
.adm-picker-names li a { display: block; padding: var(--adm-space-1) var(--adm-space-2); border-radius: var(--adm-radius-sm); text-decoration: none; color: inherit; }
.adm-picker-names li a:hover { background: var(--adm-surface); }
.adm-picker-names li a[aria-current="true"] { background: var(--adm-surface); box-shadow: inset 3px 0 0 var(--adm-accent); font-weight: 600; }
.adm-picker-names .adm-group-heading { margin: var(--adm-space-3) 0 var(--adm-space-1); font-size: .8rem; letter-spacing: .04em; text-transform: uppercase; color: var(--adm-muted); }
.adm-picker-detail { grid-area: detail; min-width: 0; }
.adm-picker-head { display: flex; flex-wrap: wrap; align-items: flex-start; justify-content: space-between; gap: var(--adm-space-3); }
.adm-picker-head h2 { margin: 0; }
.adm-picker-head .adm-hint { margin-top: 0; }
.adm-picker-action { display: flex; flex-wrap: wrap; align-items: center; gap: var(--adm-space-2) var(--adm-space-3); margin: 0; }
.adm-picker-action .adm-hint { margin: 0; }
.adm-picker .adm-preview { height: clamp(16rem, calc(100vh - 28rem), 44rem); min-height: 0; }
.adm-picker-facts { display: grid; grid-template-columns: repeat(auto-fit, minmax(14rem, 1fr)); gap: 0 var(--adm-space-5); margin-top: var(--adm-space-3); }
.adm-picker-facts p { margin: var(--adm-space-1) 0; }
@media (max-width: 40rem) {
  .adm-picker { grid-template-columns: 1fr; grid-template-areas: "names" "detail"; }
  .adm-picker-names { position: static; max-height: none; }
}
/* The names box is a <details>. Wide screens keep it open beside the preview and hide its summary; a browser without ::details-content keeps the summary, so the list is one click away. */
@media (min-width: 40.01rem) {
  @supports selector(::details-content) {
    .adm-picker-names::details-content { content-visibility: visible; display: block; }
    .adm-picker-names summary { display: none; }
  }
}
```

- [ ] **Step 4: Write the shared partial**

Create `src/Portfolio.Web/Pages/Admin/_TemplatePicker.cshtml`:

```cshtml
@using Portfolio.Web.Services
@model TemplatePickerModel
@* The template picker, driven only by the model: choosing a name, a preview page or a width is a link, so nothing here needs script.
   The host supplies the one action button (Model.Action) and any notes (Model.Notes). *@
@{
    var t = Model.Selected;
    var action = Model.Action;
}
<div class="adm-card adm-picker">
    <details class="adm-picker-names">
        <summary>Templates: <strong>@t.Name</strong></summary>
        <nav aria-label="Templates">
            <h2 class="adm-sr">Templates</h2>
            @foreach (var group in Model.Groups)
            {
                <h3 class="adm-group-heading" id="group-@(group.Entries[0].Key)">@group.Profession</h3>
                <ul aria-labelledby="group-@(group.Entries[0].Key)">
                    @foreach (var entry in group.Entries)
                    {
                        <li><a href="@entry.Url" aria-current="@(entry.IsSelected ? "true" : null)">@entry.Name</a>@if (entry.Tag is not null) {<span class="adm-pill">@entry.Tag</span>}</li>
                    }
                </ul>
            }
        </nav>
    </details>

    <div class="adm-picker-detail">
        <div class="adm-picker-head">
            <div>
                <h2 id="picker-heading">@t.Name @if (Model.SelectedTag is not null) {<span class="adm-pill">@Model.SelectedTag</span>}</h2>
                <p class="adm-hint">@t.Profession · @TemplateView.Cap(t.Archetype) layout</p>
            </div>
            @if (action is not null)
            {
                <form method="post" asp-page="@action.Page" class="adm-picker-action">
                    <input type="hidden" name="Key" value="@action.Key" />
                    <button type="submit" class="adm-btn adm-btn-primary" disabled="@(action.DisabledReason is not null)" aria-describedby="@(action.DisabledReason is not null ? "picker-action-reason" : null)">@action.Label</button>
                    @if (action.DisabledReason is { } reason)
                    {
                        <span id="picker-action-reason" class="adm-hint">@reason</span>
                    }
                </form>
            }
        </div>
        <p>@t.Description</p>

        <nav class="adm-tabs" aria-label="Preview page">
            @foreach (var page in Model.Pages)
            {
                <a href="@page.Url" aria-current="@(page.IsCurrent ? "page" : null)">@page.Label</a>
            }
        </nav>
        <p class="adm-hint">
            Width:
            <a href="@Model.DesktopUrl" aria-current="@(Model.Mobile ? null : "true")">desktop</a> ·
            <a href="@Model.MobileUrl" aria-current="@(Model.Mobile ? "true" : null)">mobile</a> ·
            <a href="@Model.PreviewUrl" target="_blank" rel="noopener">open in a new tab</a>
        </p>
        <iframe class="adm-preview adm-picker-frame@(Model.Mobile ? " adm-preview-mobile" : "")" src="@Model.PreviewUrl"
                title="Preview of the @t.Name template" referrerpolicy="same-origin">
            @if (Model.ThumbUrl is not null)
            {
                <img class="adm-thumb" src="@Model.ThumbUrl" width="1200" height="800" alt="Preview of the @t.Name template: the home page of a sample site." />
            }
            else
            {
                <div class="adm-thumb adm-thumb-fallback" aria-hidden="true">@t.Name</div>
            }
        </iframe>

        <div class="adm-picker-facts">
            <p class="adm-included"><strong>Pages:</strong> @(string.Join(", ", Model.Details.Pages))</p>
            @if (Model.Details.ItemLabel is { Length: > 0 })
            {
                <p class="adm-included"><strong>A piece of work is called:</strong> @Model.Details.ItemLabel</p>
            }
            <p class="adm-included"><strong>Sample content:</strong> @(Model.Details.HasSample ? "available in the last step of setup (fictional example entries)" : "not available")</p>
        </div>
        @if (!string.IsNullOrWhiteSpace(Model.Details.Guidance))
        {
            <p class="adm-callout"><strong>Before you publish.</strong> @Model.Details.Guidance</p>
        }
        @foreach (var note in Model.Notes)
        {
            <p class="adm-callout" role="note"><strong>@note.Heading</strong> @note.Text @if (note.LinkUrl is not null) {<a href="@note.LinkUrl">@note.LinkText</a>}</p>
        }
    </div>
</div>
```

- [ ] **Step 5: Rewrite the admin Template page model**

Replace the whole of `src/Portfolio.Web/Pages/Admin/Template.cshtml.cs` with the following. The POST handler, `HiddenContentAsync`, `DisplayName` and `Cap` are unchanged; the page now builds a `TemplatePickerModel` instead of exposing the pieces separately.

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Portfolio.Data;
using Portfolio.Web.Security;
using Portfolio.Web.Services;

namespace Portfolio.Web.Pages.Admin;

// A template choice is one short key; anything much larger is not from this page.
[BodyLimit(16 * 1024)]
public class TemplateModel : AdminPageModel
{
    private readonly ProfileEditor _editor;
    private readonly ITemplateCatalog _catalog;
    private readonly TemplateResolver _resolver;
    private readonly PortfolioDbContext _db;

    public TemplateModel(ProfileEditor editor, ITemplateCatalog catalog, TemplateResolver resolver, PortfolioDbContext db)
    {
        _editor = editor;
        _catalog = catalog;
        _resolver = resolver;
        _db = db;
    }

    /// <summary>The template to preview. Untrusted: it only ever selects a catalog entry (see <see cref="TemplatePickerBuilder.Select"/>).</summary>
    [BindProperty(SupportsGet = true, Name = "show")]
    public string? Show { get; set; }

    /// <summary>Which page the preview opens on. Untrusted: it only ever selects one of the preview pages.</summary>
    [BindProperty(SupportsGet = true, Name = "tab")]
    public string? Tab { get; set; }

    /// <summary>"mobile" or anything else (desktop).</summary>
    [BindProperty(SupportsGet = true, Name = "size")]
    public string? Size { get; set; }

    /// <summary>The template chosen on the form being posted.</summary>
    [BindProperty]
    public string? Key { get; set; }

    /// <summary>What the shared picker partial renders, with this page's Switch action and notes added.</summary>
    public TemplatePickerModel Picker { get; private set; } = null!;

    public async Task OnGetAsync(CancellationToken ct)
    {
        var profile = await _editor.LoadAsync(ct);
        var currentKey = _catalog.Get(profile.TemplateKey).Key;
        var selected = TemplatePickerBuilder.Select(_catalog, Show, currentKey);
        var isCurrent = selected.Key == currentKey;

        var view = _resolver.Resolve(SiteProfileMapper.ToOptions(profile), selected.Key);
        IReadOnlyList<string> keeps = [];
        IReadOnlyList<string> reappearing = [];
        var refused = false;
        if (!isCurrent)
        {
            // What a switch would do, decided exactly as the POST decides it but on a detached copy (nothing is written): sections kept on, or a refusal.
            var plan = SectionSettings.PlanSwitch(_resolver, SiteProfileMapper.ToEntity(SiteProfileMapper.ToOptions(profile)), selected);
            keeps = plan.Kept;
            refused = !plan.Allowed;
            view = plan.Target; // the preview shows the menu the switch would leave
            // Sections the switch would bring back: on under the selected template, off under the current one (their content was kept all along).
            var now = _resolver.Resolve(SiteProfileMapper.ToOptions(profile), currentKey);
            reappearing = view.Sections.Where(s => s.Enabled && !now.IsEnabled(s.Key)).Select(s => Cap(s.Label)).ToList();
        }
        var hidden = await HiddenContentAsync(view, ct);

        var notes = new List<PickerNote>();
        var label = $"Switch to {selected.Name}";
        if (!isCurrent)
            notes.Add(new PickerNote("Preview only.", $"Nothing is saved or visible to visitors until you choose \"{label}\"."));
        if (hidden.Count > 0)
            notes.Add(new PickerNote("Hidden but kept:", $"{string.Join(", ", hidden.Select(h => $"{h.Label} ({h.Count})"))}. These stay in the database and reappear if you switch to a template that uses them."));
        if (reappearing.Count > 0)
            notes.Add(new PickerNote("Shown again:", $"{string.Join(", ", reappearing)}. They are on under this template, and their content was kept."));
        if (!isCurrent && refused)
            notes.Add(new PickerNote("Cannot be switched to yet:", "with your saved section choices this template would show no section in the menu, and none of the sections that are on now exist in it. Switch one of its sections on first.", "/admin/sections", "Open Sections"));
        else if (!isCurrent && keeps.Count > 0)
            notes.Add(new PickerNote("Kept on:", $"{string.Join(", ", keeps)}. This template shows them off by default, so they stay on if you switch (they are saved as your choice) and the menu is not left empty."));

        var picker = TemplatePickerBuilder.Build(_catalog, new TemplatePickerInput("/admin/template", selected, TemplatePickerBuilder.PagesOf(view), Tab, Size, currentKey, "In use"));
        Picker = picker with
        {
            Action = isCurrent ? null : new PickerAction("/Admin/Template", selected.Key, label, refused ? "Not available yet: see the note under the preview." : null),
            Notes = notes,
        };
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!_catalog.TryGet(Key, out var template))
        {
            FlashError("That template is not available.");
            return RedirectToPage();
        }

        // The sections page never leaves a site without a menu section; a switch must not either. The rule runs inside the write, on the row as it is then, so a sections save
        // that lands in the middle cannot defeat it. When the new template's defaults would empty the menu, the menu sections that are on now stay on.
        var kept = new List<string>();
        SectionSettings.SwitchPlan? plan = null;
        var outcome = await _editor.SetTemplateAsync(template, profile =>
        {
            plan = SectionSettings.PlanSwitch(_resolver, profile, template);
            kept = plan.Kept.ToList();
            return plan.Allowed;
        }, ct);
        if (outcome == SaveOutcome.Refused)
        {
            FlashError(SectionSettings.NoMenuMessage(template.Name, plan!.Target));
            return RedirectToPage();
        }
        if (outcome != SaveOutcome.Saved)
        {
            FlashError("Your profile was changed at the same moment, so the template was not switched. Please try again.");
            return RedirectToPage();
        }
        if (kept.Count > 0)
        {
            Flash($"Switched to the {template.Name} template. {string.Join(", ", kept)} stay on, although {template.Name} shows them off by default, so your menu is not left empty. Nothing was deleted.");
            return RedirectToPage();
        }
        Flash($"Switched to the {template.Name} template. Nothing was deleted: sections it does not use are hidden, and come back if you switch again.");
        return RedirectToPage();
    }

    private async Task<IReadOnlyList<(string Label, int Count)>> HiddenContentAsync(TemplateView view, CancellationToken ct)
    {
        var counts = new Dictionary<string, int>
        {
            [SectionRegistry.Experience] = await _db.Experiences.CountAsync(ct),
            [SectionRegistry.Projects] = await _db.Projects.CountAsync(ct),
            [SectionRegistry.Skills] = await _db.Skills.CountAsync(ct),
            [SectionRegistry.Certifications] = await _db.Certifications.CountAsync(ct),
            [SectionRegistry.Blog] = await _db.BlogPosts.CountAsync(ct),
            [SectionRegistry.Talks] = await _db.Talks.CountAsync(ct),
            [SectionRegistry.Testimonials] = await _db.Testimonials.CountAsync(ct),
        };
        return counts.Where(c => c.Value > 0 && !view.IsEnabled(c.Key))
            .Select(c => (Label: DisplayName(c.Key), Count: c.Value))
            .ToList();
    }

    private static string DisplayName(string key) => key switch
    {
        SectionRegistry.Experience => "Experience",
        SectionRegistry.Projects => "Projects",
        SectionRegistry.Skills => "Skills",
        SectionRegistry.Certifications => "Certifications",
        SectionRegistry.Blog => "Blog posts",
        SectionRegistry.Talks => "Talks",
        _ => "Testimonials",
    };

    private static string Cap(string value) => TemplateView.Cap(value);
}
```

(The old private `HasMenuSection` helper had no callers and is not carried over. If `dotnet build` reports an unused-member warning for anything else, remove only that member.)

- [ ] **Step 6: Rewrite the admin Template page markup**

Replace the whole of `src/Portfolio.Web/Pages/Admin/Template.cshtml` with:

```cshtml
@page "/admin/template"
@model TemplateModel
@{
    ViewData["Title"] = "Template";
}
<h1>Template</h1>
<p class="adm-lede">A template decides the layout, wording and which sections the public site shows. Choose a name to preview it here without saving, then switch. Switching never deletes anything: content for sections a template does not use is hidden, and returns when you switch back.</p>

<partial name="/Pages/Admin/_TemplatePicker.cshtml" model="Model.Picker" />
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test tests/Portfolio.Web.Tests --filter "FullyQualifiedName~TemplatePickerAdminPageTests|FullyQualifiedName~TemplatePickerCssTests|FullyQualifiedName~TemplateSwitchTests|FullyQualifiedName~SectionSettingsTests|FullyQualifiedName~AdminShellTests|FullyQualifiedName~AdminCspTests|FullyQualifiedName~ContentSecurityTests|FullyQualifiedName~AdminAccessTests|FullyQualifiedName~TemplatePreviewTests"`
Expected: PASS, 0 failed. `SectionSettingsTests` must still pass without edits: it pins `<strong>Shown again:</strong>`, `<strong>Cannot be switched to yet:</strong>`, `<strong>Kept on:</strong> Articles.` and `tab=%2Fblog`. If one of them fails, fix the partial or the note text, never the test.

- [ ] **Step 8: Commit**

```bash
git add src/Portfolio.Web/Pages/Admin/_TemplatePicker.cshtml src/Portfolio.Web/Pages/Admin/Template.cshtml src/Portfolio.Web/Pages/Admin/Template.cshtml.cs src/Portfolio.Web/wwwroot/css/admin.css tests/Portfolio.Web.Tests/Admin/TemplatePickerPageTests.cs tests/Portfolio.Web.Tests/Templates/TemplateSwitchTests.cs
git commit -m "feat(admin): shared template picker, used first on the Template page

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 3: Wizard step 1 uses the picker; the old card list and its styles go (criteria 10, 11, 12, 14 on `/admin/welcome`)

**Files:**
- Modify: `src/Portfolio.Web/Pages/Admin/Welcome/Template.cshtml` (whole file)
- Modify: `src/Portfolio.Web/Pages/Admin/Welcome/Template.cshtml.cs` (whole file)
- Modify: `src/Portfolio.Web/Security/AdminAuth.cs:157-159`
- Modify: `src/Portfolio.Web/wwwroot/css/admin.css` (remove old rules)
- Modify tests: `tests/Portfolio.Web.Tests/Admin/OnboardingFlowTests.cs:284-335`, `tests/Portfolio.Web.Tests/Templates/ArchetypeTemplateFlowTests.cs:64-68`, `tests/Portfolio.Web.Tests/Admin/OnboardingAccessibilityAccentTests.cs:121,183`
- Test (append): `tests/Portfolio.Web.Tests/Admin/TemplatePickerPageTests.cs`

**Interfaces:**
- Consumes: Task 1's builder; Task 2's partial (`<partial name="/Pages/Admin/_TemplatePicker.cshtml" model="Model.Picker" />`).
- Produces: `WelcomeTemplateModel` keeps its POST contract: field `Key`, handler `OnPostAsync`, success redirect to `/Admin/Welcome/Basics`, failure re-renders the page (200) with a `ModelState` error under `Key`.

- [ ] **Step 1: Add the failing wizard tests**

Append to `tests/Portfolio.Web.Tests/Admin/TemplatePickerPageTests.cs`, inside the namespace after the existing classes:

```csharp
/// <summary>The shared template picker on wizard step 1: every template selectable, saved to the draft only through the action button.</summary>
public class TemplatePickerWizardTests
{
    private static readonly TemplateCatalog Catalog = new(Path.Combine(AppContext.BaseDirectory, "Templates"));

    public static IEnumerable<object[]> Keys => Catalog.All.Select(t => new object[] { t.Key });

    private static string Plain(string html) => WebUtility.HtmlDecode(html);

    [Theory]
    [MemberData(nameof(Keys))]
    public async Task EveryTemplate_CanBeSelectedAndPreviewed_OnTheWizardStep(string key)
    {
        var t = Catalog.Get(key);
        using var app = new AdminApp();
        var owner = await WizardFlow.FirstRunClientAsync(app);

        var html = Plain(await owner.GetStringAsync($"{WizardFlow.Template}?show={key}"));

        Assert.Contains($"<h2 id=\"picker-heading\">{t.Name}", html);
        Assert.Contains($"src=\"/?preview={key}\"", html);
        Assert.Contains($"href=\"/admin/welcome/thumb/{key}\"", html);
        Assert.Contains($">Use {t.Name} and continue</button>", html);
        Assert.Contains($"<strong>Pages:</strong> {string.Join(", ", t.Sections.Where(s => s.Enabled && s.Key != SectionRegistry.Contact).Select(s => s.Label))}, contact", html);
        if (string.IsNullOrWhiteSpace(t.Guidance)) Assert.DoesNotContain("Before you publish", html);
        else Assert.Contains(t.Guidance, html);
    }

    [Fact]
    public async Task BrowsingNeverSaves_AndTheActionButtonSavesTheChoiceToTheDraft()
    {
        using var app = new AdminApp();
        var owner = await WizardFlow.FirstRunClientAsync(app);

        await owner.GetStringAsync($"{WizardFlow.Template}?show=healthcare&Key=healthcare");
        Assert.Null(await WizardFlow.DraftAsync(app));

        var posted = await WizardFlow.PickTemplateAsync(owner, "healthcare");

        Assert.Equal("/admin/welcome/basics", WizardFlow.Where(posted));
        Assert.Equal("healthcare", (await WizardFlow.DraftAsync(app))!.TemplateKey);
    }

    [Fact]
    public async Task TheChosenTemplate_IsTaggedSelected_AndOffersKeep_WhileOthersOfferUse()
    {
        using var app = new AdminApp();
        var owner = await WizardFlow.FirstRunClientAsync(app);
        var before = Plain(await owner.GetStringAsync($"{WizardFlow.Template}?show=healthcare"));
        Assert.DoesNotContain("adm-pill\">Selected<", before); // nothing is chosen until there is a draft
        await WizardFlow.PickTemplateAsync(owner, "healthcare");

        var chosen = Plain(await owner.GetStringAsync($"{WizardFlow.Template}?show=healthcare"));
        var other = Plain(await owner.GetStringAsync($"{WizardFlow.Template}?show=software"));

        Assert.Contains(">Keep Healthcare and continue</button>", chosen);
        Assert.Single(Regex.Matches(chosen, @"</a><span class=""adm-pill"">Selected</span>"));
        Assert.Contains(">Use Software and continue</button>", other);
        Assert.DoesNotContain(">Keep ", other);
        Assert.Contains("<a href=\"/admin/welcome?show=healthcare&size=desktop\"", other); // the names box links back to the chosen one
    }

    [Theory]
    [InlineData("nope")]
    [InlineData("%22%3E%3Cscript%3E")]
    [InlineData("../software")]
    public async Task AnUnknownShow_FallsBackToTheTemplateTheDraftNames(string show)
    {
        using var app = new AdminApp();
        var owner = await WizardFlow.FirstRunClientAsync(app);
        await WizardFlow.PickTemplateAsync(owner, "healthcare");

        var html = await owner.GetStringAsync($"{WizardFlow.Template}?show={show}&tab=//evil.example/x");

        Assert.Contains("src=\"/?preview=healthcare\"", html);
        Assert.DoesNotContain("evil.example", html);
        Assert.DoesNotContain("<script>", html);
    }

    [Fact]
    public async Task APostedKeyOutsideTheCatalog_IsRefused_AndTheDraftIsUntouched()
    {
        using var app = new AdminApp();
        var owner = await WizardFlow.FirstRunClientAsync(app);

        var response = await WizardFlow.PickTemplateAsync(owner, "no-such-template");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Choose one of the templates.", await response.Content.ReadAsStringAsync());
        Assert.Null(await WizardFlow.DraftAsync(app));
    }

    [Fact]
    public async Task OnlyTheTemplateStepAndTheReviewStep_MayFrameThePublicSite()
    {
        using var app = new AdminApp();
        var owner = await WizardFlow.FirstRunClientAsync(app);
        await WizardFlow.PickTemplateAsync(owner, "software");
        await WizardFlow.SaveBasicsAsync(owner);
        static string Csp(HttpResponseMessage r) => string.Join(";", r.Headers.GetValues("Content-Security-Policy"));

        Assert.Contains("frame-src 'self'", Csp(await owner.GetAsync(WizardFlow.Template)));
        Assert.Contains("frame-src 'self'", Csp(await owner.GetAsync(WizardFlow.Review)));
        Assert.DoesNotContain("frame-src", Csp(await owner.GetAsync(WizardFlow.Basics)));
        Assert.DoesNotContain("frame-src", Csp(await owner.GetAsync(WizardFlow.Photo)));
    }

    [Fact]
    public async Task ATemplateWithoutAThumbnail_OffersNoStaticPictureLink()
    {
        var dir = TemplateDirs.WithExtra("plain", n => n["name"] = "Plain");
        try
        {
            using var app = AdminApp.WithTemplates(dir);
            var owner = await app.SignedInClientAsync();

            var html = await owner.GetStringAsync($"{WizardFlow.Template}?show=plain");

            Assert.DoesNotContain("/admin/welcome/thumb/plain", html);
            Assert.DoesNotContain("static picture", html);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
```

- [ ] **Step 2: Update the old tests that pin the removed card markup**

(a) `tests/Portfolio.Web.Tests/Admin/OnboardingFlowTests.cs`: replace the whole `TheTemplateStep_ShowsCardsGroupedByProfession_WithThumbnailsAndWhatIsIncluded` test (lines 284-316) with:

```csharp
    [Fact]
    public async Task TheTemplateStep_ListsEveryTemplateByProfession_AndShowsOnlyTheSelectedOnesDetails()
    {
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();

        var html = WebUtility.HtmlDecode(await owner.GetStringAsync(WizardFlow.Template));

        foreach (var profession in new[] { "Software engineer", "Healthcare professional", "Product designer", "Photographer and filmmaker", "Marketing and content strategist", "Sales and business development lead", "Finance and accounting professional" })
            Assert.Contains($">{profession}</h3>", html);
        Assert.Equal(7, Regex.Matches(html, @"href=""/admin/welcome\?show=[a-z-]+&size=desktop""").Count);
        Assert.Contains("<strong>Pages:</strong> about, experience, work, skills, certifications, blog, talks, testimonials, contact", html); // the default template, software
        Assert.DoesNotContain("Before you publish", html); // software carries no guidance, and no other template's details are on the page

        var healthcare = WebUtility.HtmlDecode(await owner.GetStringAsync(WizardFlow.Template + "?show=healthcare"));
        Assert.Contains("licences & registrations", healthcare);
        Assert.Contains("Initiative", healthcare);
        Assert.Contains("patient-identifiable", healthcare);
        Assert.Single(Regex.Matches(healthcare, "Before you publish")); // only the selected template's guidance is on the page
        Assert.Contains("href=\"/admin/welcome/thumb/healthcare\"", healthcare);
        Assert.Equal(1, Regex.Matches(healthcare, "name=\"Key\"").Count);
    }
```

and rewrite `ATemplateWithoutAThumbnail_GetsADataDrivenCard` (now line ~318) as `ATemplateWithoutAThumbnail_OffersNoStaticPictureLink`: change the request line `var html = await owner.GetStringAsync(WizardFlow.Template);` to `var html = await owner.GetStringAsync(WizardFlow.Template + "?show=plain");`, drop the two assertions about `adm-thumb adm-thumb-fallback` and `>Plain</div>`, add `Assert.DoesNotContain("static picture", html);`, and keep `Assert.DoesNotContain("/admin/welcome/thumb/plain", html);` and the `NotFound` check on `GET /admin/welcome/thumb/plain`.

(b) `tests/Portfolio.Web.Tests/Templates/ArchetypeTemplateFlowTests.cs` lines 64-65: replace

```csharp
        var step = Plain(await owner.GetStringAsync(WizardFlow.Template));
        Assert.Contains($">{template.Profession}</h2>", step);
```

with

```csharp
        var step = Plain(await owner.GetStringAsync($"{WizardFlow.Template}?show={key}"));
        Assert.Contains($">{template.Profession}</h3>", step);
```

Line 66 (`src="/admin/welcome/thumb/{key}"`) becomes `Assert.Contains($"href=\"/admin/welcome/thumb/{key}\"", step);`: the thumbnail is now a link under the frame. Lines 67-68 (`value="{key}"`, `template.Name`) stay: the key is the hidden `Key` value. The lines that fetch `/admin/welcome/thumb/{key}` directly and check `image/png` stay.

(c) `tests/Portfolio.Web.Tests/Admin/OnboardingAccessibilityAccentTests.cs`: line 121 `Assert.Contains("<legend>Choose a template</legend>", template);` becomes `Assert.Contains("<nav aria-label=\"Templates\">", template);` (the fieldset and legend parity check at lines 125-126 stays); line 183 `Assert.Contains(".adm-pick input[type=\"radio\"]:focus-visible", css);` becomes `Assert.Contains(".adm-choice input[type=\"radio\"]:focus-visible", css);`.

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/Portfolio.Web.Tests --filter "FullyQualifiedName~TemplatePickerWizardTests|FullyQualifiedName~OnboardingFlowTests|FullyQualifiedName~ArchetypeTemplateFlowTests|FullyQualifiedName~OnboardingAccessibilityAccentTests"`
Expected: FAIL. The wizard still renders radio cards, so `picker-heading`, `Use X and continue`, `</h3>` and the frame CSP assertions fail. The CSS assertion in `OnboardingAccessibilityAccentTests` for `.adm-choice ...:focus-visible` may already pass because that selector shares a line with `.adm-pick`; that is fine.

- [ ] **Step 4: Rewrite the wizard step page model**

Replace the whole of `src/Portfolio.Web/Pages/Admin/Welcome/Template.cshtml.cs` with:

```csharp
using Microsoft.AspNetCore.Mvc;
using Portfolio.Web.Security;
using Portfolio.Web.Services;

namespace Portfolio.Web.Pages.Admin.Welcome;

// A template choice is one short key; anything much larger is not from this page.
[BodyLimit(16 * 1024)]
public class WelcomeTemplateModel : WelcomePageModel
{
    private readonly TemplateResolver _resolver;

    public WelcomeTemplateModel(OnboardingService onboarding, ITemplateCatalog catalog, TemplateResolver resolver) : base(onboarding, catalog)
    {
        _resolver = resolver;
    }

    public override int CurrentStep => 1;

    /// <summary>The template chosen on the form being posted. Untrusted: it only ever selects a catalog entry (<see cref="ITemplateCatalog.TryGet"/>).</summary>
    [BindProperty]
    public string? Key { get; set; }

    /// <summary>The template to preview. Untrusted: it only ever selects a catalog entry (see <see cref="TemplatePickerBuilder.Select"/>).</summary>
    [BindProperty(SupportsGet = true, Name = "show")]
    public string? Show { get; set; }

    /// <summary>Which page the preview opens on. Untrusted: it only ever selects one of the preview pages.</summary>
    [BindProperty(SupportsGet = true, Name = "tab")]
    public string? Tab { get; set; }

    /// <summary>"mobile" or anything else (desktop).</summary>
    [BindProperty(SupportsGet = true, Name = "size")]
    public string? Size { get; set; }

    /// <summary>What the shared picker partial renders, with this step's Use or Keep action added.</summary>
    public TemplatePickerModel Picker { get; private set; } = null!;

    public async Task OnGetAsync(CancellationToken ct)
    {
        await LoadAsync(ct);
        BuildPicker(Show);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        await LoadAsync(ct);
        if (!Catalog.TryGet(Key, out var chosen))
        {
            ModelState.AddModelError(nameof(Key), "Choose one of the templates.");
            BuildPicker(null);
            return Page();
        }

        if (await Onboarding.SaveTemplateAsync(chosen, ct) is { } refused)
        {
            ModelState.AddModelError(nameof(Key), refused);
            BuildPicker(chosen.Key);
            return Page();
        }
        return RedirectToPage("/Admin/Welcome/Basics");
    }

    /// <summary>Shows <paramref name="show"/> when it names a template, else the template the draft (or, before one exists, the site) is on.</summary>
    private void BuildPicker(string? show)
    {
        var chosenKey = Catalog.TryGet(Draft?.TemplateKey, out var inDraft) ? inDraft.Key : null;
        var selected = TemplatePickerBuilder.Select(Catalog, show, Template.Key);
        var view = _resolver.Resolve(SiteProfileMapper.ToOptions(State.Profile), selected.Key);
        var label = selected.Key == chosenKey ? $"Keep {selected.Name} and continue" : $"Use {selected.Name} and continue";

        var picker = TemplatePickerBuilder.Build(Catalog, new TemplatePickerInput("/admin/welcome", selected, TemplatePickerBuilder.PagesOf(view), Tab, Size, chosenKey, "Selected"));
        Picker = picker with
        {
            Action = new PickerAction("/Admin/Welcome/Template", selected.Key, label),
            Notes = [new PickerNote("Preview only.", $"Your choice is saved to the setup draft when you choose \"{label}\". Visitors see nothing until you publish.")],
        };
    }
}
```

If `dotnet build` reports that `TemplateCard` is still referenced somewhere, it was only used by the old page; run `git grep -n TemplateCard` first and expect no remaining match outside the two files this task rewrites.

- [ ] **Step 5: Rewrite the wizard step markup**

Replace the whole of `src/Portfolio.Web/Pages/Admin/Welcome/Template.cshtml` with:

```cshtml
@page "/admin/welcome"
@model WelcomeTemplateModel
@{
    ViewData["Title"] = "Setup · Step 1 of 4 · Template";
}
<partial name="_WizardHeader" model="Model" />
<p class="adm-lede">A template sets the layout, the wording and which pages your site has. Pick the one closest to your profession. You can change it later at any time: nothing you add is ever deleted, sections a template does not use are only hidden.</p>

@if (!ViewData.ModelState.IsValid)
{
    <div id="error-summary" class="adm-flash adm-flash-error" role="alert" tabindex="-1" autofocus>
        <strong>Choose a template to continue.</strong>
        <div asp-validation-summary="All"></div>
    </div>
}
<partial name="/Pages/Admin/_TemplatePicker.cshtml" model="Model.Picker" />
<partial name="_WizardFooter" model="Model" />
```

- [ ] **Step 6: Let the wizard's first step frame the public site**

In `src/Portfolio.Web/Security/AdminAuth.cs`, replace lines 157-159:

```csharp
                    // The template page and the wizard's review step also frame the public site (same origin) to preview a template or the draft.
                    var frames = ctx.Request.Path.StartsWithSegments(TemplatePath, StringComparison.OrdinalIgnoreCase)
                                 || ctx.Request.Path.StartsWithSegments(WelcomeReviewPath, StringComparison.OrdinalIgnoreCase) ? " frame-src 'self';" : "";
```

with:

```csharp
                    // The template page, the wizard's template step (exactly /admin/welcome, not the steps under it) and the wizard's review step also
                    // frame the public site (same origin) to preview a template or the draft.
                    var frames = ctx.Request.Path.StartsWithSegments(TemplatePath, StringComparison.OrdinalIgnoreCase)
                                 || string.Equals(ctx.Request.Path.Value?.TrimEnd('/'), WelcomeTemplatePath, StringComparison.OrdinalIgnoreCase)
                                 || ctx.Request.Path.StartsWithSegments(WelcomeReviewPath, StringComparison.OrdinalIgnoreCase) ? " frame-src 'self';" : "";
```

and add the constant on the line after `public const string WelcomeReviewPath = "/admin/welcome/review";` (line 25):

```csharp
    public const string WelcomeTemplatePath = "/admin/welcome";
```

- [ ] **Step 7: Remove the styles the old pages used**

In `src/Portfolio.Web/wwwroot/css/admin.css`:

1. Delete these three lines (and the comment line `/* Template switcher */` above them, keeping the `.adm-pill` line that follows):

```css
.adm-templates { display: grid; gap: 1rem; grid-template-columns: repeat(auto-fit, minmax(18rem, 1fr)); margin-bottom: 1.25rem; }
.adm-templates .adm-card { margin-bottom: 0; }
.adm-template[aria-current="true"] { border-color: var(--adm-accent); box-shadow: 0 0 0 1px var(--adm-accent); }
```

   The comment becomes `/* Pills, callouts, preview tabs and frame (the template picker and the wizard review step) */` placed where `/* Template switcher */` was.

2. Delete these four lines:

```css
.adm-pick { display: flex; flex-direction: column; gap: .5rem; }
.adm-pick:focus-within { border-color: var(--adm-accent); box-shadow: 0 0 0 1px var(--adm-accent); }
.adm-pick-head { display: flex; align-items: center; gap: .6rem; }
.adm-pick p { margin: .25rem 0; }
```

2b. Also delete these two lines (the picker no longer renders an image or a name placeholder), and delete the width assertion at `OnboardingAccessibilityAccentTests.cs:186`, `Assert.Matches(new Regex(@"\.adm-thumb \{[^}]*width: 100%"), css);`:

```css
.adm-thumb { display: block; width: 100%; height: auto; aspect-ratio: 3 / 2; object-fit: cover; border: 1px solid var(--adm-border); border-radius: .35rem; background: var(--adm-bg); }
.adm-thumb-fallback { display: flex; align-items: center; justify-content: center; font-weight: 700; font-size: 1.25rem; color: var(--adm-muted); }
```

3. Replace these three lines (they mix `.adm-pick` and `.adm-choice` selectors)

```css
.adm-pick-head label, .adm-choice label { font-weight: 600; margin: 0; }
.adm-pick input[type="radio"], .adm-choice input[type="radio"] { width: 1.25rem; height: 1.25rem; accent-color: var(--adm-accent); flex: none; }
.adm-pick input[type="radio"]:focus-visible, .adm-choice input[type="radio"]:focus-visible { outline: 3px solid var(--adm-accent); outline-offset: 2px; }
```

   with

```css
.adm-choice label { font-weight: 600; margin: 0; }
.adm-choice input[type="radio"] { width: 1.25rem; height: 1.25rem; accent-color: var(--adm-accent); flex: none; }
.adm-choice input[type="radio"]:focus-visible { outline: 3px solid var(--adm-accent); outline-offset: 2px; }
```

Then run `git grep -n -E "adm-pick\b|adm-pick-head|adm-templates|adm-template\b" -- src tests` and expect no output except `adm-picker*` names (the regex has word boundaries so `adm-picker` does not match). The Tailwind-generated `app.css` is excluded from the grep on purpose.

- [ ] **Step 8: Run the focused tests, then the whole suite**

Run: `dotnet test tests/Portfolio.Web.Tests --filter "FullyQualifiedName~TemplatePicker|FullyQualifiedName~OnboardingFlowTests|FullyQualifiedName~ArchetypeTemplateFlowTests|FullyQualifiedName~OnboardingAccessibilityAccentTests|FullyQualifiedName~OnboardingSecurityTests"`
Expected: PASS, 0 failed.

Then run: `dotnet test Portfolio.slnx`
Expected: 0 failed. If a test outside the four updated files fails, it is a real regression or another test that pinned the old card markup (for example a test reading template names or guidance out of the wizard step). Do not edit it blindly: use `superpowers:systematic-debugging`, decide whether the assertion encodes the old layout or a behaviour that must survive, and change the test only in the first case, naming it in the commit message.

- [ ] **Step 9: Commit**

```bash
git add src/Portfolio.Web/Pages/Admin/Welcome/Template.cshtml src/Portfolio.Web/Pages/Admin/Welcome/Template.cshtml.cs src/Portfolio.Web/Security/AdminAuth.cs src/Portfolio.Web/wwwroot/css/admin.css tests/Portfolio.Web.Tests/Admin/TemplatePickerPageTests.cs tests/Portfolio.Web.Tests/Admin/OnboardingFlowTests.cs tests/Portfolio.Web.Tests/Templates/ArchetypeTemplateFlowTests.cs tests/Portfolio.Web.Tests/Admin/OnboardingAccessibilityAccentTests.cs
git commit -m "feat(admin): wizard step 1 uses the shared template picker; remove the radio-card list

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 4: Docs, full verification and the browser pass

**Files:**
- Modify: `docs/template-authoring-guide.md:207,212-215`

- [ ] **Step 1: Update the authoring guide**

In `docs/template-authoring-guide.md` replace `"Use ...". Anyone else gets the normal page` with `"Switch to ...". Anyone else gets the normal page` (line 207).

Replace the paragraph at lines 211-215 (from `Ship \`Templates/<key>/preview.png\`` to `instead of a picture.`) with:

```markdown
Ship `Templates/<key>/preview.png`, 1200 x 800, under about 600 KB: a real screenshot of the home page of a running site in
that template (dark mode, sample content) works well. The template picker finds it through the catalog by key
(`ITemplateCatalog.GetPreviewImagePath`) and serves it from `/admin/welcome/thumb/<key>`, owner-only: the key must be in the
catalog and the file name is fixed, so nothing in the request becomes a path. It is not a static file and needs no manifest
field. The picker's main view is the live public site in a frame; the thumbnail is offered as a "static picture" link under the
frame, for when the preview does not load. A template without one still works: the link is simply not shown.
```

- [ ] **Step 2: Run the real build and the whole suite**

Run: `dotnet build Portfolio.slnx` then `dotnet test Portfolio.slnx`
Expected: build succeeds with only the baseline NU1902/NU1903 advisory warnings (16); tests report 0 failed and a non-zero executed count. Report the real counts. A pass on zero executed tests is "not executed".

- [ ] **Step 3: Manual browser pass (criteria 10, 13 and the visual items; not assertable in a test)**

Start the app (`dotnet run --project src/Portfolio.Web`, then the printed HTTPS URL), sign in at `/admin/login`, and check each item at 1280 by 800 and at 390 by 844, in light and dark (the OS colour-scheme setting):

| Where | Check |
|---|---|
| Menu, Appearance, Template, 1280 by 800 | Names box on the right with profession headings; the selected name, live preview, tabs and the Switch button fit without scrolling the page; the names box scrolls inside itself if it outgrows the view. |
| Same page | Click each of the 7 names: preview, details and guidance change; "In use" stays on the live template; no template needs page scrolling to see its preview. |
| Same page | Tabs and the Desktop/Mobile links change only the frame; "open in a new tab" opens the public page. |
| Menu, Tools, Setup wizard (step 1), 1280 by 800 | Same layout; the button reads "Use X and continue"; after choosing and returning to step 1 the chosen template shows "Selected" and "Keep X and continue". |
| Both pages, 390 by 844 | Names box is above the preview inside a closed "Templates: X" summary; opening it lists the names; choosing one reloads with the new template selected. |
| Both pages, keyboard only | Tab order is names, then (Switch or Use) button, tabs, width links, frame; focus rings are visible on every link and button. |
| `/admin/template?show=<a template whose switch is refused>` if reproducible | Button disabled with the reason beside it. Otherwise this is covered by `WhenASwitchWouldEmptyTheMenu_TheButtonIsDisabled_AndGivesAReason`. |

If the preview needs page scrolling at 1280 by 800, change only the `28rem` in `.adm-picker .adm-preview { height: clamp(16rem, calc(100vh - 28rem), 44rem); }` (decision 10) until it fits on both pages, then re-run Step 2. Anything else that looks wrong is a defect to fix or to list as a known gap; it is not a reason to change a test.

- [ ] **Step 4: Commit**

```bash
git add docs/template-authoring-guide.md src/Portfolio.Web/wwwroot/css/admin.css
git commit -m "docs: describe the template picker, the Switch button and the thumbnail's new role

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

(Add `admin.css` only if Step 3 changed it. Never `app.css`.)

- [ ] **Step 5: Review before opening the PR**

Run the `quality-reviewer` agent on `git diff origin/develop...HEAD` with the acceptance criteria 10 to 14 and the decisions list above. Then hand over for a PR; do not push or open it without being asked.

---

## Acceptance coverage

| Criterion | Where it is checked |
|---|---|
| 10. Choosing a name shows live preview and details without page scrolling at 1280 by 800, on both hosts | Preview and details content: `EveryTemplate_CanBeSelectedAndPreviewed_WithItsOwnDetails`, `EveryTemplate_CanBeSelectedAndPreviewed_OnTheWizardStep`. No-scroll fit: manual, Task 4 Step 3 |
| 11. Every catalog template can be selected and previewed on both hosts | The two Theory tests above, one row per catalog template, so a new template is covered automatically |
| 12. Unknown `show` falls back to the current template | `AnUnknownShow_FallsBackToTheGivenTemplate`, `TheTab_OnlyEverSelectsAPageOfTheList`, `TheSize_IsOneOfTwoWidths_...` (unit); existing `TheSwitcher_IgnoresUntrustedShowAndTabValues` (admin); `AnUnknownShow_FallsBackToTheTemplateTheDraftNames` (wizard) |
| 13. `/admin/template` keeps the hidden, reappearing and kept-on notes and disables the switch with a reason | Existing `SectionSettingsTests` (note text, unchanged), `TheSwitcher_ShowsTheGuidanceForSensitiveTemplates_AndWhatStaysHidden`, new `WhenASwitchWouldEmptyTheMenu_TheButtonIsDisabled_AndGivesAReason` |
| 14. The wizard saves the choice to the draft only through its action button | `BrowsingNeverSaves_AndTheActionButtonSavesTheChoiceToTheDraft`, `APostedKeyOutsideTheCatalog_IsRefused_AndTheDraftIsUntouched`, existing `OnboardingSecurityTests` (GET with `?Key=` changes nothing) |
| 7 (carried from PR A): one `h1` and a breadcrumb on every page | Unchanged by this PR; the existing `AdminShellTests` run in Task 2 Step 7 and Task 3 Step 8 |
| 9 (carried): CSP | `OnlyTheTemplateStepAndTheReviewStep_MayFrameThePublicSite`; existing `OnlyThisPage_MayFrameThePublicSite`; `AdminCspTests` |

## How a person sees the change

- Template picker (live site): sign in at `/admin/login`, menu, Appearance, Template. Choose a name in the box on the right; use Switch to change the live site.
- Template picker (setup): menu, Tools, Setup wizard, step 1. "Use X and continue" saves to the draft and goes to Basics.
- Narrow screens (below 40rem): the names are behind the "Templates: X" summary above the preview.

## Self-review

**Spec coverage.** 5.1 layout: Task 2 (partial and CSS: names box on the right with an internal scroll, header, preview with tabs and width switch as links, details strip, narrow `<details>` with the selected name in its summary, thumbnail as frame fallback). 5.2 two hosts: Task 2 (Switch, "In use", the three notes, the refusal reason with a disabled button) and Task 3 (Use or Keep, "Selected", no extra notes). 5.3 units: Task 1 (`TemplatePickerModel` and `TemplatePickerBuilder`, one place for grouping and details; untrusted `show`, `tab`, `size`). Criteria 10 to 14: see the table. Nothing in section 5 lacks a task.

**Placeholder scan.** No "TBD", "TODO" or "add appropriate handling". Two conditional instructions are deliberate and actionable: Task 3 Step 8 says how to treat an unexpected failing test, and Task 4 Step 3 names the single CSS number to tune.

**Type consistency.** `Select`, `PagesOf` and `Build` have the same signatures in Task 1, Task 2 (`TemplateModel`) and Task 3 (`WelcomeTemplateModel`). `TemplatePickerInput` is built positionally in the same order everywhere: base path, selected, pages, tab, size, marked key, mark label. `PickerAction(Page, Key, Label, DisabledReason)` and `PickerNote(Heading, Text, LinkUrl, LinkText)` are used with those argument orders in both hosts. The ids `picker-heading` and `picker-action-reason`, the classes `adm-picker-names`, `adm-pill`, `adm-picker-thumb` and the strings asserted by tests all come from the partial in Task 2 Step 4.
