# Admin Redesign PR A: Shell and Dashboard Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the admin top bar with a collapsible left menu, add a shared page header (breadcrumb), replace the dashboard's tile grid with status, checklist and counts, and refresh the admin CSS, delivering acceptance criteria 1 to 9 of `docs/superpowers/specs/2026-11-15-admin-redesign-design.md` (section 4).

**Architecture:** A pure `AdminMenu` service builds a menu model (groups, items, current item, breadcrumb) from the active `TemplateView`, the request path and optional counts. A thin scoped `AdminChrome` gathers the template and counts with failure tolerance and calls it. `_AdminLayout.cshtml` hosts the menu (rendered by `AdminMenuViewComponent`, markup only), the page header and the flash messages. One small first-party script, `admin-shell.js`, handles the collapse toggle, the cookie and the narrow-screen overlay. The dashboard is built by a pure `AdminDashboard` builder whose blocks are each omitted when their data is missing.

**Tech Stack:** .NET 10, ASP.NET Core Razor Pages, EF Core (SQLite in tests), xUnit, hand-written `admin.css`, one vanilla JS file. No build step is added.

## Global Constraints

Copied from the spec (sections 2 and 3), plus repo rules.

- The admin CSP (`AdminAuth.cs`) is `default-src 'none'; style-src 'self'; img-src 'self'`, with `script-src 'self'` only on paths `AllowsScript` permits. No inline script and no inline style. Icons are inline SVG in markup.
- `admin.css` is one hand-written file with light and dark colour tokens. No CSS build step is added.
- Template wording drives labels. A menu or dashboard label for a section comes from the active template (`EditorView`, `TemplateDefinition.Section(...).Label`), never from a hardcoded profession-specific string.
- Every form must work without script. Script only adds convenience.
- Out of scope: the public site, the fields of the content editors, authentication, and the template manifests. PR B (template picker) and PR C (wizard) are not started here.
- Build approach: server-rendered. One small script for the menu toggle.
- The dashboard must open even if the database is struggling. Any block whose data cannot be read is left out and the failure is logged. A failed count never breaks a page.
- Repo rules (`CONTRIBUTING.md`): admin pages inherit `AdminPageModel`, state changes are POST, match surrounding code style, `dotnet build` warning-clean (baseline has 16 pre-existing NuGet advisory warnings NU1902/NU1903, which are not ours) and `dotnet test` green.
- Git rules: work only in this worktree (`E:\Repos\living-cv\.claude\worktrees\admin-redesign`, branch `feature/admin-redesign`). Do not push and do not open a PR. **Never stage `src/Portfolio.Web/wwwroot/css/app.css`**: the build regenerates it and it shows as modified; always `git add` explicit paths.
- Commit messages end with `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`.

## Decisions and spec interpretations (flag these in review)

1. **`h1` placement.** Spec 4.4 says the shared header holds breadcrumb, `h1` and an action slot, and section 10 says PR A "changes the shell only and leaves page bodies as they are". About 22 pages already own an `<h1>` in their body, several with `id="page-heading"` that other markup refers to, and the wizard's `h1` carries focus and live-region behaviour. Moving them would touch every page body. **Decision:** the header renders the breadcrumb and an optional `Actions` section; every page keeps its own `h1`. Criterion 7 ("a breadcrumb and one `h1`") is enforced by a test that counts `<h1` on every page. Rework to a layout-owned `h1` can be a later cleanup.
2. **Criterion 7 scope.** "Every page" means every signed-in admin page. The login page and `/setup` render in the layout without the menu or breadcrumb.
3. **Breadcrumb shape** (spec 4.4: Admin / group item / page): `Admin / [parent menu item] / [menu item] / [page]`. The "group" in the spec's own example (Content / Projects / Edit) is the sub-menu parent, not the "Your site" heading. Pages below a menu item pass a last crumb through `ViewData["Crumb"]`, else `ViewData["Title"]` is used.
4. **Footer items** (View site, Sign out) and the collapse toggle are rendered by the view component, not carried in the model (Sign out is an anti-forgery POST form).
5. **Counts and the template** are read by `AdminChrome` so the menu degrades to neutral labels and no counts if either read fails (needed because the layout now loads them on every page, including ones that did not before). `RequestTemplate.GetAsync` becomes `virtual` so that failure path can be tested.
6. **Existing tests that encode the old chrome are updated first** (they assert the removed top bar, the tile grid, or "script only on edit forms"). They are listed per task.

## File Structure

Create:

| File | Responsibility |
|---|---|
| `src/Portfolio.Web/Services/AdminMenu.cs` | Menu model records, `AdminMenu.Build`, current-item resolution, breadcrumb, collapse-state helpers. No HTML. |
| `src/Portfolio.Web/Services/AdminContentCounts.cs` | Per-request, failure-tolerant counts of entries per section and blog drafts. |
| `src/Portfolio.Web/Services/AdminChrome.cs` | Gathers template and counts with failure tolerance, returns the menu model. |
| `src/Portfolio.Web/Services/AdminDashboard.cs` | Pure dashboard builder: status, checklist, counts. |
| `src/Portfolio.Web/Pages/Admin/AdminMenuViewComponent.cs` | Renders the menu model. No logic. |
| `src/Portfolio.Web/Pages/Shared/Components/AdminMenu/Default.cshtml` | Menu markup. |
| `src/Portfolio.Web/Pages/Shared/Components/AdminMenu/_MenuLink.cshtml` | One menu link. |
| `src/Portfolio.Web/Pages/Admin/_AdminIcon.cshtml` | Inline SVG icons by key. |
| `src/Portfolio.Web/wwwroot/js/admin-shell.js` | Toggle, cookie write, overlay open and close. Nothing else. |
| `tests/Portfolio.Web.Tests/Admin/AdminMenuTests.cs` | Unit: menu, current item, collapse helpers, breadcrumb. |
| `tests/Portfolio.Web.Tests/Admin/AdminChromeTests.cs` | Unit: counts and chrome failure tolerance. |
| `tests/Portfolio.Web.Tests/Admin/AdminDashboardTests.cs` | Unit and integration: dashboard blocks. |
| `tests/Portfolio.Web.Tests/Admin/AdminShellTests.cs` | Integration: menu on every page, collapse, no-script, breadcrumb and `h1`, CSP and script tag. |
| `tests/Portfolio.Web.Tests/Admin/AdminCssTests.cs` | The new CSS tokens exist in light and dark. |

Modify:

| File | Change |
|---|---|
| `src/Portfolio.Web/Security/AdminAuth.cs` | `AllowsScript` true for every admin path except the login page; comments. |
| `src/Portfolio.Web/Services/RequestTemplate.cs` | `GetAsync` becomes `virtual`. |
| `src/Portfolio.Web/Program.cs` | Register `AdminMenu`, `AdminContentCounts`, `AdminChrome`. |
| `src/Portfolio.Web/Pages/Admin/_AdminLayout.cshtml` | Shell, menu, page header, script tag. |
| `src/Portfolio.Web/Pages/Admin/Index.cshtml` and `.cs` | New dashboard. |
| `src/Portfolio.Web/wwwroot/css/admin.css` | Tokens, shell, menu, page header, dashboard, restyle. |
| `CONTRIBUTING.md` | The sentence about admin script. |
| Existing tests | See Tasks 1, 4 and 6. |

Commands used throughout (run from the worktree root):

- One test class: `dotnet test tests/Portfolio.Web.Tests --filter "FullyQualifiedName~AdminMenuTests"`
- Whole suite: `dotnet test Portfolio.slnx`
- Build: `dotnet build Portfolio.slnx`

---

### Task 1: CSP allows first-party script on every signed-in admin page (criterion 9)

**Files:**
- Modify: `src/Portfolio.Web/Security/AdminAuth.cs:155-161,186-199`
- Modify tests: `tests/Portfolio.Web.Tests/Admin/ContentSecurityTests.cs:245-271`, `tests/Portfolio.Web.Tests/Transfer/TransferSecurityTests.cs:190-195`, `tests/Portfolio.Web.Tests/Admin/OnboardingSecurityTests.cs:196-205`
- Create test: `tests/Portfolio.Web.Tests/Admin/AdminCspTests.cs`

**Interfaces:**
- Produces: `AdminAuth.AllowsScript(PathString)` (internal) returns true for any path under `/admin` except `/admin/login`. Task 4's layout calls it to decide whether to emit the script tag.

- [ ] **Step 1: Update the three existing tests to the new rule and add the login test**

In `ContentSecurityTests.cs` replace the test at lines 245-271:

```csharp
    [Fact]
    public async Task EveryAdminPage_MayLoadFirstPartyScript_ButNothingIsEverInline()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        var (seeded, stamp) = await SeedAsync(app);

        foreach (var path in Gets(seeded))
        {
            var response = await owner.GetAsync(path);
            var csp = string.Join(";", response.Headers.GetValues("Content-Security-Policy"));
            Assert.Contains("script-src 'self'", csp);
            Assert.DoesNotContain("unsafe-inline", csp);
            Assert.DoesNotContain("unsafe-eval", csp);
            Assert.Contains("default-src 'none'", csp);
            Assert.Contains("frame-ancestors 'none'", csp);
            Assert.Contains("no-store", string.Join(",", response.Headers.GetValues("Cache-Control")));
            Assert.Contains("noindex", string.Join(",", response.Headers.GetValues("X-Robots-Tag")));

            var html = await response.Content.ReadAsStringAsync();
            // No inline script or handler: the only script an admin page may carry is a src file.
            Assert.DoesNotMatch(@"<script(?![^>]*\ssrc=)", html);
            Assert.DoesNotMatch(@"\son[a-z]+\s*=", html);
            Assert.DoesNotContain("javascript:", html);
        }
    }
```

In `TransferSecurityTests.cs` line 192 replace `Assert.DoesNotContain("script-src", csp);` with `Assert.Contains("script-src 'self'", csp);` (leave line 195, the `<script\b` assertion, for Task 4).

In `OnboardingSecurityTests.cs` line 203 replace `Assert.DoesNotContain("script-src", csp);` with:

```csharp
            Assert.Contains("script-src 'self'", csp);
            Assert.DoesNotContain("unsafe-inline", csp);
```

Create `tests/Portfolio.Web.Tests/Admin/AdminCspTests.cs`:

```csharp
using System.Net;
using Portfolio.Web.Tests.Support;
using Xunit;

namespace Portfolio.Web.Tests.Admin;

/// <summary>Criterion 9: signed-in admin pages may load first-party script, the login page may not, and nothing is ever inline.</summary>
public class AdminCspTests
{
    [Fact]
    public async Task TheLoginPage_AllowsNoScript()
    {
        using var app = new AdminApp();
        var client = app.NewClient();
        Assert.Equal(HttpStatusCode.Redirect, (await app.RunSetupAsync(client)).StatusCode);

        var response = await client.GetAsync("/admin/login");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var csp = string.Join(";", response.Headers.GetValues("Content-Security-Policy"));
        Assert.DoesNotContain("script-src", csp);
        Assert.Contains("default-src 'none'", csp);
    }

    [Theory]
    [InlineData("/admin")]
    [InlineData("/admin/profile")]
    [InlineData("/admin/photo")]
    [InlineData("/admin/content")]
    [InlineData("/admin/sections")]
    [InlineData("/admin/template")]
    [InlineData("/admin/transfer")]
    [InlineData("/admin/welcome")]
    public async Task EverySignedInAdminPage_AllowsScriptFromItsOwnOrigin_AndNoInlineScript(string path)
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);

        var response = await owner.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var csp = string.Join(";", response.Headers.GetValues("Content-Security-Policy"));
        Assert.Contains("script-src 'self'", csp);
        Assert.DoesNotContain("unsafe-inline", csp);
        Assert.DoesNotContain("unsafe-eval", csp);
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/Portfolio.Web.Tests --filter "FullyQualifiedName~AdminCspTests|FullyQualifiedName~EveryAdminPage_MayLoadFirstPartyScript|FullyQualifiedName~NoScriptAndNoInlineHandler|FullyQualifiedName~WizardResponses_ArePrivate"`
Expected: FAIL. The `EverySignedInAdminPage...` theory rows, the content, transfer and wizard tests fail on `script-src 'self'` missing. `TheLoginPage_AllowsNoScript` already passes (it guards the other direction).

- [ ] **Step 3: Implement**

In `AdminAuth.cs` replace the comment at lines 155-156 with:

```csharp
                    // These pages load only their own stylesheet, photos and first-party script (the menu toggle, and the unsaved changes warning on
                    // the edit forms) and nothing inline. The login page, which a signed-out visitor reaches, loads no script.
```

Replace the `AllowsScript` summary and body (lines 186-199) with:

```csharp
    /// <summary>The admin area may load first-party script (<c>admin-shell.js</c> for the menu, <c>admin-unsaved.js</c> on the edit forms) and never inline script.
    /// Only the login page, which a signed-out visitor can reach, allows none.</summary>
    internal static bool AllowsScript(PathString path) =>
        path.StartsWithSegments(AdminPath, StringComparison.OrdinalIgnoreCase)
        && !path.StartsWithSegments(LoginPath, StringComparison.OrdinalIgnoreCase);
```

Delete the now-unused `private static readonly string[] ProfileEditorParts = ...;` line that followed it.

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test tests/Portfolio.Web.Tests --filter "FullyQualifiedName~AdminCspTests|FullyQualifiedName~ContentSecurityTests|FullyQualifiedName~TransferSecurityTests|FullyQualifiedName~OnboardingSecurityTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Portfolio.Web/Security/AdminAuth.cs tests/Portfolio.Web.Tests/Admin/ContentSecurityTests.cs tests/Portfolio.Web.Tests/Transfer/TransferSecurityTests.cs tests/Portfolio.Web.Tests/Admin/OnboardingSecurityTests.cs tests/Portfolio.Web.Tests/Admin/AdminCspTests.cs
git commit -m "feat(admin): allow first-party script on every signed-in admin page except login"
```

---

### Task 2: Menu model, current item, collapse helpers (criteria 1, 2, 6, part of 3)

**Files:**
- Create: `src/Portfolio.Web/Services/AdminMenu.cs`
- Test: `tests/Portfolio.Web.Tests/Admin/AdminMenuTests.cs`

**Interfaces:**
- Produces (used by Tasks 3, 4, 5):
  - `record AdminMenuItem(string Key, string Label, string Url, string Icon, int? Count, bool Hidden, bool IsCurrent, IReadOnlyList<AdminMenuItem> Children)` with `bool ContainsCurrent`.
  - `record AdminMenuGroup(string? Label, IReadOnlyList<AdminMenuItem> Items)`.
  - `record AdminMenuModel(IReadOnlyList<AdminMenuGroup> Groups)` with `Leaves`, `Current`, `ParentOf(AdminMenuItem)`.
  - `record AdminCrumb(string Label, string? Url)` (Url null means the current page).
  - `sealed class AdminMenu`: `AdminMenuModel Build(TemplateView? template, string path, IReadOnlyDictionary<string,int>? counts)`; `static IReadOnlyList<AdminCrumb> Breadcrumb(AdminMenuModel menu, string path, string? title, string? crumb)`; `static bool IsRailPage(string path)`; `static bool IsCollapsed(string path, string? cookie)`; constants `CollapsedCookie = "adm-menu"`, `CollapsedValue = "collapsed"`.
  - Item keys: `dashboard`, `profile` (children `profile.basics`, `profile.hero`, `profile.metrics`, `profile.about`, `profile.photo`), `content` (children `content.overview`, `content.<section>`), `template`, `sections`, `wizard`, `transfer`. Group labels: `null`, `Your site`, `Appearance`, `Tools`.

- [ ] **Step 1: Write the failing tests**

Create `tests/Portfolio.Web.Tests/Admin/AdminMenuTests.cs`:

```csharp
using System.Text.Json.Nodes;
using Portfolio.Data.Entities;
using Portfolio.Web.Configuration;
using Portfolio.Web.Services;
using Portfolio.Web.Tests.Support;
using Xunit;

namespace Portfolio.Web.Tests.Admin;

/// <summary>The menu model: structure, template wording, hidden sections, counts, the current item and the collapse rules. No HTML is involved.</summary>
public class AdminMenuTests
{
    private static readonly AdminMenu Menu = new();

    private static TemplateView Software(params SectionSetting[] sections) =>
        new TemplateResolver(new TemplateCatalog(Path.Combine(AppContext.BaseDirectory, "Templates")))
            .Resolve(new SiteOptions { TemplateKey = "software", Sections = sections.ToList() }, null);

    private static AdminMenuItem Find(AdminMenuModel model, string key) =>
        model.Groups.SelectMany(g => g.Items).SelectMany(i => new[] { i }.Concat(i.Children)).Single(i => i.Key == key);

    [Fact]
    public void TheGroupsAndItems_AreThoseOfTheSpec_InOrder()
    {
        var model = Menu.Build(Software(), "/admin", null);

        Assert.Equal([null, "Your site", "Appearance", "Tools"], model.Groups.Select(g => g.Label));
        Assert.Equal(["dashboard"], model.Groups[0].Items.Select(i => i.Key));
        Assert.Equal(["profile", "content"], model.Groups[1].Items.Select(i => i.Key));
        Assert.Equal(["template", "sections"], model.Groups[2].Items.Select(i => i.Key));
        Assert.Equal(["wizard", "transfer"], model.Groups[3].Items.Select(i => i.Key));
    }

    [Fact]
    public void ProfileAndContent_AreSubMenus_ThatReachEveryPageUnderThem()
    {
        var model = Menu.Build(Software(), "/admin", null);

        Assert.Equal(
            ["/admin/profile", "/admin/profile/hero", "/admin/profile/metrics", "/admin/profile/about", "/admin/photo"],
            Find(model, "profile").Children.Select(c => c.Url));
        Assert.Equal(
            ["/admin/content", .. EditorFields.Sections.Select(s => "/admin/content/" + s)],
            Find(model, "content").Children.Select(c => c.Url));
        Assert.Equal("/admin/profile", Find(model, "profile").Url);
        Assert.Equal("/admin/content", Find(model, "content").Url);
        Assert.Equal("/admin/template", Find(model, "template").Url);
        Assert.Equal("/admin/sections", Find(model, "sections").Url);
        Assert.Equal("/admin/welcome", Find(model, "wizard").Url);
        Assert.Equal("/admin/transfer", Find(model, "transfer").Url);
    }

    [Fact]
    public void Labels_ComeFromTheTemplatesWording_NotFromTheMenu()
    {
        var dir = TemplateDirs.WithExtra("quirky", node =>
        {
            foreach (var section in node["sections"]!.AsArray())
            {
                section!["label"] = "Zone " + section["key"]!.GetValue<string>();
                section.AsObject().Remove("shortLabel");
            }
            node["editor"] = JsonNode.Parse("""
            {
              "hero": { "fields": { "heading": { "label": "Zhero page" } } },
              "metrics": { "fields": { "heading": { "label": "Zmetrics page" } } },
              "about": { "fields": { "heading": { "label": "Zabout page" } } },
              "sections": { "fields": { "heading": { "label": "Zsec map" } } },
              "transfer": { "fields": { "heading": { "label": "Zport" } } }
            }
            """);
        });
        var view = new TemplateResolver(new TemplateCatalog(dir)).Resolve(new SiteOptions { TemplateKey = "quirky" }, null);

        var model = Menu.Build(view, "/admin", null);

        Assert.Equal("Zone experience", Find(model, "content.experience").Label);
        Assert.Equal("Zone testimonials", Find(model, "content.testimonials").Label);
        Assert.Equal("Zhero page", Find(model, "profile.hero").Label);
        Assert.Equal("Zmetrics page", Find(model, "profile.metrics").Label);
        Assert.Equal("Zabout page", Find(model, "profile.about").Label);
        Assert.Equal("Zsec map", Find(model, "sections").Label);
        Assert.Equal("Zport", Find(model, "transfer").Label);
    }

    [Fact]
    public void ASectionTheTemplateSwitchesOff_StaysInTheMenu_FlaggedHidden()
    {
        var model = Menu.Build(Software(new SectionSetting { Key = "talks", Enabled = false, Shown = false }), "/admin", null);

        var talks = Find(model, "content.talks");
        Assert.True(talks.Hidden);
        Assert.Equal("/admin/content/talks", talks.Url);
        Assert.All(EditorFields.Sections.Where(s => s != "talks"), s => Assert.False(Find(model, "content." + s).Hidden));
    }

    [Fact]
    public void Counts_AreAttachedToContentItems_AndLeftOffWhenTheyCouldNotBeRead()
    {
        var counts = new Dictionary<string, int> { ["experience"] = 3, ["skills"] = 0 };

        var withCounts = Menu.Build(Software(), "/admin", counts);
        var without = Menu.Build(Software(), "/admin", null);

        Assert.Equal(3, Find(withCounts, "content.experience").Count);
        Assert.Equal(0, Find(withCounts, "content.skills").Count);
        Assert.Null(Find(withCounts, "content.projects").Count); // not in the dictionary
        Assert.All(without.Leaves, l => Assert.Null(l.Count));
    }

    [Theory]
    [InlineData("/admin", "dashboard")]
    [InlineData("/admin/", "dashboard")]
    [InlineData("/admin/profile", "profile.basics")]
    [InlineData("/admin/profile/hero", "profile.hero")]
    [InlineData("/admin/profile/metrics", "profile.metrics")]
    [InlineData("/admin/photo", "profile.photo")]
    [InlineData("/admin/content", "content.overview")]
    [InlineData("/admin/content/", "content.overview")]
    [InlineData("/admin/content/experience", "content.experience")]
    [InlineData("/admin/content/projects/edit/4", "content.projects")]
    [InlineData("/admin/content/projects/images/4", "content.projects")]
    [InlineData("/admin/content/blog/delete/2", "content.blog")]
    [InlineData("/admin/sections", "sections")]
    [InlineData("/admin/template", "template")]
    [InlineData("/admin/welcome", "wizard")]
    [InlineData("/admin/welcome/review", "wizard")]
    [InlineData("/admin/transfer", "transfer")]
    [InlineData("/admin/transfer/confirm", "transfer")]
    [InlineData("/ADMIN/Content/Skills", "content.skills")]
    public void TheCurrentPage_IsTheItemWithTheLongestMatchingAddress(string path, string expected)
    {
        var model = Menu.Build(Software(), path, null);

        Assert.Equal(expected, model.Current?.Key);
        Assert.Single(model.Leaves, l => l.IsCurrent);
    }

    [Theory]
    [InlineData("/admin/profilex")]
    [InlineData("/admin/contents")]
    [InlineData("/admin/nothing-here")]
    [InlineData("/elsewhere")]
    public void AnAddressThatIsNotInTheMenu_MarksNothingCurrent(string path)
    {
        var model = Menu.Build(Software(), path, null);

        Assert.Null(model.Current);
        Assert.DoesNotContain(model.Leaves, l => l.IsCurrent);
    }

    [Fact]
    public void TheParentOfTheCurrentItem_ContainsIt_SoItsSubMenuRendersOpen()
    {
        var model = Menu.Build(Software(), "/admin/content/skills", null);

        Assert.True(Find(model, "content").ContainsCurrent);
        Assert.False(Find(model, "profile").ContainsCurrent);
        Assert.False(Find(model, "content").IsCurrent); // the parent itself is never "the page"
    }

    [Fact]
    public void WithoutATemplate_TheMenuStillBuilds_InNeutralWords()
    {
        var model = Menu.Build(null, "/admin/photo", null);

        Assert.Equal("profile.photo", model.Current?.Key);
        Assert.Equal("Experience", Find(model, "content.experience").Label);
        Assert.Equal("Home page introduction", Find(model, "profile.hero").Label);
        Assert.Equal("Export and import", Find(model, "transfer").Label);
        Assert.False(Find(model, "content.talks").Hidden);
    }

    [Theory]
    [InlineData("/admin/welcome", true)]
    [InlineData("/admin/welcome/basics", true)]
    [InlineData("/admin/welcome/review", true)]
    [InlineData("/ADMIN/WELCOME/photo", true)]
    [InlineData("/admin/welcomex", false)]
    [InlineData("/admin", false)]
    [InlineData("/admin/template", false)]
    public void TheWizardPages_AreRailPages(string path, bool expected) =>
        Assert.Equal(expected, AdminMenu.IsRailPage(path));

    [Theory]
    [InlineData("/admin", "collapsed", true)]
    [InlineData("/admin", null, false)]
    [InlineData("/admin", "", false)]
    [InlineData("/admin", "Collapsed", false)]
    [InlineData("/admin", "collapsed; x", false)]
    [InlineData("/admin", "expanded", false)]
    [InlineData("/admin/welcome", null, true)]
    [InlineData("/admin/welcome/basics", "", true)]
    public void TheCookie_IsOnlyEverComparedWithTheLiteral(string path, string? cookie, bool expected) =>
        Assert.Equal(expected, AdminMenu.IsCollapsed(path, cookie));
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Portfolio.Web.Tests --filter "FullyQualifiedName~AdminMenuTests"`
Expected: build FAIL, `AdminMenu`, `AdminMenuModel` and `AdminMenuItem` do not exist.

- [ ] **Step 3: Write the implementation**

Create `src/Portfolio.Web/Services/AdminMenu.cs`:

```csharp
namespace Portfolio.Web.Services;

/// <summary>One entry of the admin menu. A parent (Profile, Content) has <see cref="Children"/>; only a leaf is ever the current page.</summary>
public sealed record AdminMenuItem(string Key, string Label, string Url, string Icon, int? Count, bool Hidden, bool IsCurrent, IReadOnlyList<AdminMenuItem> Children)
{
    /// <summary>True for the current page and for a parent whose sub-menu holds it, so the sub-menu is drawn open.</summary>
    public bool ContainsCurrent => IsCurrent || Children.Any(c => c.IsCurrent);
}

public sealed record AdminMenuGroup(string? Label, IReadOnlyList<AdminMenuItem> Items);

public sealed record AdminMenuModel(IReadOnlyList<AdminMenuGroup> Groups)
{
    /// <summary>The items a page can be: a parent's children, or the item itself when it has none.</summary>
    public IEnumerable<AdminMenuItem> Leaves =>
        Groups.SelectMany(g => g.Items).SelectMany(i => i.Children.Count > 0 ? i.Children : new[] { i });

    public AdminMenuItem? Current => Leaves.FirstOrDefault(l => l.IsCurrent);

    public AdminMenuItem? ParentOf(AdminMenuItem leaf) =>
        Groups.SelectMany(g => g.Items).FirstOrDefault(i => i.Children.Any(c => c.Key == leaf.Key));
}

/// <summary>One step of the breadcrumb. A null <see cref="Url"/> is the page being shown.</summary>
public sealed record AdminCrumb(string Label, string? Url);

/// <summary>
/// Builds the admin menu from the template in effect and the request path. It returns a model, not markup: the view component draws it.
/// Section names come from the template's wording (<see cref="EditorView"/>); with no template (it could not be read) the neutral defaults of
/// <see cref="EditorFields"/> are used so a page never fails because of its menu.
/// </summary>
public sealed class AdminMenu
{
    /// <summary>The cookie the menu script writes. The server only ever compares it with <see cref="CollapsedValue"/>.</summary>
    public const string CollapsedCookie = "adm-menu";
    public const string CollapsedValue = "collapsed";

    private const string Dashboard = "/admin";
    private const string WizardUrl = "/admin/welcome";

    /// <summary>Wizard pages always draw the rail, whatever the cookie says, so the wizard keeps its room.</summary>
    public static bool IsRailPage(string path) => Covers(WizardUrl, path);

    public static bool IsCollapsed(string path, string? cookie) =>
        IsRailPage(path) || string.Equals(cookie, CollapsedValue, StringComparison.Ordinal);

    public AdminMenuModel Build(TemplateView? template, string path, IReadOnlyDictionary<string, int>? counts)
    {
        var content = new List<AdminMenuItem> { Item("content.overview", "Overview", "/admin/content", "") };
        foreach (var section in EditorFields.Sections)
        {
            var (label, enabled) = SectionName(template, section);
            int? count = counts is not null && counts.TryGetValue(section, out var n) ? n : null;
            content.Add(Item("content." + section, label, "/admin/content/" + section, "", count, hidden: !enabled));
        }

        AdminMenuGroup[] groups =
        [
            new(null, [Item("dashboard", "Dashboard", Dashboard, "dashboard")]),
            new("Your site",
            [
                Item("profile", "Profile", "/admin/profile", "profile", children:
                [
                    Item("profile.basics", "Basics", "/admin/profile", ""),
                    Item("profile.hero", PartLabel(template, ProfileParts.Hero), "/admin/profile/hero", ""),
                    Item("profile.metrics", PartLabel(template, ProfileParts.Metrics), "/admin/profile/metrics", ""),
                    Item("profile.about", PartLabel(template, ProfileParts.About), "/admin/profile/about", ""),
                    Item("profile.photo", "Photo", "/admin/photo", ""),
                ]),
                Item("content", "Content", "/admin/content", "content", children: content),
            ]),
            new("Appearance",
            [
                Item("template", "Template", "/admin/template", "template"),
                Item("sections", PartLabel(template, ProfileParts.Sections), "/admin/sections", "sections"),
            ]),
            new("Tools",
            [
                Item("wizard", "Setup wizard", WizardUrl, "wizard"),
                Item("transfer", PartLabel(template, ProfileParts.Transfer), "/admin/transfer", "transfer"),
            ]),
        ];

        var leaves = groups.SelectMany(g => g.Items).SelectMany(i => i.Children.Count > 0 ? i.Children : new[] { i });
        var current = leaves.Where(l => Covers(l.Url, path)).OrderByDescending(l => l.Url.Length).FirstOrDefault()?.Key;
        return new AdminMenuModel(groups.Select(g => g with { Items = g.Items.Select(i => Mark(i, current)).ToList() }).ToList());
    }

    /// <summary>
    /// Admin / [parent] / [menu item] / [page]. The page is the last crumb a page passes (<c>ViewData["Crumb"]</c>) or its title; it only appears
    /// below a menu item (an edit form under Projects), where the menu item becomes a link back.
    /// </summary>
    public static IReadOnlyList<AdminCrumb> Breadcrumb(AdminMenuModel menu, string path, string? title, string? crumb)
    {
        var last = string.IsNullOrWhiteSpace(crumb) ? title : crumb;
        if (menu.Current is not { } current)
            return string.IsNullOrWhiteSpace(last) ? [new("Admin", "/admin")] : [new("Admin", "/admin"), new(last, null)];
        if (current.Key == "dashboard") return [new("Admin", null)];

        var crumbs = new List<AdminCrumb> { new("Admin", "/admin") };
        if (menu.ParentOf(current) is { } parent) crumbs.Add(new(parent.Label, parent.Url));
        var deeper = !Normalise(path).Equals(current.Url, StringComparison.OrdinalIgnoreCase);
        if (deeper && !string.IsNullOrWhiteSpace(last) && !last.Equals(current.Label, StringComparison.OrdinalIgnoreCase))
        {
            crumbs.Add(new(current.Label, current.Url));
            crumbs.Add(new(last, null));
        }
        else
        {
            crumbs.Add(new(current.Label, null));
        }
        return crumbs;
    }

    private static AdminMenuItem Item(string key, string label, string url, string icon, int? count = null, bool hidden = false, IReadOnlyList<AdminMenuItem>? children = null) =>
        new(key, label, url, icon, count, hidden, false, children ?? []);

    private static AdminMenuItem Mark(AdminMenuItem item, string? current) =>
        item with { IsCurrent = current is not null && item.Children.Count == 0 && item.Key == current, Children = item.Children.Select(c => Mark(c, current)).ToList() };

    private static string Normalise(string path) => path.Length > 1 ? path.TrimEnd('/') : path;

    /// <summary>True when <paramref name="path"/> is the address or lies below it on a segment boundary. The dashboard only matches itself.</summary>
    private static bool Covers(string url, string path)
    {
        path = Normalise(path);
        return path.Equals(url, StringComparison.OrdinalIgnoreCase)
               || (url != Dashboard && path.StartsWith(url + "/", StringComparison.OrdinalIgnoreCase));
    }

    private static string PartLabel(TemplateView? template, string part) =>
        template is null ? EditorFields.For(part).First(f => f.Key == "heading").Label : new EditorView(template, part).Heading;

    private static (string Label, bool Enabled) SectionName(TemplateView? template, string section)
    {
        if (template is null) return (TemplateView.Cap(section), true);
        var view = new EditorView(template, section);
        return (view.SectionLabel, view.SectionEnabled);
    }
}
```

Notes for the implementer: `Item("...", "...", "...", "")` uses an empty icon key for children (the link partial draws no icon for an empty key). `Sections = sections.ToList()` in the test assumes `SiteOptions.Sections` is a `List<SectionSetting>`; if it is another collection type, adjust that one line.

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/Portfolio.Web.Tests --filter "FullyQualifiedName~AdminMenuTests"`
Expected: PASS (all theory rows).

- [ ] **Step 5: Commit**

```bash
git add src/Portfolio.Web/Services/AdminMenu.cs tests/Portfolio.Web.Tests/Admin/AdminMenuTests.cs
git commit -m "feat(admin): menu model with template wording, hidden sections and current-item rules"
```

---

### Task 3: Counts and chrome, both failure-tolerant (part of criterion 8, spec 4.1)

**Files:**
- Create: `src/Portfolio.Web/Services/AdminContentCounts.cs`, `src/Portfolio.Web/Services/AdminChrome.cs`
- Modify: `src/Portfolio.Web/Services/RequestTemplate.cs:32`, `src/Portfolio.Web/Program.cs:31`
- Test: `tests/Portfolio.Web.Tests/Admin/AdminChromeTests.cs`

**Interfaces:**
- Consumes: `AdminMenu.Build`, `AdminMenuModel` (Task 2).
- Produces:
  - `sealed record ContentCounts(IReadOnlyDictionary<string,int> Entries, int BlogDrafts)`.
  - `sealed class AdminContentCounts(PortfolioDbContext db, ILogger<AdminContentCounts> logger)` with `Task<ContentCounts?> GetAsync(CancellationToken ct = default)`: memoised per instance (scoped = per request), returns null and logs when the read fails.
  - `sealed class AdminChrome(AdminMenu menu, RequestTemplate template, AdminContentCounts counts, ILogger<AdminChrome> logger)` with `Task<AdminMenuModel> MenuAsync(string path, CancellationToken ct)`: never throws for a template or count failure.

- [ ] **Step 1: Write the failing tests**

Create `tests/Portfolio.Web.Tests/Admin/AdminChromeTests.cs`:

```csharp
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Portfolio.Data;
using Portfolio.Web.Services;
using Portfolio.Web.Tests.Support;
using Xunit;

namespace Portfolio.Web.Tests.Admin;

public class AdminChromeTests
{
    /// <summary>A database with no tables: every count query throws, as it would while the database is struggling.</summary>
    private static PortfolioDbContext BrokenDb() =>
        new(new DbContextOptionsBuilder<PortfolioDbContext>().UseSqlite(new SqliteConnection("DataSource=:memory:")).Options);

    private sealed class UnreadableTemplate : RequestTemplate
    {
        public UnreadableTemplate() : base(null!, null!, null!) { }
        public override Task<TemplateView> GetAsync() => throw new InvalidOperationException("the profile could not be read");
    }

    [Fact]
    public async Task Counts_AreCountedPerSection_WithBlogDraftsApart()
    {
        using var app = new AdminApp();
        await ContentFlow.BlankSiteAsync(app);
        await ContentFlow.SeedRowsAsync(app, pairs: true); // two rows in every table, one of the blog rows a draft
        using var db = app.CreateDbContext();

        var counts = await new AdminContentCounts(db, NullLogger<AdminContentCounts>.Instance).GetAsync();

        Assert.NotNull(counts);
        Assert.All(EditorFields.Sections, s => Assert.Equal(2, counts.Entries[s]));
        Assert.Equal(1, counts.BlogDrafts);
    }

    [Fact]
    public async Task Counts_AreNull_WhenTheyCannotBeRead_AndAskedOnlyOnce()
    {
        using var db = BrokenDb();
        var counts = new AdminContentCounts(db, NullLogger<AdminContentCounts>.Instance);

        var first = counts.GetAsync();
        var second = counts.GetAsync();

        Assert.Null(await first);
        Assert.Same(first, second); // one attempt per request, so a struggling database is not asked again for every block
    }

    [Fact]
    public async Task TheMenu_StillBuilds_WhenTheTemplateAndTheCountsCannotBeRead()
    {
        using var db = BrokenDb();
        var chrome = new AdminChrome(new AdminMenu(), new UnreadableTemplate(), new AdminContentCounts(db, NullLogger<AdminContentCounts>.Instance), NullLogger<AdminChrome>.Instance);

        var menu = await chrome.MenuAsync("/admin/photo", CancellationToken.None);

        Assert.Equal("profile.photo", menu.Current?.Key);
        Assert.Contains(menu.Leaves, l => l.Label == "Experience"); // neutral wording, not a failure
        Assert.All(menu.Leaves, l => Assert.Null(l.Count));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Portfolio.Web.Tests --filter "FullyQualifiedName~AdminChromeTests"`
Expected: build FAIL (`AdminContentCounts`, `AdminChrome` missing; `RequestTemplate.GetAsync` is not virtual).

- [ ] **Step 3: Implement**

`RequestTemplate.cs` line 32: `public Task<TemplateView> GetAsync() => _view ??= LoadAsync();` becomes `public virtual Task<TemplateView> GetAsync() => _view ??= LoadAsync();`

Create `src/Portfolio.Web/Services/AdminContentCounts.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Portfolio.Data;

namespace Portfolio.Web.Services;

/// <summary>How many entries each content section holds, and how many blog posts are drafts.</summary>
public sealed record ContentCounts(IReadOnlyDictionary<string, int> Entries, int BlogDrafts);

/// <summary>
/// The counts the menu and the dashboard show. Scoped, so a request asks the database once however many blocks want the numbers. A failed read is
/// logged and answered with null: the counts are a convenience and never break a page.
/// </summary>
public sealed class AdminContentCounts
{
    private readonly PortfolioDbContext _db;
    private readonly ILogger<AdminContentCounts> _logger;
    private Task<ContentCounts?>? _counts;

    public AdminContentCounts(PortfolioDbContext db, ILogger<AdminContentCounts> logger)
    {
        _db = db;
        _logger = logger;
    }

    public Task<ContentCounts?> GetAsync(CancellationToken ct = default) => _counts ??= LoadAsync(ct);

    private async Task<ContentCounts?> LoadAsync(CancellationToken ct)
    {
        try
        {
            var entries = new Dictionary<string, int>
            {
                [SectionRegistry.Experience] = await _db.Experiences.CountAsync(ct),
                [SectionRegistry.Skills] = await _db.Skills.CountAsync(ct),
                [SectionRegistry.Projects] = await _db.Projects.CountAsync(ct),
                [SectionRegistry.Certifications] = await _db.Certifications.CountAsync(ct),
                [SectionRegistry.Talks] = await _db.Talks.CountAsync(ct),
                [SectionRegistry.Testimonials] = await _db.Testimonials.CountAsync(ct),
                [SectionRegistry.Blog] = await _db.BlogPosts.CountAsync(ct),
            };
            return new ContentCounts(entries, await _db.BlogPosts.CountAsync(b => b.Draft, ct));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not count the entries for the admin menu and dashboard.");
            return null;
        }
    }
}
```

Create `src/Portfolio.Web/Services/AdminChrome.cs`:

```csharp
namespace Portfolio.Web.Services;

/// <summary>
/// Gathers what the menu needs (the template in effect and the entry counts) and builds it. The layout now does this on every admin page, including
/// ones that never read the template before, so a failure of either read must not fail the page: it logs and draws the menu in neutral words
/// and without counts.
/// </summary>
public sealed class AdminChrome
{
    private readonly AdminMenu _menu;
    private readonly RequestTemplate _template;
    private readonly AdminContentCounts _counts;
    private readonly ILogger<AdminChrome> _logger;

    public AdminChrome(AdminMenu menu, RequestTemplate template, AdminContentCounts counts, ILogger<AdminChrome> logger)
    {
        _menu = menu;
        _template = template;
        _counts = counts;
        _logger = logger;
    }

    public async Task<AdminMenuModel> MenuAsync(string path, CancellationToken ct)
    {
        TemplateView? view = null;
        try
        {
            view = await _template.GetAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not read the template for the admin menu; using neutral wording.");
        }
        var counts = await _counts.GetAsync(ct);
        return _menu.Build(view, path, counts?.Entries);
    }
}
```

`Program.cs`, after line 31 (`builder.Services.AddScoped<ContentEditor>();`) add:

```csharp
builder.Services.AddSingleton<AdminMenu>();
builder.Services.AddScoped<AdminContentCounts>();
builder.Services.AddScoped<AdminChrome>();
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/Portfolio.Web.Tests --filter "FullyQualifiedName~AdminChromeTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Portfolio.Web/Services/AdminContentCounts.cs src/Portfolio.Web/Services/AdminChrome.cs src/Portfolio.Web/Services/RequestTemplate.cs src/Portfolio.Web/Program.cs tests/Portfolio.Web.Tests/Admin/AdminChromeTests.cs
git commit -m "feat(admin): failure-tolerant entry counts and menu chrome"
```

---

### Task 4: The shell: layout, menu markup, script, CSS (criteria 1 to 6, 9)

This is the largest task. It replaces the top bar. Tests first, then markup, script and CSS.

**Files:**
- Create: `src/Portfolio.Web/Pages/Admin/AdminMenuViewComponent.cs`, `src/Portfolio.Web/Pages/Shared/Components/AdminMenu/Default.cshtml`, `src/Portfolio.Web/Pages/Shared/Components/AdminMenu/_MenuLink.cshtml`, `src/Portfolio.Web/Pages/Admin/_AdminIcon.cshtml`, `src/Portfolio.Web/wwwroot/js/admin-shell.js`
- Modify: `src/Portfolio.Web/Pages/Admin/_AdminLayout.cshtml`, `src/Portfolio.Web/wwwroot/css/admin.css`, `CONTRIBUTING.md`
- Test: `tests/Portfolio.Web.Tests/Admin/AdminShellTests.cs`; update `ContentAccessibilityTests.cs:157-172`, `TransferTemplateTests.cs:57-75`, `TransferSecurityTests.cs:195`

**Interfaces:**
- Consumes: `AdminChrome.MenuAsync`, `AdminMenu.IsCollapsed`, `AdminMenu.IsRailPage`, `AdminMenu.Breadcrumb` (Task 5 adds the breadcrumb markup; this task wires the shell only), `AdminAuth.AllowsScript`.
- Produces (markup hooks the tests and the script rely on):
  - Shell: `<div class="adm-shell" data-adm-shell data-menu="expanded|collapsed" data-rail-page="true|false">`; signed-out pages add `adm-shell-bare` and render no menu.
  - Menu: `<nav class="adm-menu" id="adm-menu" aria-label="Admin menu" data-adm-menu>`; group headings `<p class="adm-menu-heading">`; links `a.adm-menu-link` with `data-tip`; sub-menus `<details class="adm-menu-sub">`; rail-only parent links `a.adm-menu-rail`; hidden tag `<span class="adm-menu-tag">hidden</span>`, dim class `adm-menu-dim`; count `<span class="adm-menu-count">N</span>`.
  - Toggle `<button data-adm-toggle aria-expanded aria-controls="adm-menu">`; overlay opener `<button data-adm-open aria-expanded="false" aria-controls="adm-menu">` in `.adm-topbar`.
  - Script tag `<script src="/js/admin-shell.js?v=..." defer>`, emitted only when signed in and `AdminAuth.AllowsScript(path)`.

- [ ] **Step 1: Write the failing integration tests**

Create `tests/Portfolio.Web.Tests/Admin/AdminShellTests.cs`:

```csharp
using System.Net;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Portfolio.Web.Services;
using Portfolio.Web.Tests.Support;
using Xunit;

namespace Portfolio.Web.Tests.Admin;

/// <summary>The left menu on every signed-in admin page: what it holds, its wording, its states, and that it works without script.</summary>
public class AdminShellTests
{
    private static readonly string[] Pages =
    [
        "/admin", ProfileFlow.Identity, ProfileFlow.Hero, ProfileFlow.Metrics, ProfileFlow.About, "/admin/photo",
        "/admin/content", ContentFlow.Experience, ContentFlow.Skills, ContentFlow.Projects, ContentFlow.Certifications, ContentFlow.Talks, ContentFlow.Testimonials, ContentFlow.Blog,
        SectionsFlow.Path, "/admin/template", TransferFlow.Page, "/admin/welcome",
    ];

    /// <summary>The menu's own markup: everything from its opening tag to the closing one.</summary>
    internal static string MenuOf(string html)
    {
        var match = Regex.Match(html, @"<nav class=""adm-menu""[^>]*>.*?</nav>", RegexOptions.Singleline);
        Assert.True(match.Success, "the page has no admin menu");
        return match.Value;
    }

    private static string[] Hrefs(string menu) => Regex.Matches(menu, @"href=""([^""]+)""").Select(m => m.Groups[1].Value).ToArray();

    private static string AnchorTo(string menu, string href)
    {
        var match = Regex.Match(menu, $@"<a [^>]*href=""{Regex.Escape(href)}""[^>]*>.*?</a>", RegexOptions.Singleline);
        Assert.True(match.Success, $"no menu link to {href}");
        return match.Value;
    }

    [Fact]
    public async Task EverySignedInAdminPage_ShowsTheMenu_WithTheGroupsAndEveryTopLevelPage()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);

        foreach (var path in Pages)
        {
            var response = await owner.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var menu = MenuOf(await response.Content.ReadAsStringAsync());

            foreach (var heading in new[] { "Your site", "Appearance", "Tools" })
                Assert.Contains($"<p class=\"adm-menu-heading\">{heading}</p>", menu);
            var hrefs = Hrefs(menu);
            foreach (var target in new[]
                     {
                         "/admin", "/admin/profile", "/admin/profile/hero", "/admin/profile/metrics", "/admin/profile/about", "/admin/photo",
                         "/admin/content", "/admin/content/experience", "/admin/content/skills", "/admin/content/projects", "/admin/content/certifications",
                         "/admin/content/talks", "/admin/content/testimonials", "/admin/content/blog",
                         "/admin/template", "/admin/sections", "/admin/welcome", "/admin/transfer", "/",
                     })
                Assert.True(hrefs.Contains(target), $"{path}: the menu has no link to {target}");
            Assert.Contains("Sign out", menu);
            Assert.Contains("<form method=\"post\" action=\"/admin/logout\"", menu);
        }
    }

    [Fact]
    public async Task TheMenu_UsesTheTemplatesWords()
    {
        var dir = TemplateDirs.WithExtra("quirky", node =>
        {
            node["name"] = "Quirky";
            foreach (var section in node["sections"]!.AsArray())
            {
                section!["label"] = "Zone " + section["key"]!.GetValue<string>();
                section.AsObject().Remove("shortLabel");
            }
            node["editor"] = JsonNode.Parse("""
            {
              "hero": { "fields": { "heading": { "label": "Zhero page" } } },
              "sections": { "fields": { "heading": { "label": "Zsec map" } } },
              "transfer": { "fields": { "heading": { "label": "Zport" } } }
            }
            """);
        });
        using var app = AdminApp.WithTemplates(dir);
        var owner = await ContentFlow.BlankSiteAsync(app, "quirky");

        var menu = MenuOf(await owner.GetStringAsync("/admin"));

        Assert.Contains("Zone experience", AnchorTo(menu, "/admin/content/experience"));
        Assert.Contains("Zone blog", AnchorTo(menu, "/admin/content/blog"));
        Assert.Contains("Zhero page", AnchorTo(menu, "/admin/profile/hero"));
        Assert.Contains("Zsec map", AnchorTo(menu, "/admin/sections"));
        Assert.Contains("Zport", AnchorTo(menu, "/admin/transfer"));
        Assert.DoesNotContain("Export and import", menu);
    }

    [Fact]
    public async Task ASectionTheTemplateSwitchesOff_IsDimmed_WithTheTextHidden()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        Assert.Equal(HttpStatusCode.Redirect, (await SectionsFlow.SaveAsync(owner, SectionsFlow.Set("talks", enabled: false))).StatusCode);

        var menu = MenuOf(await owner.GetStringAsync("/admin/content"));

        var talks = AnchorTo(menu, "/admin/content/talks");
        Assert.Contains("adm-menu-dim", talks);
        Assert.Contains("<span class=\"adm-menu-tag\">hidden</span>", talks);
        var experience = AnchorTo(menu, "/admin/content/experience");
        Assert.DoesNotContain("adm-menu-dim", experience);
        Assert.DoesNotContain("adm-menu-tag", experience);
    }

    [Fact]
    public async Task EachContentItem_ShowsItsEntryCount()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        await ContentFlow.SeedRowsAsync(app, pairs: true);

        var menu = MenuOf(await owner.GetStringAsync("/admin"));

        Assert.Contains("<span class=\"adm-menu-count\">2</span>", AnchorTo(menu, "/admin/content/experience"));
        Assert.Contains("<span class=\"adm-menu-count\">2</span>", AnchorTo(menu, "/admin/content/blog"));
    }

    [Fact]
    public async Task TheCurrentPage_IsMarked_AndItsSubMenuIsOpen()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);

        var menu = MenuOf(await owner.GetStringAsync(ContentFlow.Skills));

        Assert.Single(Regex.Matches(menu, @"aria-current=""page"""));
        Assert.Contains("aria-current=\"page\"", AnchorTo(menu, "/admin/content/skills"));
        var open = Regex.Matches(menu, @"<details class=""adm-menu-sub""( open=""open"")?>").Select(m => m.Groups[1].Success).ToArray();
        Assert.Equal([false, true], open); // Profile closed, Content open
    }

    [Fact]
    public async Task TheMenu_IsExpandedByDefault_AndCollapsedByTheCookie_OnlyWhenItIsTheLiteral()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);

        async Task<string> Get(string path, string? cookie)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, path);
            if (cookie is not null) request.Headers.Add("Cookie", cookie);
            return await (await owner.SendAsync(request)).Content.ReadAsStringAsync();
        }

        var expanded = await Get("/admin", null);
        Assert.Contains("data-menu=\"expanded\"", expanded);
        Assert.Matches(@"<button[^>]*data-adm-toggle[^>]*aria-expanded=""true""|<button[^>]*aria-expanded=""true""[^>]*data-adm-toggle", expanded);

        var collapsed = await Get("/admin", "adm-menu=collapsed");
        Assert.Contains("data-menu=\"collapsed\"", collapsed);
        Assert.Matches(@"<button[^>]*data-adm-toggle[^>]*aria-expanded=""false""|<button[^>]*aria-expanded=""false""[^>]*data-adm-toggle", collapsed);

        foreach (var junk in new[] { "adm-menu=Collapsed", "adm-menu=collapsed2", "adm-menu=expanded", "adm-menu=" })
            Assert.Contains("data-menu=\"expanded\"", await Get("/admin", junk));
    }

    [Fact]
    public async Task WizardPages_DrawTheRail_WhateverTheCookie()
    {
        using var app = new AdminApp();
        var owner = await WizardFlow.FirstRunClientAsync(app);
        await WizardFlow.PickTemplateAsync(owner, "software");

        foreach (var path in new[] { WizardFlow.Template, WizardFlow.Basics })
        {
            var html = await owner.GetStringAsync(path);
            Assert.Contains("data-menu=\"collapsed\"", html);
            Assert.Contains("data-rail-page=\"true\"", html);
        }
        Assert.Contains("data-rail-page=\"false\"", await owner.GetStringAsync("/admin"));
    }

    [Fact]
    public async Task WithoutScript_EveryLinkIsPlainMarkup_AndNothingIsInlineOrHidden()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);

        foreach (var path in Pages)
        {
            var html = await owner.GetStringAsync(path);
            var menu = MenuOf(html);
            Assert.DoesNotContain("hidden=\"", menu);        // nothing a missing script would have to reveal
            Assert.DoesNotMatch(@"\sstyle\s*=", html);        // the admin CSP refuses inline style
            Assert.DoesNotMatch(@"\son[a-z]+\s*=", html);
            Assert.True(Hrefs(menu).Length >= 18, $"{path}: the menu lost links");
        }
    }

    [Fact]
    public async Task TheNarrowScreenOverlay_HasItsButton_AndTheHooksTheScriptNeeds()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);

        var html = await owner.GetStringAsync("/admin");

        Assert.Matches(@"<button[^>]*data-adm-open[^>]*>", html);
        Assert.Matches(@"<button[^>]*data-adm-open[^>]*aria-controls=""adm-menu""[^>]*aria-expanded=""false""|<button[^>]*data-adm-open[^>]*aria-expanded=""false""[^>]*aria-controls=""adm-menu""", html);
        Assert.Contains("id=\"adm-menu\"", html);
        Assert.Contains("data-adm-shell", html);
    }

    [Fact]
    public async Task TheShellScript_IsLoadedOnSignedInPages_NotOnTheLoginPage_AndIsServed()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        var anonymous = app.NewClient();

        var page = await owner.GetStringAsync("/admin");
        var login = await anonymous.GetStringAsync("/admin/login");

        var src = Regex.Match(page, "<script src=\"(/js/admin-shell[^\"]*\\.js)\"");
        Assert.True(src.Success, "the shell script tag is missing");
        Assert.DoesNotContain("<script", login);
        Assert.DoesNotContain("adm-menu", login);
        var script = await anonymous.GetAsync(src.Groups[1].Value);
        Assert.Equal(HttpStatusCode.OK, script.StatusCode);
        var body = await script.Content.ReadAsStringAsync();
        Assert.Contains("adm-menu", body);
        Assert.Contains("Escape", body);
        Assert.DoesNotContain("innerHTML", body);
    }

    [Fact]
    public async Task AFailedCountNeverBreaksAPage_TheMenuJustLosesItsNumbers()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        using (var db = app.CreateDbContext())
            await db.Database.ExecuteSqlRawAsync("DROP TABLE \"Experiences\"");

        foreach (var path in new[] { "/admin", ProfileFlow.Identity, ProfileFlow.Hero, "/admin/photo", SectionsFlow.Path, "/admin/template", TransferFlow.Page, "/admin/welcome", ContentFlow.Skills })
        {
            var response = await owner.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var menu = MenuOf(await response.Content.ReadAsStringAsync());
            Assert.DoesNotContain("adm-menu-count", menu);
            Assert.Contains("href=\"/admin/content/experience\"", menu);
        }
    }
}
```

Update the three existing tests that assert the removed top bar and tile grid:

`ContentAccessibilityTests.cs` replace lines 157-172 with:

```csharp
    [Fact]
    public async Task TheAdminMenu_PointsAtContent_AndMarksTheCurrentPage()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);

        var hub = AdminShellTests.MenuOf(await owner.GetStringAsync("/admin/content"));
        var list = AdminShellTests.MenuOf(await owner.GetStringAsync(ContentFlow.Skills));
        var dashboard = AdminShellTests.MenuOf(await owner.GetStringAsync("/admin"));

        Assert.Matches(@"<a [^>]*href=""/admin/content""[^>]*aria-current=""page""|<a [^>]*aria-current=""page""[^>]*href=""/admin/content""", hub);
        Assert.Matches(@"<a [^>]*href=""/admin/content/skills""[^>]*aria-current=""page""|<a [^>]*aria-current=""page""[^>]*href=""/admin/content/skills""", list);
        Assert.Contains("href=\"/admin/content\"", dashboard);
        Assert.DoesNotContain("aria-current=\"page\"", dashboard.Replace("href=\"/admin\"", "")); // only the dashboard link is current there
    }
```

`TransferTemplateTests.cs` replace lines 67-68 and 73:

```csharp
        Assert.Contains("href=\"/admin/transfer\"", dashboard);
        Assert.Contains("Zport", AdminShellTests.MenuOf(dashboard)); // the menu names the page in the manifest's words
```
and line 73 becomes:
```csharp
        AssertOnlyTheManifestsWords("the dashboard", dashboard);
```
(add `using Portfolio.Web.Tests.Admin;` to the file's usings.)

`TransferSecurityTests.cs` line 195 replace `Assert.DoesNotMatch(@"<script\b", html);` with `Assert.DoesNotMatch(@"<script(?![^>]*\ssrc=)", html); // only a first-party file, never inline`.

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/Portfolio.Web.Tests --filter "FullyQualifiedName~AdminShellTests|FullyQualifiedName~TheAdminMenu_PointsAtContent|FullyQualifiedName~ThePageAndTheDashboardTile"`
Expected: FAIL ("the page has no admin menu", missing script tag).

- [ ] **Step 3: Write the icon partial and the view component**

Create `src/Portfolio.Web/Pages/Admin/_AdminIcon.cshtml`:

```cshtml
@model string
@* Inline SVG only: the admin CSP allows no inline style. Drawn on a 24-unit grid; stroke and size come from admin.css. An empty key draws nothing. *@
@if (!string.IsNullOrEmpty(Model))
{
    <svg viewBox="0 0 24 24" aria-hidden="true" focusable="false">
        @switch (Model)
        {
            case "dashboard":
                <rect x="3" y="3" width="7" height="9" rx="1" /><rect x="14" y="3" width="7" height="5" rx="1" /><rect x="14" y="12" width="7" height="9" rx="1" /><rect x="3" y="16" width="7" height="5" rx="1" />
                break;
            case "profile":
                <circle cx="12" cy="8" r="4" /><path d="M4 21c0-4.4 3.6-7 8-7s8 2.6 8 7" />
                break;
            case "content":
                <path d="M6 3h8l4 4v14H6z" /><path d="M14 3v4h4M9 12h6M9 16h6" />
                break;
            case "template":
                <rect x="3" y="4" width="18" height="16" rx="2" /><path d="M3 9h18M9 9v11" />
                break;
            case "sections":
                <path d="M4 6h16M4 12h16M4 18h10" />
                break;
            case "wizard":
                <path d="M12 3l1.8 4.7 4.7 1.8-4.7 1.8L12 16l-1.8-4.7L5.5 9.5l4.7-1.8z" /><path d="M19 15l.8 2.2 2.2.8-2.2.8L19 21l-.8-2.2-2.2-.8 2.2-.8z" />
                break;
            case "transfer":
                <path d="M7 4v14M3 14l4 4 4-4" /><path d="M17 20V6M13 10l4-4 4 4" />
                break;
            case "external":
                <path d="M14 4h6v6M20 4l-9 9" /><path d="M18 14v5a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1V7a1 1 0 0 1 1-1h5" />
                break;
            case "signout":
                <path d="M9 4H5a1 1 0 0 0-1 1v14a1 1 0 0 0 1 1h4" /><path d="M16 8l4 4-4 4M20 12H9" />
                break;
            case "collapse":
                <path d="M11 6l-6 6 6 6M19 6l-6 6 6 6" />
                break;
            case "menu":
                <path d="M4 6h16M4 12h16M4 18h16" />
                break;
            case "chevron":
                <path d="M9 6l6 6-6 6" />
                break;
            default:
                <circle cx="12" cy="12" r="3" />
                break;
        }
    </svg>
}
```

Create `src/Portfolio.Web/Pages/Admin/AdminMenuViewComponent.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Portfolio.Web.Services;

namespace Portfolio.Web.Pages.Admin;

/// <summary>What the menu view needs: the model and whether it is drawn as the icon rail.</summary>
public sealed record AdminMenuView(AdminMenuModel Menu, bool Collapsed);

/// <summary>Draws the admin menu. All decisions (what is in it, which item is current, whether it is collapsed) are made before this is called.</summary>
public class AdminMenuViewComponent : ViewComponent
{
    public IViewComponentResult Invoke(AdminMenuModel menu, bool collapsed) => View(new AdminMenuView(menu, collapsed));
}
```

Create `src/Portfolio.Web/Pages/Shared/Components/AdminMenu/_MenuLink.cshtml`:

```cshtml
@using Portfolio.Web.Services
@model AdminMenuItem
<a class="adm-menu-link@(Model.Hidden ? " adm-menu-dim" : "")" href="@Model.Url" aria-current="@(Model.IsCurrent ? "page" : null)" data-tip="@Model.Label"><partial name="/Pages/Admin/_AdminIcon.cshtml" model="Model.Icon" /><span class="adm-menu-label">@Model.Label</span>@if (Model.Hidden) {<span class="adm-menu-tag">hidden</span>}@if (Model.Count is { } count) {<span class="adm-menu-count">@count</span>}</a>
```

Create `src/Portfolio.Web/Pages/Shared/Components/AdminMenu/Default.cshtml`:

```cshtml
@using Portfolio.Web.Pages.Admin
@using Portfolio.Web.Services
@model AdminMenuView
<nav class="adm-menu" id="adm-menu" aria-label="Admin menu" data-adm-menu>
    <div class="adm-menu-head">
        <a class="adm-brand adm-menu-link" href="/admin" data-tip="Admin"><partial name="/Pages/Admin/_AdminIcon.cshtml" model="@("dashboard")" /><span class="adm-menu-label">Admin</span></a>
    </div>
    @foreach (var group in Model.Menu.Groups)
    {
        <div class="adm-menu-group">
            @if (group.Label is { } heading)
            {
                <p class="adm-menu-heading">@heading</p>
            }
            <ul class="adm-menu-list">
                @foreach (var item in group.Items)
                {
                    @if (item.Children.Count == 0)
                    {
                        <li><partial name="/Pages/Shared/Components/AdminMenu/_MenuLink.cshtml" model="item" /></li>
                    }
                    else
                    {
                        @* The rail has no room for a sub-menu, so a parent is a plain link to its own page there; the details element is for the wide menu. *@
                        <li class="adm-menu-parent">
                            <a class="adm-menu-link adm-menu-rail" href="@item.Url" aria-current="@(item.ContainsCurrent ? "true" : null)" data-tip="@item.Label"><partial name="/Pages/Admin/_AdminIcon.cshtml" model="item.Icon" /><span class="adm-menu-label">@item.Label</span></a>
                            <details class="adm-menu-sub" open="@item.ContainsCurrent">
                                <summary class="adm-menu-link"><partial name="/Pages/Admin/_AdminIcon.cshtml" model="item.Icon" /><span class="adm-menu-label">@item.Label</span><span class="adm-chevron"><partial name="/Pages/Admin/_AdminIcon.cshtml" model="@("chevron")" /></span></summary>
                                <ul class="adm-menu-list adm-menu-sublist">
                                    @foreach (var child in item.Children)
                                    {
                                        <li><partial name="/Pages/Shared/Components/AdminMenu/_MenuLink.cshtml" model="child" /></li>
                                    }
                                </ul>
                            </details>
                        </li>
                    }
                }
            </ul>
        </div>
    }
    <div class="adm-menu-foot">
        <ul class="adm-menu-list">
            <li><a class="adm-menu-link" href="/" target="_blank" rel="noopener" data-tip="View site"><partial name="/Pages/Admin/_AdminIcon.cshtml" model="@("external")" /><span class="adm-menu-label">View site<span class="adm-sr"> (opens in a new tab)</span></span></a></li>
            <li>
                <form method="post" asp-page="/Admin/Logout" class="adm-signout">
                    <button type="submit" class="adm-menu-link adm-menu-button" data-tip="Sign out"><partial name="/Pages/Admin/_AdminIcon.cshtml" model="@("signout")" /><span class="adm-menu-label">Sign out</span></button>
                </form>
            </li>
            <li>
                <button type="button" class="adm-menu-link adm-menu-button adm-menu-toggle" data-adm-toggle aria-controls="adm-menu" aria-expanded="@(Model.Collapsed ? "false" : "true")" data-tip="Collapse menu"><partial name="/Pages/Admin/_AdminIcon.cshtml" model="@("collapse")" /><span class="adm-menu-label">Collapse menu</span></button>
            </li>
        </ul>
    </div>
</nav>
```

Notes: `<form method="post" asp-page="/Admin/Logout">` renders `action="/admin/logout"` plus the anti-forgery token; the test asserts `<form method="post" action="/admin/logout"`. If the tag helper emits attributes in a different order, adjust that one assertion to match the rendered form. The toggle button is `display: none` on narrow screens (CSS below).

- [ ] **Step 4: Replace the layout**

Replace the whole of `src/Portfolio.Web/Pages/Admin/_AdminLayout.cshtml` with:

```cshtml
@using Portfolio.Web.Security
@using Portfolio.Web.Services
@inject AdminChrome Chrome
@{
    var title = ViewData["Title"] as string ?? "Admin";
    var signedIn = User.Identity?.IsAuthenticated == true;
    var flash = TempData[AdminPageModel.FlashKey] as string;
    var flashError = TempData[AdminPageModel.FlashErrorKey] as string;
    // After a redirect the message takes focus so a screen reader reads it; only pages that ask for it (the content editors) do this.
    var focusFlash = ViewData["FocusFlash"] is true;
    var path = Context.Request.Path.Value ?? "/admin";
    var menu = signedIn ? await Chrome.MenuAsync(path, Context.RequestAborted) : null;
    var railPage = AdminMenu.IsRailPage(path);
    // The cookie is read here so the first paint is already in the right state; the script only ever writes it.
    var collapsed = signedIn && AdminMenu.IsCollapsed(path, Context.Request.Cookies[AdminMenu.CollapsedCookie]);
}
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <meta name="robots" content="noindex, nofollow, noarchive" />
    <meta name="color-scheme" content="light dark" />
    <title>@title · Admin</title>
    <link rel="icon" href="~/favicon.svg" type="image/svg+xml" />
    <link rel="stylesheet" href="~/css/admin.css" asp-append-version="true" />
</head>
<body class="adm">
    <a href="#main-content" class="adm-skip">Skip to content</a>
    <div class="adm-shell@(signedIn ? "" : " adm-shell-bare")" data-adm-shell data-menu="@(collapsed ? "collapsed" : "expanded")" data-rail-page="@(railPage ? "true" : "false")">
        @if (menu is not null)
        {
            <div class="adm-topbar">
                <button type="button" class="adm-menu-link adm-menu-button adm-topbar-button" data-adm-open aria-controls="adm-menu" aria-expanded="false"><partial name="/Pages/Admin/_AdminIcon.cshtml" model="@("menu")" /><span class="adm-menu-label">Menu</span></button>
                <a class="adm-brand" href="/admin">Admin</a>
            </div>
            @await Component.InvokeAsync("AdminMenu", new { menu, collapsed })
        }
        <div class="adm-content">
            @if (menu is null)
            {
                <header class="adm-plainbar"><a class="adm-brand" href="/admin">Admin</a></header>
            }
            <main id="main-content" class="adm-main">
                @if (!string.IsNullOrEmpty(flash))
                {
                    <div class="adm-flash adm-flash-ok" role="status" aria-live="polite" tabindex="-1" autofocus="@(focusFlash && string.IsNullOrEmpty(flashError) ? "autofocus" : null)">@flash</div>
                }
                @if (!string.IsNullOrEmpty(flashError))
                {
                    <div class="adm-flash adm-flash-error" role="alert" tabindex="-1" autofocus="@(focusFlash ? "autofocus" : null)">@flashError</div>
                }
                @RenderBody()
            </main>
        </div>
    </div>
    @if (signedIn && AdminAuth.AllowsScript(Context.Request.Path))
    {
        <script src="~/js/admin-shell.js" asp-append-version="true" defer></script>
    }
    @await RenderSectionAsync("Scripts", required: false)
</body>
</html>
```

(The page header and breadcrumb are added in Task 5. Flash messages stay first inside `main` for now; Task 5 places the header above them.)

- [ ] **Step 5: Write the script**

Create `src/Portfolio.Web/wwwroot/js/admin-shell.js`:

```js
// The admin menu's convenience layer: collapse to an icon rail and back (remembered in a cookie the server reads), and open as an overlay on narrow
// screens. Admin pages run no inline script (see the admin CSP); this and admin-unsaved.js are the only scripts they may load. Without it the menu
// is simply open on wide screens and sits above the page on narrow ones, and every link and form still works.
(function () {
  var shell = document.querySelector('[data-adm-shell]');
  var toggle = document.querySelector('[data-adm-toggle]');
  if (!shell || !toggle) return;
  shell.classList.add('adm-js');

  var COOKIE = 'adm-menu';
  var railPage = shell.getAttribute('data-rail-page') === 'true';

  // The server only ever compares the cookie with the word "collapsed", so expanding just removes it.
  function remember(collapsed) {
    if (railPage) return; // a wizard page's rail belongs to that page and is never remembered
    var secure = location.protocol === 'https:' ? '; Secure' : '';
    document.cookie = collapsed
      ? COOKIE + '=collapsed; path=/admin; max-age=31536000; SameSite=Lax' + secure
      : COOKIE + '=; path=/admin; max-age=0; SameSite=Lax' + secure;
  }

  toggle.addEventListener('click', function () {
    var collapsed = shell.getAttribute('data-menu') !== 'collapsed';
    shell.setAttribute('data-menu', collapsed ? 'collapsed' : 'expanded');
    toggle.setAttribute('aria-expanded', collapsed ? 'false' : 'true');
    remember(collapsed);
  });

  var opener = document.querySelector('[data-adm-open]');
  var menu = document.querySelector('[data-adm-menu]');
  if (!opener || !menu) return;

  function isOpen() { return shell.getAttribute('data-menu-open') === 'true'; }
  function setOpen(open) {
    shell.setAttribute('data-menu-open', open ? 'true' : 'false');
    opener.setAttribute('aria-expanded', open ? 'true' : 'false');
  }

  opener.addEventListener('click', function () {
    var open = !isOpen();
    setOpen(open);
    if (open) {
      var first = menu.querySelector('a, button');
      if (first) first.focus();
    }
  });
  document.addEventListener('keydown', function (event) {
    if (event.key === 'Escape' && isOpen()) {
      setOpen(false);
      opener.focus();
    }
  });
  menu.addEventListener('click', function (event) {
    if (event.target.closest && event.target.closest('a')) setOpen(false);
  });
})();
```

- [ ] **Step 6: Replace the top-bar CSS with the shell and menu CSS**

In `src/Portfolio.Web/wwwroot/css/admin.css`:

1. Delete these now-unused rules: `.adm-header`, `.adm-bar`, `.adm-nav`, `.adm-nav a`, `.adm-nav a[aria-current="page"]` (the five lines between `.adm-skip:focus` and `.adm-signout`). Keep `.adm-brand`, `.adm-signout`.
2. Replace the `.adm-main` rule with `.adm-main { padding: 1.5rem 1.5rem 3rem; }`.
3. Append this block at the end of the file:

```css
/* Shell: left menu beside the page. The menu is a rail when data-menu="collapsed" (wide screens only). */
.adm-shell { display: grid; grid-template-columns: 16rem minmax(0, 1fr); min-height: 100vh; }
.adm-shell[data-menu="collapsed"] { grid-template-columns: 4.25rem minmax(0, 1fr); }
.adm-shell-bare { display: block; }
.adm-shell-bare .adm-main { max-width: 60rem; margin: 0 auto; }
.adm-content { min-width: 0; }
.adm-plainbar { padding: .75rem 1.5rem; border-bottom: 1px solid var(--adm-border); background: var(--adm-surface); }
.adm-topbar { display: none; }
.adm-main form { max-width: 48rem; }

.adm-menu { position: sticky; top: 0; align-self: start; height: 100vh; overflow-y: auto; display: flex; flex-direction: column; gap: .25rem; padding: .75rem; background: var(--adm-surface); border-right: 1px solid var(--adm-border); }
.adm-menu-head { padding: .25rem 0 .5rem; }
.adm-menu-group { margin-bottom: .25rem; }
.adm-menu-heading { margin: .75rem .6rem .25rem; font-size: .72rem; font-weight: 700; letter-spacing: .06em; text-transform: uppercase; color: var(--adm-muted); }
.adm-menu-list { list-style: none; margin: 0; padding: 0; display: grid; gap: 2px; }
.adm-menu-foot { margin-top: auto; padding-top: .75rem; border-top: 1px solid var(--adm-border); }
.adm .adm-menu-link { position: relative; display: flex; align-items: center; gap: .65rem; width: 100%; padding: .5rem .6rem; border: 0; border-radius: .35rem; background: none; color: var(--adm-text); font: inherit; text-align: left; text-decoration: none; cursor: pointer; }
.adm .adm-menu-link:hover { background: var(--adm-bg); }
.adm .adm-menu-link[aria-current="page"] { background: var(--adm-bg); font-weight: 600; box-shadow: inset 3px 0 0 var(--adm-accent); }
.adm .adm-menu-link[aria-current="true"] { font-weight: 600; }
.adm-menu-link svg { width: 1.25rem; height: 1.25rem; flex: none; fill: none; stroke: currentColor; stroke-width: 1.75; stroke-linecap: round; stroke-linejoin: round; }
.adm-menu-dim .adm-menu-label { opacity: .6; }
.adm-menu-tag { padding: 0 .4rem; border: 1px solid var(--adm-border); border-radius: 999px; font-size: .7rem; color: var(--adm-muted); }
.adm-menu-count { margin-left: auto; font-size: .8rem; color: var(--adm-muted); font-variant-numeric: tabular-nums; }
.adm-menu-sub > summary { list-style: none; }
.adm-menu-sub > summary::-webkit-details-marker { display: none; }
.adm-menu-sub > summary .adm-chevron { margin-left: auto; display: inline-flex; transition: transform .15s; }
.adm-menu-sub[open] > summary .adm-chevron { transform: rotate(90deg); }
.adm-menu-sublist { margin: 2px 0 .35rem 1.1rem; padding-left: .5rem; border-left: 1px solid var(--adm-border); }
.adm-menu-sublist .adm-menu-link { padding: .35rem .6rem; font-size: .92rem; }
.adm-menu-rail { display: none; }
.adm-signout { margin: 0; }
.adm-menu-toggle .adm-menu-label { white-space: nowrap; }

/* Wide screens: the rail. Labels stay in the page for screen readers and show as a tooltip on hover and focus. */
@media (min-width: 40.0625rem) {
  .adm-shell[data-menu="collapsed"] .adm-menu { overflow: visible; padding-inline: .5rem; }
  .adm-shell[data-menu="collapsed"] .adm-menu-heading,
  .adm-shell[data-menu="collapsed"] .adm-menu-tag,
  .adm-shell[data-menu="collapsed"] .adm-menu-count,
  .adm-shell[data-menu="collapsed"] .adm-menu-sub { display: none; }
  .adm-shell[data-menu="collapsed"] .adm-menu-rail { display: flex; }
  .adm-shell[data-menu="collapsed"] .adm-menu-link { justify-content: center; }
  .adm-shell[data-menu="collapsed"] .adm-menu-label { position: absolute; width: 1px; height: 1px; margin: -1px; overflow: hidden; clip: rect(0 0 0 0); white-space: nowrap; }
  .adm-shell[data-menu="collapsed"] .adm-menu-toggle svg { transform: rotate(180deg); }
  .adm-shell[data-menu="collapsed"] .adm-menu-link:hover::after,
  .adm-shell[data-menu="collapsed"] .adm-menu-link:focus-visible::after { content: attr(data-tip); position: absolute; left: calc(100% + .5rem); top: 50%; transform: translateY(-50%); z-index: 30; padding: .25rem .55rem; border-radius: .35rem; background: var(--adm-text); color: var(--adm-bg); font-size: .8rem; white-space: nowrap; pointer-events: none; }
}

/* Narrow screens: without script the menu sits above the page; with script it is an overlay opened from a slim top bar. */
@media (max-width: 40rem) {
  .adm-shell, .adm-shell[data-menu="collapsed"] { display: block; }
  .adm-menu { position: static; height: auto; border-right: 0; border-bottom: 1px solid var(--adm-border); }
  .adm-menu-toggle { display: none; }
  .adm-main { padding: 1rem 1rem 3rem; }
  .adm-shell.adm-js .adm-topbar { position: sticky; top: 0; z-index: 25; display: flex; align-items: center; gap: .75rem; height: 3.25rem; padding: 0 .75rem; background: var(--adm-surface); border-bottom: 1px solid var(--adm-border); }
  .adm-topbar .adm-menu-link { width: auto; }
  .adm-shell.adm-js .adm-menu { display: none; position: fixed; top: 3.25rem; right: 0; bottom: 0; left: 0; z-index: 20; height: auto; overflow-y: auto; border: 0; }
  .adm-shell.adm-js[data-menu-open="true"] .adm-menu { display: flex; }
}
```

- [ ] **Step 7: Update CONTRIBUTING.md**

Replace the sentence at the end of the content-editors bullet (line 62-63: "Admin pages run no inline script: the one exception is the first-party `wwwroot/js/admin-unsaved.js` on the edit forms (the admin CSP allows `script-src 'self'` there only, see `AdminAuth.AllowsScript`).") with:

```
Admin pages run no inline script. Signed-in admin pages load the first-party `wwwroot/js/admin-shell.js` (the menu), and the edit forms also `wwwroot/js/admin-unsaved.js`; the admin CSP allows `script-src 'self'` on every admin path except the login page (see `AdminAuth.AllowsScript`).
```

- [ ] **Step 8: Run the shell tests and the three updated tests**

Run: `dotnet test tests/Portfolio.Web.Tests --filter "FullyQualifiedName~AdminShellTests|FullyQualifiedName~AdminCspTests|FullyQualifiedName~TheAdminMenu_PointsAtContent|FullyQualifiedName~ThePageAndTheDashboardTile|FullyQualifiedName~NoScriptAndNoInlineHandler"`
Expected: PASS. If the cookie test (`TheMenu_IsExpandedByDefault...`) fails because a manually added `Cookie` header is dropped by the test client, send the cookie through `new HttpRequestMessage` with `TryAddWithoutValidation("Cookie", ...)` or build the client with `HandleCookies = false` after copying the session cookie; do not weaken the assertion.

- [ ] **Step 9: Run the whole suite to find other tests that assumed the old chrome**

Run: `dotnet test Portfolio.slnx`
Expected: failures only in tests that read the removed tile grid (Task 6 owns the dashboard) or old nav markup. Fix any test that asserts the old top bar here (update it to the menu); leave dashboard-tile assertions for Task 6 and list them in the commit message if any remain red. Do not proceed to Task 5 with failures that are not dashboard-tile related.

- [ ] **Step 10: Commit**

```bash
git add src/Portfolio.Web/Pages/Admin/_AdminLayout.cshtml src/Portfolio.Web/Pages/Admin/AdminMenuViewComponent.cs src/Portfolio.Web/Pages/Admin/_AdminIcon.cshtml src/Portfolio.Web/Pages/Shared/Components/AdminMenu src/Portfolio.Web/wwwroot/js/admin-shell.js src/Portfolio.Web/wwwroot/css/admin.css CONTRIBUTING.md tests/Portfolio.Web.Tests/Admin/AdminShellTests.cs tests/Portfolio.Web.Tests/Admin/ContentAccessibilityTests.cs tests/Portfolio.Web.Tests/Transfer/TransferTemplateTests.cs tests/Portfolio.Web.Tests/Transfer/TransferSecurityTests.cs
git commit -m "feat(admin): collapsible left menu with icon rail, overlay on narrow screens and first-party shell script"
```

---

### Task 5: Page header with breadcrumb (criterion 7)

**Files:**
- Modify: `src/Portfolio.Web/Pages/Admin/_AdminLayout.cshtml`, `src/Portfolio.Web/wwwroot/css/admin.css`
- Test: append to `tests/Portfolio.Web.Tests/Admin/AdminMenuTests.cs` (breadcrumb unit tests) and `tests/Portfolio.Web.Tests/Admin/AdminShellTests.cs` (every page)

**Interfaces:**
- Consumes: `AdminMenu.Breadcrumb(menu, path, title, crumb)` (Task 2), `ViewData["Crumb"]` (optional page value).
- Produces: `<nav class="adm-crumbs" aria-label="Breadcrumb"><ol>…</ol></nav>` above the flash messages in `main`; an optional `@section Actions { }` rendered at the header's right.

- [ ] **Step 1: Write the failing tests**

Append to `AdminMenuTests.cs` (inside the class):

```csharp
    private static string Trail(string path, string? title = null, string? crumb = null) =>
        string.Join(" / ", AdminMenu.Breadcrumb(Menu.Build(Software(), path, null), path, title, crumb).Select(c => c.Url is null ? $"[{c.Label}]" : c.Label));

    [Fact]
    public void TheDashboard_IsJustAdmin()
    {
        Assert.Equal("[Admin]", Trail("/admin", "Dashboard"));
    }

    [Fact]
    public void ATopLevelPage_IsAdminThenThePage()
    {
        Assert.Equal("Admin / [Template]", Trail("/admin/template", "Template"));
        Assert.Equal("Admin / [Setup wizard]", Trail("/admin/welcome", "Template"));
    }

    [Fact]
    public void ASubMenuPage_NamesItsParent()
    {
        Assert.Equal("Admin / Content / [Overview]", Trail("/admin/content", "Content"));
        Assert.Equal("Admin / Profile / [Basics]", Trail("/admin/profile", "Profile"));
        Assert.Equal("Admin / Content / [Skills]", Trail("/admin/content/skills", "Skills"));
    }

    [Fact]
    public void APageBelowAMenuItem_AddsItsOwnLastCrumb_AndLinksBack()
    {
        Assert.Equal("Admin / Content / Projects / [Edit project]", Trail("/admin/content/projects/edit/4", "Edit project"));
        Assert.Equal("Admin / Content / Projects / [Gallery]", Trail("/admin/content/projects/images/4", "Edit project", crumb: "Gallery"));
    }

    [Fact]
    public void APageWhoseTitleIsTheItemsOwnName_IsNotRepeated() =>
        Assert.Equal("Admin / Content / [Skills]", Trail("/admin/content/skills/", "Skills"));

    [Fact]
    public void APageOutsideTheMenu_IsAdminThenItsTitle()
    {
        Assert.Equal("Admin / [Somewhere]", Trail("/admin/somewhere", "Somewhere"));
        Assert.Equal("Admin", Trail("/admin/somewhere", null));
    }
```

Append to `AdminShellTests.cs` (inside the class):

```csharp
    [Fact]
    public async Task EverySignedInPage_HasABreadcrumb_AndExactlyOneH1()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        var seeded = await ContentFlow.SeedRowsAsync(app, pairs: true);
        var paths = Pages.Concat(
        [
            $"{ContentFlow.Experience}/edit", $"{ContentFlow.Experience}/edit/{seeded.ExperienceId}", $"{ContentFlow.Experience}/delete/{seeded.ExperienceId}",
            $"{ContentFlow.Skills}/edit", $"{ContentFlow.Skills}/delete/{seeded.SkillId}",
            $"{ContentFlow.Projects}/edit/{seeded.ProjectId}", $"{ContentFlow.Projects}/images/{seeded.ProjectId}", $"{ContentFlow.Projects}/delete/{seeded.ProjectId}",
            $"{ContentFlow.Certifications}/edit", $"{ContentFlow.Talks}/edit", $"{ContentFlow.Testimonials}/edit",
            $"{ContentFlow.Blog}/edit", $"{ContentFlow.Blog}/edit/{seeded.BlogId}", $"{ContentFlow.Blog}/delete/{seeded.BlogId}",
        ]).ToArray();

        foreach (var path in paths)
        {
            var response = await owner.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var html = await response.Content.ReadAsStringAsync();
            Assert.True(Regex.Matches(html, @"<h1[\s>]").Count == 1, $"{path} has {Regex.Matches(html, @"<h1[\s>]").Count} h1 elements");
            Assert.Contains("<nav class=\"adm-crumbs\" aria-label=\"Breadcrumb\">", html);
            Assert.Single(Regex.Matches(html, @"<li [^>]*aria-current=""page""|<span aria-current=""page"""));
        }
    }

    [Fact]
    public async Task TheBreadcrumb_FollowsTheMenu_AndThePageBelowItAppearsLast()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);

        var crumbs = Regex.Match(await owner.GetStringAsync($"{ContentFlow.Projects}/edit"), @"<nav class=""adm-crumbs"".*?</nav>", RegexOptions.Singleline).Value;

        Assert.Matches(@"<a href=""/admin"">Admin</a>.*<a href=""/admin/content"">Content</a>.*<a href=""/admin/content/projects"">.*</a>.*aria-current=""page""", crumbs.Replace("\n", " "));
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/Portfolio.Web.Tests --filter "FullyQualifiedName~AdminMenuTests|FullyQualifiedName~EverySignedInPage_HasABreadcrumb|FullyQualifiedName~TheBreadcrumb_FollowsTheMenu"`
Expected: the new breadcrumb unit tests PASS already (the function exists from Task 2, which is intended: they pin its rules); the two integration tests FAIL (no `adm-crumbs`). If any page reports `h1` count other than 1, that page is a real finding: fix that page's heading (one `h1`), not the test.

- [ ] **Step 3: Render the header in the layout**

In `_AdminLayout.cshtml`, in the `@{ }` block add after `var collapsed = ...;`:

```csharp
    var crumbs = menu is null ? [] : AdminMenu.Breadcrumb(menu, path, ViewData["Title"] as string, ViewData["Crumb"] as string);
    var hasActions = IsSectionDefined("Actions");
```

Replace `<main id="main-content" class="adm-main">` opening line's following content so the header comes first:

```cshtml
            <main id="main-content" class="adm-main">
                @if (crumbs.Count > 0 || hasActions)
                {
                    <div class="adm-pagehead">
                        @if (crumbs.Count > 0)
                        {
                            <nav class="adm-crumbs" aria-label="Breadcrumb">
                                <ol>
                                    @foreach (var crumb in crumbs)
                                    {
                                        <li>@if (crumb.Url is { } url) {<a href="@url">@crumb.Label</a>} else {<span aria-current="page">@crumb.Label</span>}</li>
                                    }
                                </ol>
                            </nav>
                        }
                        @if (hasActions)
                        {
                            <div class="adm-pagehead-actions">@await RenderSectionAsync("Actions")</div>
                        }
                    </div>
                }
```
(the flash blocks and `@RenderBody()` follow unchanged.)

In `admin.css` append:

```css
/* Page header: breadcrumb on the left, an optional action slot on the right. The page's own h1 follows it. */
.adm-pagehead { display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: .75rem; margin-bottom: 1rem; }
.adm-crumbs ol { display: flex; flex-wrap: wrap; gap: .25rem; margin: 0; padding: 0; list-style: none; font-size: .875rem; color: var(--adm-muted); }
.adm-crumbs li + li::before { content: "/"; margin-right: .25rem; }
.adm-crumbs a { color: inherit; }
.adm-crumbs [aria-current="page"] { color: var(--adm-text); font-weight: 600; }
.adm-pagehead-actions { display: flex; flex-wrap: wrap; gap: .5rem; }
```

Note: the `<li>` text above has the `<li>` holding an `<a>` or a `<span aria-current>`; the test's `Single` matches the `<span aria-current="page"` form.

- [ ] **Step 4: Run to verify**

Run: `dotnet test tests/Portfolio.Web.Tests --filter "FullyQualifiedName~AdminMenuTests|FullyQualifiedName~AdminShellTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Portfolio.Web/Pages/Admin/_AdminLayout.cshtml src/Portfolio.Web/wwwroot/css/admin.css tests/Portfolio.Web.Tests/Admin/AdminMenuTests.cs tests/Portfolio.Web.Tests/Admin/AdminShellTests.cs
git commit -m "feat(admin): shared page header with a breadcrumb derived from the menu"
```

---

### Task 6: Dashboard: status, checklist, counts (criterion 8)

**Files:**
- Create: `src/Portfolio.Web/Services/AdminDashboard.cs`
- Modify: `src/Portfolio.Web/Pages/Admin/Index.cshtml`, `src/Portfolio.Web/Pages/Admin/Index.cshtml.cs`, `src/Portfolio.Web/wwwroot/css/admin.css`
- Test: `tests/Portfolio.Web.Tests/Admin/AdminDashboardTests.cs`; fix any existing test still reading the removed tiles

**Interfaces:**
- Consumes: `ContentCounts`, `AdminContentCounts` (Task 3), `OnboardingState`, `TemplateView`, `EditorView`, `EditorFields.Sections`.
- Produces:
  - `record DashboardStatus(bool SetupPending, string TemplateName)`, `record DashboardTask(string Text, string Url)`, `record DashboardCount(string Section, string Label, int Count, string Url, string? Detail)`.
  - `record DashboardModel(DashboardStatus? Status, IReadOnlyList<DashboardTask>? Checklist, IReadOnlyList<DashboardCount>? Counts)` with `static DashboardModel Empty`. A null block means "could not be read"; an empty checklist means "nothing outstanding". Both are omitted from the page.
  - `static class AdminDashboard { static DashboardModel Build(TemplateView? template, OnboardingState? state, string resumePath, ContentCounts? counts); }`
  - `IndexModel.Dashboard` (a `DashboardModel`); `IndexModel.SetupPending` and `ResumePath` stay.

- [ ] **Step 1: Write the failing tests**

Create `tests/Portfolio.Web.Tests/Admin/AdminDashboardTests.cs`:

```csharp
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Portfolio.Data.Entities;
using Portfolio.Web.Configuration;
using Portfolio.Web.Services;
using Portfolio.Web.Tests.Support;
using Xunit;

namespace Portfolio.Web.Tests.Admin;

public class AdminDashboardTests
{
    private static TemplateView Software(params SectionSetting[] sections) =>
        new TemplateResolver(new TemplateCatalog(Path.Combine(AppContext.BaseDirectory, "Templates")))
            .Resolve(new SiteOptions { TemplateKey = "software", Sections = sections.ToList() }, null);

    private static OnboardingState State(bool finished, string? photo) =>
        new(new SiteProfile { OnboardingCompletedAt = finished ? DateTimeOffset.UtcNow : null, PhotoUrl = photo }, null);

    private static ContentCounts Counts(int each, int drafts = 0) =>
        new(EditorFields.Sections.ToDictionary(s => s, _ => each), drafts);

    [Fact]
    public void WhenNothingCanBeRead_NoBlockIsBuilt()
    {
        var model = AdminDashboard.Build(null, null, "/admin/welcome", null);

        Assert.Null(model.Status);
        Assert.Null(model.Checklist);
        Assert.Null(model.Counts);
    }

    [Fact]
    public void EachBlock_IsLeftOutOnItsOwn_WhenOnlyItsDataIsMissing()
    {
        var withoutCounts = AdminDashboard.Build(Software(), State(true, "/uploads/p.jpg"), "/admin/welcome", null);
        var withoutState = AdminDashboard.Build(Software(), null, "/admin/welcome", Counts(1));

        Assert.NotNull(withoutCounts.Status);
        Assert.Null(withoutCounts.Checklist);
        Assert.Null(withoutCounts.Counts);
        Assert.Null(withoutState.Status);
        Assert.Null(withoutState.Checklist);
        Assert.NotNull(withoutState.Counts);
    }

    [Fact]
    public void TheStatus_NamesTheTemplate_AndWhetherSetupIsPending()
    {
        var pending = AdminDashboard.Build(Software(), State(false, null), "/admin/welcome", Counts(0)).Status!;
        var done = AdminDashboard.Build(Software(), State(true, null), "/admin/welcome", Counts(0)).Status!;

        Assert.True(pending.SetupPending);
        Assert.False(done.SetupPending);
        Assert.Equal(Software().Definition.Name, pending.TemplateName);
    }

    [Fact]
    public void TheChecklist_ListsSetupPhotoAndEveryEnabledSectionWithNoEntries_EachWithItsFix()
    {
        var tasks = AdminDashboard.Build(Software(), State(false, null), "/admin/welcome/basics", Counts(0)).Checklist!;

        Assert.Contains(tasks, t => t.Url == "/admin/welcome/basics");
        Assert.Contains(tasks, t => t.Url == "/admin/photo");
        foreach (var s in EditorFields.Sections)
            Assert.Contains(tasks, t => t.Url == $"/admin/content/{s}/edit");
    }

    [Fact]
    public void TheChecklist_LeavesOutWhatIsDone_AndASectionTheTemplateSwitchesOff()
    {
        var counts = new ContentCounts(EditorFields.Sections.ToDictionary(s => s, s => s == "experience" ? 3 : 0), 0);

        var tasks = AdminDashboard.Build(Software(new SectionSetting { Key = "talks", Enabled = false, Shown = false }), State(true, "/uploads/p.jpg"), "/admin/welcome", counts).Checklist!;

        Assert.DoesNotContain(tasks, t => t.Url == "/admin/welcome");
        Assert.DoesNotContain(tasks, t => t.Url == "/admin/photo");
        Assert.DoesNotContain(tasks, t => t.Url == "/admin/content/experience/edit");
        Assert.DoesNotContain(tasks, t => t.Url == "/admin/content/talks/edit");
        Assert.Contains(tasks, t => t.Url == "/admin/content/skills/edit");
    }

    [Fact]
    public void TheChecklist_IsEmpty_WhenNothingIsOutstanding() =>
        Assert.Empty(AdminDashboard.Build(Software(), State(true, "/uploads/p.jpg"), "/admin/welcome", Counts(2)).Checklist!);

    [Fact]
    public void TheCounts_UseTheTemplatesWording_AndCallOutBlogDrafts()
    {
        var counts = new ContentCounts(EditorFields.Sections.ToDictionary(s => s, s => s == "blog" ? 5 : 1), BlogDrafts: 2);

        var rows = AdminDashboard.Build(Software(), State(true, null), "/admin/welcome", counts).Counts!;

        Assert.Equal(EditorFields.Sections, rows.Select(r => r.Section));
        Assert.Equal(new EditorView(Software(), "experience").SectionLabel, rows.Single(r => r.Section == "experience").Label);
        Assert.Equal("/admin/content/blog", rows.Single(r => r.Section == "blog").Url);
        Assert.Equal("3 published, 2 drafts", rows.Single(r => r.Section == "blog").Detail);
        Assert.Null(rows.Single(r => r.Section == "skills").Detail);
    }

    [Fact]
    public async Task TheDashboardPage_ShowsStatusChecklistAndCounts_InsteadOfTiles()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);

        var html = await owner.GetStringAsync("/admin");

        Assert.Contains("id=\"status-heading\"", html);
        Assert.Contains("id=\"todo-heading\"", html);
        Assert.Contains("id=\"counts-heading\"", html);
        Assert.Contains("href=\"/admin/photo\"", Regex.Match(html, @"<section[^>]*aria-labelledby=""todo-heading"".*?</section>", RegexOptions.Singleline).Value);
        Assert.DoesNotContain("adm-tile", html);
        Assert.Matches(@"<a href=""/"" target=""_blank""", html); // the link to the public site
    }

    [Fact]
    public async Task AnOutstandingLine_GoesAway_WhenItsSectionGetsAnEntry()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        Assert.Contains("href=\"/admin/content/experience/edit\"", await owner.GetStringAsync("/admin"));

        await ContentFlow.SeedRowsAsync(app);

        Assert.DoesNotContain("href=\"/admin/content/experience/edit\"", Regex.Match(await owner.GetStringAsync("/admin"), @"<section[^>]*todo-heading.*?</section>", RegexOptions.Singleline).Value);
    }

    [Fact]
    public async Task WhileSetupIsPending_TheContinueCalloutStays_AndTheChecklistLinksToIt()
    {
        using var app = new AdminApp();
        var owner = await WizardFlow.FirstRunClientAsync(app);

        var html = await owner.GetStringAsync("/admin");

        Assert.Contains("Continue setup", html);
        Assert.Contains("Skip setup", html);
        Assert.Contains("Not finished", html);
    }

    [Fact]
    public async Task TheDashboard_StillOpens_WhenTheCountsCannotBeRead_AndLeavesThoseBlocksOut()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        using (var db = app.CreateDbContext())
            await db.Database.ExecuteSqlRawAsync("DROP TABLE \"Experiences\"");

        var response = await owner.GetAsync("/admin");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("id=\"status-heading\"", html);
        Assert.DoesNotContain("id=\"todo-heading\"", html);
        Assert.DoesNotContain("id=\"counts-heading\"", html);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Portfolio.Web.Tests --filter "FullyQualifiedName~AdminDashboardTests"`
Expected: build FAIL (`AdminDashboard`, `DashboardModel` missing).

- [ ] **Step 3: Write the builder**

Create `src/Portfolio.Web/Services/AdminDashboard.cs`:

```csharp
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
```

- [ ] **Step 4: Rewrite the page model and view**

Replace `src/Portfolio.Web/Pages/Admin/Index.cshtml.cs` with:

```csharp
using Portfolio.Web.Services;

namespace Portfolio.Web.Pages.Admin;

public class IndexModel : AdminPageModel
{
    private readonly OnboardingService _onboarding;
    private readonly ILogger<IndexModel> _logger;
    private readonly RequestTemplate _template;
    private readonly AdminContentCounts _counts;

    public IndexModel(OnboardingService onboarding, ILogger<IndexModel> logger, RequestTemplate template, AdminContentCounts counts)
    {
        _onboarding = onboarding;
        _logger = logger;
        _template = template;
        _counts = counts;
    }

    /// <summary>The status row, the "Finish your site" checklist and the counts. A block that could not be read is null and left out.</summary>
    public DashboardModel Dashboard { get; private set; } = DashboardModel.Empty;

    /// <summary>True until the onboarding wizard has been published or skipped. Only a prompt: nothing is forced and no admin page is blocked.</summary>
    public bool SetupPending { get; private set; }

    /// <summary>Where "Continue setup" goes: the first step that is not saved yet.</summary>
    public string ResumePath { get; private set; } = "/admin/welcome";

    public async Task OnGetAsync(CancellationToken ct)
    {
        // The dashboard must open even when the database is struggling: each read is its own try, and a block whose data is missing is simply left out.
        TemplateView? template = null;
        OnboardingState? state = null;
        try
        {
            template = await _template.GetAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not read the template for the dashboard.");
        }
        try
        {
            state = await _onboarding.GetStateAsync(ct);
            SetupPending = state.FirstRun;
            if (state.Draft is { } draft)
                ResumePath = Welcome.WelcomePageModel.Steps[Math.Clamp(draft.Step + 1, 1, 4) - 1].Path;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not read the setup state for the dashboard.");
        }
        Dashboard = AdminDashboard.Build(template, state, ResumePath, await _counts.GetAsync(ct)); // the counts log their own failure
    }
}
```

Replace `src/Portfolio.Web/Pages/Admin/Index.cshtml` with:

```cshtml
@page "/admin"
@model IndexModel
@{
    ViewData["Title"] = "Dashboard";
}
<h1>Dashboard</h1>
<p class="adm-lede">Signed in as <strong>@User.Identity?.Name</strong>.</p>
@if (Model.Dashboard.Status is { } status)
{
    <section class="adm-card" aria-labelledby="status-heading">
        <h2 id="status-heading" class="adm-sr">Site status</h2>
        <dl class="adm-statusrow">
            <div><dt>Setup</dt><dd>@(status.SetupPending ? "Not finished" : "Finished")</dd></div>
            <div><dt>Template</dt><dd>@status.TemplateName</dd></div>
            <div><dt>Public site</dt><dd><a href="/" target="_blank" rel="noopener">View your site<span class="adm-sr"> (opens in a new tab)</span></a></dd></div>
        </dl>
    </section>
}
@if (Model.SetupPending)
{
    <section class="adm-card adm-callout" aria-labelledby="setup-heading">
        <h2 id="setup-heading">Finish setting up your site</h2>
        <p>Your site still shows its placeholder profile. The setup wizard takes about two minutes: pick a template, enter your details, add a photo and publish. Nothing is public until you do. Afterwards, add your experience, skills, work and credentials from <a href="/admin/content">Content</a>.</p>
        <div class="adm-actions">
            <a class="adm-btn adm-btn-primary" href="@Model.ResumePath">Continue setup</a>
            <form method="post" asp-page="/Admin/Welcome/Template" asp-page-handler="Skip">
                <button type="submit" class="adm-btn">Skip setup</button>
            </form>
        </div>
    </section>
}
@if (Model.Dashboard.Checklist is { Count: > 0 } todo)
{
    <section class="adm-card adm-attention" aria-labelledby="todo-heading">
        <h2 id="todo-heading">Finish your site</h2>
        <ul class="adm-checklist">
            @foreach (var task in todo)
            {
                <li><a href="@task.Url">@task.Text</a></li>
            }
        </ul>
    </section>
}
@if (Model.Dashboard.Counts is { Count: > 0 } counts)
{
    <section class="adm-card" aria-labelledby="counts-heading">
        <h2 id="counts-heading">Your content</h2>
        <ul class="adm-counts">
            @foreach (var row in counts)
            {
                <li>
                    <a href="@row.Url"><span class="adm-count-label">@row.Label</span><span class="adm-count-num">@row.Count</span></a>
                    @if (row.Detail is { } detail)
                    {
                        <span class="adm-hint">@detail</span>
                    }
                </li>
            }
        </ul>
    </section>
}
```

Append to `admin.css`:

```css
/* Dashboard */
.adm-statusrow { display: grid; grid-template-columns: repeat(auto-fit, minmax(12rem, 1fr)); gap: 1rem; margin: 0; }
.adm-statusrow dt { font-size: .8rem; font-weight: 600; letter-spacing: .04em; text-transform: uppercase; color: var(--adm-muted); }
.adm-statusrow dd { margin: .15rem 0 0; font-weight: 600; }
.adm-checklist { margin: 0; padding-left: 1.2rem; }
.adm-checklist li + li { margin-top: .35rem; }
.adm-counts { list-style: none; margin: 0; padding: 0; display: grid; gap: .75rem; grid-template-columns: repeat(auto-fit, minmax(14rem, 1fr)); }
.adm-counts a { display: flex; align-items: baseline; justify-content: space-between; gap: .75rem; padding: .6rem .8rem; border: 1px solid var(--adm-border); border-radius: .5rem; background: var(--adm-bg); color: var(--adm-text); text-decoration: none; }
.adm-counts a:hover { border-color: var(--adm-accent); }
.adm-count-num { font-size: 1.25rem; font-weight: 700; font-variant-numeric: tabular-nums; }
.adm-counts .adm-hint { margin: .25rem .8rem 0; }
```
(The `.adm-attention` warning styling arrives with the new tokens in Task 7; until then it is an ordinary card.)

- [ ] **Step 5: Run to verify, then fix tests that read the old tiles**

Run: `dotnet test tests/Portfolio.Web.Tests --filter "FullyQualifiedName~AdminDashboardTests"`
Expected: PASS.

Run: `dotnet test Portfolio.slnx`
Expected: any remaining failure is a test asserting a removed tile or its wording (for example text like "Add, edit, reorder and remove your" or `<h2>…</h2>` of a tile on `/admin`). For each: decide whether the behaviour it guards still exists (then assert it against the menu with `AdminShellTests.MenuOf`, or against the new dashboard blocks) or was only the tile (delete the assertion and say so in the commit message). Never loosen an assertion to make it pass without saying why.

- [ ] **Step 6: Commit**

```bash
git add src/Portfolio.Web/Services/AdminDashboard.cs src/Portfolio.Web/Pages/Admin/Index.cshtml src/Portfolio.Web/Pages/Admin/Index.cshtml.cs src/Portfolio.Web/wwwroot/css/admin.css tests/Portfolio.Web.Tests/Admin/AdminDashboardTests.cs
git commit -m "feat(admin): dashboard shows status, an outstanding checklist and content counts"
```
(Add any existing test files changed in Step 5 to the `git add`.)

---

### Task 7: Visual refresh: tokens and restyle (spec 4.6)

**Files:**
- Modify: `src/Portfolio.Web/wwwroot/css/admin.css`
- Test: `tests/Portfolio.Web.Tests/Admin/AdminCssTests.cs`

**Interfaces:**
- Produces tokens used by Tasks 4 to 6 and by PR B and C: `--adm-space-1..6`, `--adm-radius`, `--adm-radius-sm`, `--adm-shadow`, `--adm-menu-bg`, `--adm-warn-bg`, `--adm-warn-text`; each colour or shadow token has a dark-mode value.

- [ ] **Step 1: Write the failing test**

Create `tests/Portfolio.Web.Tests/Admin/AdminCssTests.cs`:

```csharp
using System.Text.RegularExpressions;
using Xunit;

namespace Portfolio.Web.Tests.Admin;

/// <summary>admin.css is one hand-written file; its tokens must keep a light and a dark value, and the old tokens must stay.</summary>
public class AdminCssTests
{
    private static string Css()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "Portfolio.Web", "wwwroot", "css", "admin.css"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir.FullName, "src", "Portfolio.Web", "wwwroot", "css", "admin.css"));
    }

    private static (string Light, string Dark) Blocks(string css)
    {
        var dark = Regex.Match(css, @"@media \(prefers-color-scheme: dark\) \{\s*:root \{(.*?)\}\s*\}", RegexOptions.Singleline);
        Assert.True(dark.Success, "the dark :root block is missing");
        var light = Regex.Match(css, @"^:root \{(.*?)\}", RegexOptions.Singleline | RegexOptions.Multiline);
        Assert.True(light.Success, "the light :root block is missing");
        return (light.Groups[1].Value, dark.Groups[1].Value);
    }

    [Theory]
    [InlineData("--adm-menu-bg")]
    [InlineData("--adm-warn-bg")]
    [InlineData("--adm-warn-text")]
    [InlineData("--adm-shadow")]
    public void EveryNewColourAndShadowToken_HasALightAndADarkValue(string token)
    {
        var (light, dark) = Blocks(Css());

        Assert.Contains(token + ":", light);
        Assert.Contains(token + ":", dark);
    }

    [Theory]
    [InlineData("--adm-space-1")]
    [InlineData("--adm-space-2")]
    [InlineData("--adm-space-3")]
    [InlineData("--adm-space-4")]
    [InlineData("--adm-space-5")]
    [InlineData("--adm-space-6")]
    [InlineData("--adm-radius")]
    [InlineData("--adm-radius-sm")]
    public void TheSpacingScaleAndRadii_AreDeclared(string token) => Assert.Contains(token + ":", Blocks(Css()).Light);

    [Theory]
    [InlineData("--adm-bg")]
    [InlineData("--adm-surface")]
    [InlineData("--adm-text")]
    [InlineData("--adm-muted")]
    [InlineData("--adm-border")]
    [InlineData("--adm-accent")]
    [InlineData("--adm-ok-bg")]
    [InlineData("--adm-error-bg")]
    public void TheExistingColourTokens_AreKept_InBothSchemes(string token)
    {
        var (light, dark) = Blocks(Css());

        Assert.Contains(token + ":", light);
        Assert.Contains(token + ":", dark);
    }

    [Fact]
    public void TheMotionAndColourSchemeHandling_IsKept()
    {
        var css = Css();

        Assert.Contains("prefers-reduced-motion: reduce", css);
        Assert.Contains("prefers-color-scheme: dark", css);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Portfolio.Web.Tests --filter "FullyQualifiedName~AdminCssTests"`
Expected: FAIL for the new tokens; the "kept" tests PASS.

- [ ] **Step 3: Add the tokens and restyle**

In the light `:root` block of `admin.css` add (after `--adm-error-text`):

```css
  --adm-space-1: .25rem;
  --adm-space-2: .5rem;
  --adm-space-3: .75rem;
  --adm-space-4: 1rem;
  --adm-space-5: 1.5rem;
  --adm-space-6: 2rem;
  --adm-radius: .5rem;
  --adm-radius-sm: .35rem;
  --adm-shadow: 0 1px 2px rgb(16 24 40 / .06), 0 1px 3px rgb(16 24 40 / .08);
  --adm-menu-bg: #ffffff;
  --adm-warn-bg: #fff4dc;
  --adm-warn-text: #7a4b00;
```

In the dark `:root` block (inside `@media (prefers-color-scheme: dark)`) add:

```css
    --adm-shadow: 0 1px 2px rgb(0 0 0 / .5), 0 1px 3px rgb(0 0 0 / .4);
    --adm-menu-bg: #10151c;
    --adm-warn-bg: #3a2a0a;
    --adm-warn-text: #fcd9a0;
```

Restyle the shared components so they use the tokens (edit the existing rules in place):

- `.adm-card`: `border-radius: var(--adm-radius); box-shadow: var(--adm-shadow); padding: var(--adm-space-5) …` (keep the existing border and background; `padding: 1.25rem` becomes `var(--adm-space-5)`, `margin-bottom: 1.25rem` becomes `var(--adm-space-5)`).
- `.adm-btn`: `border-radius: var(--adm-radius-sm);` and add `.adm-btn:hover { border-color: var(--adm-accent); }` and `.adm-btn-primary:hover { filter: brightness(1.08); }`.
- `.adm-field input…, .adm-field textarea`: `border-radius: var(--adm-radius-sm);`.
- `.adm-tabs a`: add `padding: .25rem .1rem;` and keep the bottom-border current marker.
- `.adm-pill`: unchanged colours, `border-radius: 999px` kept.
- `.adm-flash`: `border-radius: var(--adm-radius-sm); box-shadow: var(--adm-shadow);`.
- Replace the menu surface: `.adm-menu { background: var(--adm-menu-bg); }`, `.adm-topbar`, `.adm-shell.adm-js .adm-topbar` and `.adm-plainbar` use `background: var(--adm-menu-bg)`.

Append the warning and split-layout styles (the split layout is used by PR B and C; defined here so they inherit it, per spec 4.6):

```css
/* Needs attention */
.adm-attention { background: var(--adm-warn-bg); color: var(--adm-warn-text); border-color: transparent; }
.adm-attention a { color: inherit; font-weight: 600; }
.adm-attention h2 { color: inherit; }

/* Split layout: a main column and a side column, stacking below 40rem (used by the template picker and the wizard). */
.adm-split { display: grid; grid-template-columns: minmax(0, 1fr) 20rem; gap: var(--adm-space-5); align-items: start; }
@media (max-width: 40rem) { .adm-split { grid-template-columns: 1fr; } }
```

- [ ] **Step 4: Run to verify, then the suite**

Run: `dotnet test tests/Portfolio.Web.Tests --filter "FullyQualifiedName~AdminCssTests"`
Expected: PASS.

Run: `dotnet test Portfolio.slnx`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Portfolio.Web/wwwroot/css/admin.css tests/Portfolio.Web.Tests/Admin/AdminCssTests.cs
git commit -m "feat(admin): design tokens, shadow, warning pair and a consistent restyle"
```

---

### Task 8: Verification (acceptance criteria 1 to 9)

**Files:** none (this task produces evidence, plus fixes if it finds defects; any fix gets its own test first).

- [ ] **Step 1: Clean build and the full suite**

Run: `dotnet build Portfolio.slnx` then `dotnet test Portfolio.slnx`.
Expected: 0 errors; warnings only the 16 pre-existing NU1902/NU1903 advisories; every test passes; the count of tests executed is greater than the baseline (a pass on zero executed tests is not a pass).

- [ ] **Step 2: Map each criterion to its evidence**

| Criterion | Evidence |
|---|---|
| 1 menu groups, every page reachable | `AdminShellTests.EverySignedInAdminPage_ShowsTheMenu_WithTheGroupsAndEveryTopLevelPage`, `AdminMenuTests.ProfileAndContent_AreSubMenus_ThatReachEveryPageUnderThem` |
| 2 template wording | `AdminMenuTests.Labels_ComeFromTheTemplatesWording_NotFromTheMenu`, `AdminShellTests.TheMenu_UsesTheTemplatesWords` |
| 3 toggle, persistence, no flicker | server side: `TheMenu_IsExpandedByDefault_AndCollapsedByTheCookie_…`, `WizardPages_DrawTheRail_…`; script and flicker: manual (Step 3) |
| 4 no script | `WithoutScript_EveryLinkIsPlainMarkup_…`; manual with script off (Step 3) |
| 5 overlay below 40rem | markup hooks: `TheNarrowScreenOverlay_…`; behaviour: manual (Step 3) |
| 6 hidden section dimmed | `ASectionTheTemplateSwitchesOff_IsDimmed_WithTheTextHidden`, `AdminMenuTests.ASectionTheTemplateSwitchesOff_…` |
| 7 breadcrumb and one `h1` | `EverySignedInPage_HasABreadcrumb_AndExactlyOneH1`, breadcrumb unit tests |
| 8 dashboard blocks and failure | `AdminDashboardTests` (all), `AFailedCountNeverBreaksAPage_…` |
| 9 CSP | `AdminCspTests`, `ContentSecurityTests.EveryAdminPage_MayLoadFirstPartyScript_…` |

- [ ] **Step 3: Manual browser pass (criteria 3, 4, 5 and the visual items)**

The app needs SQL Server LocalDB to run for real, so render the pages from the test host instead: write a throwaway test or console snippet (in the session scratchpad, never committed) that creates an `AdminApp`, signs in, saves `/admin`, `/admin/content/skills`, and `/admin/welcome` HTML to files, copy `src/Portfolio.Web/wwwroot` next to them, serve that folder with `python -m http.server`, and open the saved pages with the Playwright browser tools. Check at 1280 by 800 and 390 by 844, light and dark (`emulate_media`):

1. Wide: menu expanded; click "Collapse menu": it becomes a rail, tooltips show on hover and keyboard focus, `aria-expanded` flips, the cookie `adm-menu=collapsed` is set with path `/admin`; reload the page with that cookie replayed in the saved HTML (`data-menu="collapsed"`) and confirm no flash of the wide menu.
2. Narrow: the menu button opens the overlay, Escape closes it and returns focus to the button, choosing a link closes it.
3. Script blocked (`javascript_enabled` off or remove the script tag): on narrow the menu sits above the page and every link is reachable; on wide it stays expanded.
4. Dark mode: menu, dashboard cards, the warning checklist and the crumbs are legible; focus rings are visible; keyboard-only Tab through the menu reaches every link and the sub-menu summaries open with Enter or Space.

Record what was checked and what could not be (for example the cookie round-trip against the real server). Fix defects test-first.

- [ ] **Step 4: Confirm nothing outside scope changed**

Run: `git diff --stat develop...HEAD` and check that only the files in this plan changed, that `app.css` is not in the diff, and that no PR B or C file (template picker partials, wizard pages) was touched.

- [ ] **Step 5: Request review (do not push)**

Use the `quality-reviewer` agent on `git diff develop...HEAD` with the acceptance criteria 1 to 9 and the decisions section of this plan, then fix what it finds, test-first. Do not push and do not open a PR until asked.

---

## Self-Review

**Spec coverage (section 4 and criteria 1 to 9):**
- 4.1 menu structure, sub-menus from native `<details>`, open when current, hidden tag, counts tolerated, `aria-current`: Tasks 2, 4. Collapse button with `aria-expanded`, rail behaviour, tooltips and accessible names, cookie `adm-menu=collapsed` scoped to `/admin` `SameSite=Lax` written by script and compared with the literal only, first paint read by the layout, wizard rail without writing the cookie, overlay below 40rem closed by button, Escape or link, no-script behaviour: Tasks 2 and 4 (script in Step 5, CSS in Step 6).
- 4.2 units: `AdminMenu` (Task 2), `AdminMenuViewComponent` (Task 4), `admin-shell.js` (Task 4), `_AdminLayout.cshtml` hosting menu, header and flash with skip link and flash focus kept (Tasks 4, 5). `AdminChrome` and `AdminContentCounts` are added units (decision 5).
- 4.3 CSP: Task 1 (rule), Task 4 (layout emits the tag only when signed in and allowed; `admin-unsaved.js` unchanged).
- 4.4 page header: Task 5, with the `h1` deviation stated in Decisions.
- 4.5 dashboard: Task 6 (status row, checklist, counts with blog drafts, failure tolerance, "Continue setup" callout kept).
- 4.6 visual refresh: Task 7 (tokens with dark values, restyled card/button/field/tabs/pill/flash, header/menu/split layout, full-width main with forms capped, `prefers-*` kept).
- Criteria 1 to 9: mapped in Task 8 Step 2.

**Placeholder scan:** no "TBD"/"TODO"/"handle edge cases". Two conditional notes exist on purpose and are actionable: Task 2's assumption about `SiteOptions.Sections`' type, and Task 4's note on how the form tag helper orders attributes and how the test client forwards a manual `Cookie` header. Task 6 Step 5 says how to decide per failing old test.

**Type consistency:** `AdminMenuItem` positional order `(Key, Label, Url, Icon, Count, Hidden, IsCurrent, Children)` is used identically in Tasks 2, 4. `AdminMenu.Build(TemplateView?, string, IReadOnlyDictionary<string,int>?)` matches Task 3's call `_menu.Build(view, path, counts?.Entries)`. `ContentCounts(Entries, BlogDrafts)` matches Task 6. `AdminChrome.MenuAsync(string, CancellationToken)` matches the layout call. `AdminMenuView(Menu, Collapsed)` matches the component and `Default.cshtml`. Item keys (`profile.basics`, `content.<section>`, `wizard`, `transfer`) match the tests. `AdminShellTests.MenuOf` is `internal static`, used from `ContentAccessibilityTests` and `TransferTemplateTests` in the same test assembly.
