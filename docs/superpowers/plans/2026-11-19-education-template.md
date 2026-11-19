# Education Template Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add Education, a school-teacher template, as a data-only preset on the existing Credentials layout, and generalise the Healthcare-named Credentials tests into a keyed list that covers both templates.

**Architecture:** A new template folder (`template.json`, `starter.json`, `preview.png`), one theme stylesheet, four generated starter pictures and a regenerated `app.css`. No C#, Razor, migration, CSP or script change. Tests move from Healthcare-named methods to a `CredentialsArchetypeTests` class driven by a `Keys` list (as `GalleryArchetypeTests` and `ResultsArchetypeTests` already work), and `ArchetypeTemplateFlowTests` learns the Credentials archetype.

**Tech Stack:** .NET 10, ASP.NET Core Razor Pages, xUnit, Tailwind (standalone CLI, run by the build), headless browser for the thumbnail.

**Spec:** `docs/superpowers/specs/2026-11-19-education-template-design.md`

## Global Constraints

Copied from the spec. Every task includes these.

- No C# change, no Razor change, no migration, no CSP change, no new script, no new copy key. `/cv`, `/cv.pdf` and `/og.png` keep the Software look (Phase 6).
- No layout partial, block or page names a template by key, theme or profession (an existing test greps for this).
- The Software, Healthcare, Gallery, Results (Marketing, Sales, Finance) public output must not change. The shared stylesheet changes only by the new `[data-template="education"]` theme rules.
- The committed `src/Portfolio.Web/wwwroot/css/app.css` is the LF build output that CI regenerates and compares. On Windows with `core.autocrlf=true` a local build writes a byte-different file. **Never stage `wwwroot/css/app.css` except in Task 8**, and stage files by explicit path, never `git add -A`.
- Visible wording must pass `DemoSite.SoftwareWording` (no "system(s)", "talk(s)", "engineer", "cloud", "stack", "software", "developer", "case study", "toolkit", "API", "embedded", "distributed", ".NET", "GitHub") on every public page.
- Every text and accent token is at least 4.5:1 on bg, surface and surface-2 in light and dark, including the four accent choices.
- Starter content is fictional: employers marked "(fictional)", initiatives and qualifications marked "(sample)", invented bodies named "Example ...", `example.org` links, no legal-form suffix (Inc, Ltd, LLC, LLP, GmbH, PLC) apart or glued on, no pupil data of any kind.
- Commit messages and code comments name no tool or assistant (repo working agreement). Do not push, open a PR or merge unless the user asks.
- Work in the worktree `e:\Repos\living-cv\.claude\worktrees\education-template` on branch `feature/phase-5d-education-template`. Do not switch branches in `e:\Repos\living-cv`.

## Commands used throughout

Run from the worktree root. `T` is the test project.

```bash
T=tests/Portfolio.Web.Tests
dotnet test $T --nologo --filter "FullyQualifiedName~<ClassOrMethod>"     # focused run
git checkout -- src/Portfolio.Web/wwwroot/css/app.css                       # undo the CRLF build output before committing
```

---

## File Structure

| File | Action | Responsibility |
|---|---|---|
| `docs/superpowers/specs/2026-11-19-education-template-design.md` | Modify | Record three corrections found while planning |
| `tests/Portfolio.Web.Tests/Templates/CredentialsArchetypeTests.cs` | Create | The keyed Credentials checks: rendering wording, 404s, images, meta, starter shape, theme, held-only credentials, editor guidance, Education-specific order |
| `tests/Portfolio.Web.Tests/Templates/TemplateRenderingTests.cs` | Modify | Remove the six Healthcare-named methods that move into the keyed class |
| `tests/Portfolio.Web.Tests/Templates/ArchetypeTemplateFlowTests.cs` | Modify | Cover the Credentials archetype (card marker, no "Next" link, key list) |
| `tests/Portfolio.Web.Tests/Templates/TemplateManifestTests.cs` | Modify | Folder list and catalog order |
| `tests/Portfolio.Web.Tests/Admin/OnboardingFlowTests.cs` | Modify | Card, guidance and radio counts |
| `src/Portfolio.Web/Templates/education/template.json` | Create | The manifest |
| `src/Portfolio.Web/Templates/education/starter.json` | Create | Fictional sample content |
| `src/Portfolio.Web/Templates/education/preview.png` | Create | 1200 x 800 dark-mode screenshot |
| `src/Portfolio.Web/Styles/themes/education.css` | Create | Theme variables |
| `src/Portfolio.Web/Styles/app.css` | Modify | One `@import` line |
| `src/Portfolio.Web/wwwroot/css/app.css` | Modify (Task 8 only) | Regenerated LF build output |
| `src/Portfolio.Web/wwwroot/img/starter/education/*.svg` | Create | Four generated starter pictures |
| `README.md`, `docs/new-user-guide.md`, `docs/template-authoring-guide.md`, `docs/templates-and-onboarding-plan.md` | Modify | Document the template and record the work |

---

### Task 1: Record three corrections in the spec

Found while reading the code for this plan. The spec was approved before them, so say so in the commit.

**Files:**
- Modify: `docs/superpowers/specs/2026-11-19-education-template-design.md`

**Interfaces:**
- Produces: a spec that matches the plan, so a reviewer comparing the two sees no gap.

- [ ] **Step 1: Correct section 6, first bullet ("Credentials keyed list")**

Replace the whole bullet with:

```markdown
- **Credentials keyed list.** Add `CredentialsArchetypeTests.Keys = ["healthcare", "education"]`. The six Healthcare-named rendering checks in `TemplateRenderingTests` (every route, disabled sections, project images, meta descriptions, missing item, empty site) move into that class as theories driven by a per-key wording table, with Healthcare's strings carried over unchanged. `DemoSite.HealthcareWording` is a guard for **Software** pages and stays as it is: it contains "initiative", "registration" and "references", which Education uses legitimately. The per-key guard for a Credentials template is its own `Foreign` pattern. `ArchetypeTemplateFlowTests` also runs over the Credentials keys, with the card marker and the "Next" link made archetype-aware (the Credentials layout has no work-page partials, so `/projects` uses the shared markup).
```

- [ ] **Step 2: Correct section 5, criterion 10**

Replace it with:

```markdown
10. A before and after render of 14 routes for Software, Healthcare, Design & UX, Photography & Film, Marketing & Content, Sales & BD and Finance & Accounting, in two content modes each (the demo seed switched to the template, and the template's own starter), is identical after normalising asset fingerprints. The comparison is taken from a clean export of the base commit and of the branch head, with a scratch test that is not committed.
```

- [ ] **Step 3: Add a risk to section 8**

Append this bullet to the risk list:

```markdown
- The Credentials layout was only ever exercised by Healthcare, so the flow checks may expose an assumption that only held for it. Mitigation: run the flow checks for Healthcare alone first (Task 3 of the plan), so a failure is attributed to the checks and not to Education.
```

- [ ] **Step 4: Commit**

```bash
git add docs/superpowers/specs/2026-11-19-education-template-design.md
git commit -m "docs: correct the Education design after reading the Credentials tests and layout"
```

---

### Task 2: Move the Healthcare-named rendering checks into a keyed Credentials class

A behaviour-preserving refactor. Keys is Healthcare only here, so every moved check must pass with Healthcare's own strings before Education exists.

**Files:**
- Create: `tests/Portfolio.Web.Tests/Templates/CredentialsArchetypeTests.cs`
- Modify: `tests/Portfolio.Web.Tests/Templates/TemplateRenderingTests.cs` (delete six methods)

**Interfaces:**
- Produces (used by Tasks 3 to 6):
  - `CredentialsArchetypeTests.Keys : string[]`, `KeyData : TheoryData<string>`
  - `CredentialsArchetypeTests.Wording` record and `CredentialsArchetypeTests.Wordings : Dictionary<string, Wording>` (internal)
  - `CredentialsArchetypeTests.Definition(string key) : TemplateDefinition` (internal static)

- [ ] **Step 1: Create the keyed class**

Create `tests/Portfolio.Web.Tests/Templates/CredentialsArchetypeTests.cs`:

```csharp
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Portfolio.Data.Entities;
using Portfolio.Web.Services;
using Portfolio.Web.Tests.Support;
using Xunit;

namespace Portfolio.Web.Tests.Templates;

/// <summary>
/// The checks for every template on the credentials archetype (registration and qualifications first, then practice), through data only. Driven by <see cref="Keys"/> and
/// <see cref="Wordings"/>: a further template on this archetype is covered by adding its key and its wording entry here.
/// </summary>
public class CredentialsArchetypeTests
{
    /// <summary>Every template on the credentials archetype.</summary>
    public static readonly string[] Keys = ["healthcare"];

    public static TheoryData<string> KeyData => new(Keys);

    public static TheoryData<string, string> DisabledSectionData
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var key in Keys)
                foreach (var path in new[] { "/blog", "/blog/anything", "/talks" }) data.Add(key, path);
            return data;
        }
    }

    /// <summary>
    /// What one template's own pages must say. Each string is wording from that template's manifest or starter, so a change of wording there is a change here too.
    /// <see cref="Foreign"/> is wording that belongs to another profession and must never appear on this template's pages (null when there is none to guard).
    /// </summary>
    internal sealed record Wording(
        string[] Home, string[] About, string[] Experience, string[] Projects, string DetailSlug, string[] Detail,
        string[] Skills, string[] Certifications, string[] Cv, string[] Contact,
        string[] NavLabels, string FooterNote, string MissingBackLink,
        (string Path, string Text)[] EmptyPages, string EmptyHome,
        string LongTitle, string GuidancePhrase, Regex? Foreign);

    internal static readonly Dictionary<string, Wording> Wordings = new()
    {
        ["healthcare"] = new Wording(
            Home: ["Licences &amp; registrations", "Clinical experience", "Clinical skills", "Research &amp; initiatives", "References", "Sample profile (fictional)"],
            About: ["Safe, compassionate critical care"],
            Experience: ["Clinical experience", "where I have practised"],
            Projects: ["Research &amp; initiatives", "Read initiative"],
            DetailSlug: "early-mobility-programme",
            Detail: ["Initiative", "Tools &amp; methods", "Aim", "Method"],
            Skills: ["Clinical skills", "Procedures", "Documentation"],
            Certifications: ["Licences, registrations &amp; training", "Current", "Registration"],
            Cv: ["Clinical experience", "Licences &amp; registrations", "Research &amp; initiatives"],
            Contact: ["Please do not send patient information", "roles, referrals or collaboration"],
            NavLabels: [">about<", ">registrations<", ">experience<", ">skills<", ">initiatives<", ">cv<"],
            FooterNote: "contains no patient information",
            MissingBackLink: "← Back to research &amp; initiatives",
            EmptyPages:
            [
                ("/projects", "No initiatives yet"),
                ("/experience", "No roles have been added yet."),
                ("/skills", "No skills have been added yet."),
                ("/certifications", "No registrations yet"),
            ],
            EmptyHome: "Initiatives coming soon",
            LongTitle: "Advanced Clinical Practitioner in Paediatric Intensive Care and Retrieval Medicine",
            GuidancePhrase: "Never publish patient-identifiable information",
            Foreign: null),
    };

    internal static string TemplatesDir => Path.Combine(AppContext.BaseDirectory, "Templates");
    internal static TemplateDefinition Definition(string key) => new TemplateCatalog(TemplatesDir).Get(key);

    private static async Task<(HttpStatusCode Status, string Html)> GetAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    // ---------------------------------------------------------------- the public pages, in the template's own wording

    [Theory]
    [MemberData(nameof(KeyData))]
    public async Task EveryPublicRoute_RendersInTheTemplatesOwnWording_WithNoSoftwareWording(string key)
    {
        var w = Wordings[key];
        var theme = Definition(key).Theme.Default;
        using var app = new AdminApp();
        await DemoSite.ApplyStarterAsync(app, key);
        var client = app.NewClient();

        var expectations = new (string Path, string[] Contains)[]
        {
            ("/", w.Home),
            ("/about", w.About),
            ("/experience", w.Experience),
            ("/projects", w.Projects),
            ($"/projects/{w.DetailSlug}", w.Detail),
            ("/skills", w.Skills),
            ("/certifications", w.Certifications),
            ("/cv", w.Cv),
            ("/contact", w.Contact),
        };

        foreach (var (path, contains) in expectations)
        {
            var (status, html) = await GetAsync(client, path);
            Assert.True(status == HttpStatusCode.OK, $"{path} returned {(int)status}");
            Assert.Contains($"<html lang=\"en\" data-template=\"{theme}\">", html);
            foreach (var text in contains) Assert.Contains(text, html);
            var visible = DemoSite.WithoutScripts(html);
            Assert.False(DemoSite.SoftwareWording.IsMatch(visible), $"{path} shows software wording: {DemoSite.SoftwareWording.Match(visible).Value}");
            if (w.Foreign is not null) Assert.False(w.Foreign.IsMatch(visible), $"{path} shows another profession's wording: {w.Foreign.Match(visible).Value}");
        }

        var nav = (await GetAsync(client, "/")).Html;
        foreach (var label in w.NavLabels) Assert.Contains(label, nav);
        Assert.DoesNotContain(">blog<", nav);
        Assert.DoesNotContain(">talks<", nav);
        Assert.DoesNotContain("ASP.NET", nav);
        Assert.Contains(w.FooterNote, nav);
    }

    [Theory]
    [MemberData(nameof(DisabledSectionData))]
    public async Task SectionsTheTemplateDoesNotUse_AnswerNotFound(string key, string path)
    {
        using var app = new AdminApp();
        await DemoSite.ApplyStarterAsync(app, key);

        var (status, html) = await GetAsync(app.NewClient(), path);

        Assert.Equal(HttpStatusCode.NotFound, status);
        // A styled page that is the same for every disabled section, with nothing that says a section exists or is switched off.
        var (otherStatus, otherHtml) = await GetAsync(app.NewClient(), path == "/talks" ? "/blog" : "/talks");
        Assert.Equal(HttpStatusCode.NotFound, otherStatus);
        Assert.Contains("<html", html);
        Assert.Contains("Page not found", html);
        Assert.Equal(DemoSite.Normalise(otherHtml), DemoSite.Normalise(html));
        var body = DemoSite.WithoutScripts(html);
        Assert.DoesNotMatch(@"(?i)blog|talk|article|presentation|disabled", body.Substring(body.IndexOf("<main", StringComparison.Ordinal)));
    }

    [Theory]
    [MemberData(nameof(KeyData))]
    public async Task ProjectImages_UseNeutralWording(string key)
    {
        var w = Wordings[key];
        var title = new TemplateCatalog(TemplatesDir).GetStarter(key)!.Projects.Single(p => p.Slug == w.DetailSlug).Title;
        using var app = new AdminApp();
        await DemoSite.ApplyStarterAsync(app, key);
        using (var db = app.CreateDbContext())
        {
            // Give the first initiative a cover and a gallery with and without captions, whatever pictures the starter carries.
            var project = await db.Projects.SingleAsync(p => p.Slug == w.DetailSlug);
            project.HeroImage = "/img/projects/_placeholder.svg";
            project.HeroImageAlt = null;
            project.Screenshots = [new ProjectImage { Url = "/img/projects/_placeholder.svg", Caption = "Run chart" }, new ProjectImage { Url = "/img/projects/_placeholder.svg" }];
            await db.SaveChangesAsync();
        }
        app.Services.GetRequiredService<ISiteProfileProvider>().Invalidate();
        var client = app.NewClient();

        foreach (var path in new[] { "/", "/projects", $"/projects/{w.DetailSlug}" })
        {
            var (status, html) = await GetAsync(client, path);
            Assert.Equal(HttpStatusCode.OK, status);
            Assert.DoesNotContain("screenshot", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("<img", html);
        }
        var detail = (await GetAsync(client, $"/projects/{w.DetailSlug}")).Html;
        Assert.Contains("aria-label=\"Images\"", detail);
        Assert.Contains("aria-label=\"Open image: Run chart\"", detail);
        Assert.Contains("aria-label=\"Open image\"", detail);
        Assert.Contains("alt=\"Run chart\"", detail);
        Assert.Contains($"alt=\"{title} image\"", detail);
        Assert.Contains($"alt=\"{title} image\"", (await GetAsync(client, "/projects")).Html);
        Assert.Contains($"alt=\"{title} image\"", (await GetAsync(client, "/")).Html);
    }

    [Theory]
    [MemberData(nameof(KeyData))]
    public async Task MetaDescriptions_StayWithin160Characters_ForALongNameAndTitle(string key)
    {
        var w = Wordings[key];
        using var app = new AdminApp();
        await DemoSite.ApplyStarterAsync(app, key);
        using (var db = app.CreateDbContext())
        {
            var profile = await db.SiteProfiles.SingleAsync();
            profile.OwnerName = "Dr Alexandra Wilhelmina Featherstonehaugh-Montgomery";
            profile.JobTitle = w.LongTitle;
            await db.SaveChangesAsync();
        }
        app.Services.GetRequiredService<ISiteProfileProvider>().Invalidate();
        var client = app.NewClient();

        foreach (var path in new[] { "/", "/about", "/experience", "/skills", "/certifications", "/projects", "/contact", "/cv" })
        {
            var html = (await GetAsync(client, path)).Html;
            var description = WebUtility.HtmlDecode(Regex.Match(html, "<meta name=\"description\" content=\"([^\"]*)\"").Groups[1].Value);
            Assert.False(string.IsNullOrWhiteSpace(description), $"{path} has no description");
            Assert.True(description.Length <= 160, $"{path} description is {description.Length} characters: {description}");
            Assert.DoesNotMatch(@"\b(is a|is an)\b", description);
        }
        var home = WebUtility.HtmlDecode(Regex.Match((await GetAsync(client, "/")).Html, "<meta name=\"description\" content=\"([^\"]*)\"").Groups[1].Value);
        Assert.StartsWith($"Dr Alexandra Wilhelmina Featherstonehaugh-Montgomery, {w.LongTitle[..30]}", home);
        Assert.EndsWith("…", home);
    }

    [Theory]
    [MemberData(nameof(KeyData))]
    public async Task AMissingInitiative_UsesTheTemplatesItemWord(string key)
    {
        var w = Wordings[key];
        using var app = new AdminApp();
        await DemoSite.ApplyStarterAsync(app, key);

        var (status, html) = await GetAsync(app.NewClient(), "/projects/no-such-initiative");

        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Contains("Initiative not found", html);
        Assert.Contains(w.MissingBackLink, html);
        Assert.DoesNotContain("Case study", html);
    }

    [Theory]
    [MemberData(nameof(KeyData))]
    public async Task AnEmptySite_ShowsTheTemplatesEmptyStates_NotSoftwareOnes(string key)
    {
        var w = Wordings[key];
        using var app = new AdminApp();
        await DemoSite.ApplyStarterAsync(app, key);
        using (var db = app.CreateDbContext())
        {
            db.Projects.RemoveRange(db.Projects);
            db.Experiences.RemoveRange(db.Experiences);
            db.Skills.RemoveRange(db.Skills);
            db.Certifications.RemoveRange(db.Certifications);
            db.Testimonials.RemoveRange(db.Testimonials);
            await db.SaveChangesAsync();
        }
        var client = app.NewClient();

        foreach (var (path, text) in w.EmptyPages)
        {
            var (status, html) = await GetAsync(client, path);
            Assert.Equal(HttpStatusCode.OK, status);
            Assert.Contains(text, html);
            var visible = DemoSite.WithoutScripts(html);
            Assert.False(DemoSite.SoftwareWording.IsMatch(visible), $"{path}: {DemoSite.SoftwareWording.Match(visible).Value}");
            if (w.Foreign is not null) Assert.False(w.Foreign.IsMatch(visible), $"{path}: {w.Foreign.Match(visible).Value}");
        }
        var home = await GetAsync(client, "/");
        Assert.Contains(w.EmptyHome, home.Html);
        Assert.DoesNotMatch(DemoSite.SoftwareWording, DemoSite.WithoutScripts(home.Html));
    }
}
```

- [ ] **Step 2: Delete the six Healthcare-named methods from `TemplateRenderingTests.cs`**

Delete each of these whole methods (attributes above them and the blank line below included), and the `// ---- Healthcare ----` comment line above the first one. Leave `Healthcare` references that sit in the "same data in both templates" test further down (it is about switching, not about Healthcare's pages).

- `Healthcare_RendersEveryPublicRoute_InItsOwnWording_WithNoSoftwareWording`
- `Healthcare_SectionsItDoesNotUse_AnswerNotFound` (a `[Theory]` with three `[InlineData]` lines)
- `Healthcare_ProjectImages_UseNeutralWording`
- `Healthcare_MetaDescriptions_StayWithin160Characters_ForALongNameAndTitle`
- `Healthcare_MissingInitiative_UsesItsItemWord`
- `Healthcare_EmptySite_ShowsItsEmptyStates_NotSoftwareOnes`

Keep `OtherNotFoundResponses_KeepTheirPlainEmpty404` and `TheNotFoundPage_IsA404ThatSearchEnginesAreToldToIgnore`, which sit between them.

Run: `grep -n "Healthcare_" tests/Portfolio.Web.Tests/Templates/TemplateRenderingTests.cs`
Expected: no output (the later switching test is named differently; if a `Healthcare_` method remains that is not one of the six, leave it).

- [ ] **Step 3: Run the moved checks and the rest of the rendering tests**

Run: `dotnet test $T --nologo --filter "FullyQualifiedName~CredentialsArchetypeTests|FullyQualifiedName~TemplateRenderingTests"`
Expected: PASS. Eight Healthcare cases in `CredentialsArchetypeTests` (one per method, three for the 404 theory) plus the remaining `TemplateRenderingTests`. If a moved check fails for Healthcare, the move changed behaviour: compare the failing assertion with the deleted original in `git show HEAD:tests/Portfolio.Web.Tests/Templates/TemplateRenderingTests.cs` and fix the new code, never the expectation.

- [ ] **Step 4: Commit**

```bash
git checkout -- src/Portfolio.Web/wwwroot/css/app.css
git add tests/Portfolio.Web.Tests/Templates/CredentialsArchetypeTests.cs tests/Portfolio.Web.Tests/Templates/TemplateRenderingTests.cs
git commit -m "test: run the Credentials rendering checks from a keyed list, with Healthcare's wording carried over unchanged"
```

---

### Task 3: Let the flow checks cover the Credentials archetype (Healthcare only)

`ArchetypeTemplateFlowTests` currently runs over Gallery and Results keys and assumes their markup. Make the three assumptions archetype-aware, add the Credentials keys, and run it for Healthcare alone, so a failure here is about the checks and not about Education.

**Files:**
- Modify: `tests/Portfolio.Web.Tests/Templates/ArchetypeTemplateFlowTests.cs`

**Interfaces:**
- Consumes: `CredentialsArchetypeTests.Keys`.
- Produces: `ArchetypeTemplateFlowTests.Keys` now includes the Credentials keys; `Card(key)` returns the right marker for all three archetypes.

- [ ] **Step 1: Update the summary and the key list**

In `ArchetypeTemplateFlowTests.cs`, replace the class summary (lines 12 to 16) and the `Keys` line (line 19):

```csharp
/// <summary>
/// The admin and cross-cutting paths for every template on an archetype other than Showcase (gallery, results and credentials), through data only: wizard, preview,
/// switching, section settings, sitemap and structured data, export and import, and a blank wizard-finished site end to end. Driven by
/// <see cref="GalleryArchetypeTests.Keys"/>, <see cref="ResultsArchetypeTests.Keys"/> and <see cref="CredentialsArchetypeTests.Keys"/>: a further template on any of them is covered by adding its key there.
/// </summary>
public class ArchetypeTemplateFlowTests
{
    public static readonly string[] Keys = [.. GalleryArchetypeTests.Keys, .. ResultsArchetypeTests.Keys, .. CredentialsArchetypeTests.Keys];
```

(Keep the existing `public static TheoryData<string> KeyData => new(Keys);` line that follows.)

- [ ] **Step 2: Make the card marker archetype-aware**

Replace the `Card` helper (line 24):

```csharp
    /// <summary>What marks one work card in the archetype's own layout. The credentials layout has no work-page partials, so its cards are the shared markup the work page and the home grid both use.</summary>
    private static string Card(string key) => Definition(key).Archetype switch
    {
        Archetypes.Results => "data-results-card",
        Archetypes.Credentials => "<article class=\"card group flex flex-col\"",
        _ => "<article class=\"group relative",
    };

    /// <summary>Only the gallery and results case-study pages end with a link to the next entry.</summary>
    private static bool HasNextLink(string key) => Definition(key).Archetype is Archetypes.Gallery or Archetypes.Results;
```

- [ ] **Step 3: Make the preview check archetype-aware**

In `ThePreview_ShowsTheGalleryLayoutToTheOwnerOnly_AndSavesNothing`, replace the line

```csharp
            if (path != "/") Assert.Contains(path == "/projects" ? Card(key) : "Next ", html);
```

with

```csharp
            if (path == "/projects") Assert.Contains(Card(key), html);
            else if (path != "/" && HasNextLink(key)) Assert.Contains("Next ", html);
```

- [ ] **Step 4: Healthcare joins the list, so stop naming it in the export check**

In `AnExportFromTheTemplate_ImportsIntoTheOthers_AndTheirsImportsIntoIt_WithoutChangingTheTemplate`, replace

```csharp
        foreach (var other in new[] { "software", "healthcare" }.Concat(Keys.Where(k => k != key)))
```

with

```csharp
        foreach (var other in new[] { "software" }.Concat(Keys.Where(k => k != key)))
```

- [ ] **Step 5: Run the flow checks for every key**

Run: `dotnet test $T --nologo --filter "FullyQualifiedName~ArchetypeTemplateFlowTests"`
Expected: PASS for all gallery, results and healthcare cases (12 methods x 6 keys).

If a **healthcare** case fails, diagnose before touching anything (superpowers:systematic-debugging). The failure is either an assertion that assumed gallery or results markup (fix it the way Steps 2 and 3 did, keyed on `Definition(key).Archetype`) or a real Healthcare behaviour the old test never saw (report it to the user, do not weaken the assertion). If a gallery or results case fails, Steps 2 to 4 changed something they should not have: undo and redo them.

- [ ] **Step 6: Commit**

```bash
git checkout -- src/Portfolio.Web/wwwroot/css/app.css
git add tests/Portfolio.Web.Tests/Templates/ArchetypeTemplateFlowTests.cs
git commit -m "test: run the template flow checks for the Credentials archetype too, with the card marker and next link made archetype-aware"
```

---

### Task 4: Write the Education tests (red)

Everything Education has to satisfy, before any of it exists. Adds the key, the wording, the list updates and the Education-specific checks.

**Files:**
- Modify: `tests/Portfolio.Web.Tests/Templates/CredentialsArchetypeTests.cs`
- Modify: `tests/Portfolio.Web.Tests/Templates/TemplateManifestTests.cs:56,58`
- Modify: `tests/Portfolio.Web.Tests/Admin/OnboardingFlowTests.cs:302-315`

**Interfaces:**
- Consumes: `CredentialsArchetypeTests.Wording`, `Wordings`, `Definition`, `TemplatesDir` (Task 2); `ResultsArchetypeTests.AssertNoLegalFormSuffix(string)` (internal static, already exists).
- Produces: the contract Tasks 5 and 6 satisfy. Strings in `Wordings["education"]` are the exact `copy` and starter strings those tasks must write.

- [ ] **Step 1: Add the Education key and wording**

In `CredentialsArchetypeTests.cs`, change the key list:

```csharp
    public static readonly string[] Keys = ["healthcare", "education"];
```

and add this entry to the `Wordings` dictionary, after the `"healthcare"` entry:

```csharp
        ["education"] = new Wording(
            Home: ["Teaching qualifications &amp; registration", "Teaching experience", "Teaching skills", "Curriculum &amp; initiatives", "References", "Sample profile (fictional)"],
            About: ["Careful planning and inclusive classrooms"],
            Experience: ["Teaching experience", "where I have taught"],
            Projects: ["Curriculum &amp; initiatives", "Read initiative"],
            DetailSlug: "reading-for-pleasure-programme",
            Detail: ["Initiative", "Methods &amp; resources", "Aim", "Approach"],
            Skills: ["Teaching skills", "Pastoral and inclusion", "Technology"],
            Certifications: ["Qualifications, registration &amp; training", "Current", "Qualifications"],
            Cv: ["Teaching experience", "Qualifications &amp; registration", "Curriculum &amp; initiatives"],
            Contact: ["Please do not send pupil information", "teaching roles, mentoring or collaboration"],
            NavLabels: [">about<", ">qualifications<", ">experience<", ">skills<", ">initiatives<", ">cv<"],
            FooterNote: "contains no pupil information",
            MissingBackLink: "← Back to curriculum &amp; initiatives",
            EmptyPages:
            [
                ("/projects", "No initiatives yet"),
                ("/experience", "No roles have been added yet."),
                ("/skills", "No skills have been added yet."),
                ("/certifications", "No qualifications yet"),
            ],
            EmptyHome: "Initiatives coming soon",
            LongTitle: "Advanced Skills Practitioner in Inclusive Education and Learning Support",
            GuidancePhrase: "Never publish pupil-identifiable information",
            Foreign: new Regex(@"(?i)\bclinical\b|\bpatients?\b|\bnurs(?:e|es|ing)\b|\bhealthcare\b|\bhospital\b|\bwards?\b", RegexOptions.Compiled)),
```

- [ ] **Step 2: Add the structural and Education-specific checks**

Append these members inside the `CredentialsArchetypeTests` class (before its closing brace):

```csharp
    // ---------------------------------------------------------------- the manifest, theme, starter and thumbnail

    private static string ThemeCss(string key) => File.ReadAllText(Path.Combine(TemplateDirs.Web, "Styles", "themes", Definition(key).Theme.Default! + ".css"));

    [Theory]
    [MemberData(nameof(KeyData))]
    public void ATemplateOnTheCredentialsArchetype_HasItsThemeStarterAndThumbnail(string key)
    {
        var catalog = new TemplateCatalog(TemplatesDir);
        var template = catalog.Get(key);

        Assert.Equal(key, template.Key);
        Assert.Equal(Archetypes.Credentials, template.Archetype);
        Assert.Contains(Archetypes.Credentials, Archetypes.Implemented);
        Assert.True(template.Sensitive, $"{key} must be sensitive: its content can identify people other than the owner");
        Assert.False(string.IsNullOrWhiteSpace(template.Guidance));
        Assert.True(template.Seo.CredentialsJsonLd, $"{key} must publish held qualifications in its structured data");
        Assert.NotNull(catalog.GetPreviewImagePath(key));
        Assert.NotNull(catalog.GetStarter(key));
        Assert.Empty(TemplateValidator.Validate(template, key));

        var css = ThemeCss(key);
        var theme = template.Theme.Default!;
        Assert.Contains($":root[data-template=\"{theme}\"][data-theme=\"light\"]", css);
        Assert.Contains($":root[data-template=\"{theme}\"][data-theme=\"dark\"]", css);
        foreach (var token in new[] { "--c-bg", "--c-surface", "--c-surface-2", "--c-border", "--c-text", "--c-muted", "--c-accent", "--c-accent-strong" })
            Assert.Equal(2, Regex.Matches(css, Regex.Escape(token) + ":").Count); // once for dark, once for light
    }

    [Theory]
    [MemberData(nameof(KeyData))]
    public void EveryCredentialsPreset_HidesTheTalksYouTubeField_BecauseNoLayoutPlaysVideoForIt(string key)
    {
        Assert.True(Definition(key).Editor.Talks.Fields.TryGetValue("youtubeId", out var field), $"{key} does not mention the youtubeId field");
        Assert.True(field!.Hidden, $"{key} must hide the YouTube field");
    }

    [Theory]
    [MemberData(nameof(KeyData))]
    public void TheStarterContent_IsFictional_UsesOnlyBundledPictures_AndNamesNoPerson(string key)
    {
        var starter = new TemplateCatalog(TemplatesDir).GetStarter(key)!;

        Assert.True(starter.Fictional);
        Assert.Contains("fictional", starter.Profile.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("example.org", starter.Profile.Email);
        Assert.NotEmpty(starter.Projects);
        Assert.Contains(starter.Projects, p => p.Featured);
        Assert.All(starter.Projects, p => Assert.Contains("(sample)", p.Title));
        Assert.All(starter.Experiences, e => Assert.Contains("fictional", e.Company, StringComparison.OrdinalIgnoreCase));
        foreach (var name in starter.Experiences.Select(e => e.Company.Replace("(fictional)", "", StringComparison.OrdinalIgnoreCase).Trim())
                     .Concat(starter.Projects.Select(p => (p.Org ?? "").Replace("(fictional)", "", StringComparison.OrdinalIgnoreCase).Trim())))
            ResultsArchetypeTests.AssertNoLegalFormSuffix(name);
        Assert.All(starter.Projects, p => Assert.All(p.Links, l => Assert.StartsWith("https://example.org/", l.Url)));

        // Pictures: bundled under the template's own folder, described, small, and nothing that needs a font, a script or a download.
        var images = new List<string>();
        foreach (var p in starter.Projects.Where(p => !string.IsNullOrWhiteSpace(p.HeroImage)))
        {
            Assert.False(string.IsNullOrWhiteSpace(p.HeroImageAlt), $"{p.Slug} cover has no description");
            images.Add(p.HeroImage!);
            foreach (var shot in p.Screenshots)
            {
                Assert.False(string.IsNullOrWhiteSpace(shot.Caption), $"{p.Slug} has a picture without a description");
                images.Add(shot.Url);
            }
        }
        foreach (var url in images)
        {
            Assert.StartsWith($"/img/starter/{key}/", url);
            var file = Path.Combine(TemplateDirs.Web, "wwwroot", url.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(file), $"{url} is not bundled under wwwroot");
            Assert.True(new FileInfo(file).Length < 60 * 1024, $"{url} is larger than a starter placeholder should be");
            Assert.DoesNotMatch(@"(?i)<script|<text|<image|https?://(?!www\.w3\.org)", File.ReadAllText(file));
        }
        Assert.Equal(images.Count, images.Distinct().Count());

        // No entry names a pupil, student or child: a heuristic for a capitalised name straight after one of those words.
        var text = File.ReadAllText(Path.Combine(TemplatesDir, key, "starter.json"));
        Assert.DoesNotMatch(@"\b(?:[Pp]upil|[Ss]tudent|[Cc]hild|[Ll]earner)s?\s+(?:named\s+)?[A-Z][a-z]{2,}\b", text);
    }

    // ---------------------------------------------------------------- structured data and editor guidance

    [Theory]
    [MemberData(nameof(KeyData))]
    public async Task OnlyHeldQualifications_AreListedAsCredentials_InThePersonMarkup(string key)
    {
        var starter = new TemplateCatalog(TemplatesDir).GetStarter(key)!;
        using var app = new AdminApp();
        await DemoSite.ApplyStarterAsync(app, key);

        var home = await app.NewClient().GetStringAsync("/");

        Assert.Contains("hasCredential", home);
        var markup = home[home.IndexOf("hasCredential", StringComparison.Ordinal)..];
        markup = markup[..markup.IndexOf("</script>", StringComparison.Ordinal)];
        var held = starter.Certifications.Where(c => c.Status == CertificationStatus.Earned).ToList();
        var notHeld = starter.Certifications.Where(c => c.Status != CertificationStatus.Earned).ToList();
        Assert.NotEmpty(held);
        Assert.NotEmpty(notHeld); // the starter must have a course in progress or planned, or this proves nothing
        foreach (var c in held) Assert.Contains(System.Text.Json.JsonEncodedText.Encode(c.Name).ToString(), markup);
        foreach (var c in notHeld) Assert.DoesNotContain(System.Text.Json.JsonEncodedText.Encode(c.Name).ToString(), markup);
    }

    [Theory]
    [MemberData(nameof(KeyData))]
    public async Task TheGuidance_IsShownOnEveryEditor_OfASensitiveCredentialsTemplate(string key)
    {
        var phrase = Wordings[key].GuidancePhrase;
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app, key);
        await ContentFlow.SeedRowsAsync(app);

        foreach (var path in new[]
                 {
                     "/admin/content", ContentFlow.Experience, ContentFlow.Experience + "/edit", ContentFlow.Skills, ContentFlow.Skills + "/edit",
                     ContentFlow.Projects, ContentFlow.Projects + "/edit", ContentFlow.Certifications, ContentFlow.Certifications + "/edit",
                     ContentFlow.Talks, ContentFlow.Talks + "/edit", ContentFlow.Testimonials, ContentFlow.Testimonials + "/edit",
                     ContentFlow.Blog, ContentFlow.Blog + "/edit",
                 })
        {
            var html = WebUtility.HtmlDecode(await owner.GetStringAsync(path));
            Assert.Contains(phrase, html);
            Assert.Contains("Before you publish", html);
        }
    }

    // ---------------------------------------------------------------- Education

    [Fact]
    public void Education_ListsItsSections_QualificationsFirst_WithArticlesAndWorkshopsOff()
    {
        var template = Definition("education");

        Assert.Equal("School teacher", template.Profession);
        Assert.Equal(new[] { "about", "certifications", "experience", "skills", "projects", "blog", "talks", "testimonials", "contact" }, template.Sections.Select(s => s.Key));
        Assert.Equal(new[] { "blog", "talks" }, template.Sections.Where(s => !s.Enabled).Select(s => s.Key));
        Assert.Equal("qualifications", template.Sections.Single(s => s.Key == "certifications").ShortLabel);
        Assert.Equal("Initiative", template.Sections.Single(s => s.Key == "projects").ItemLabel);
        Assert.Equal(new[] { "Teaching and learning", "Curriculum and assessment", "Pastoral and inclusion", "Technology", "Languages" }, template.SkillGroups);
        Assert.Contains("Never publish pupil-identifiable information", template.Guidance);
    }

    [Fact]
    public async Task Education_MenuFollowsTheSectionOrder()
    {
        using var app = new AdminApp();
        await DemoSite.ApplyStarterAsync(app, "education");

        var nav = (await GetAsync(app.NewClient(), "/")).Html;

        var positions = new[] { ">about<", ">qualifications<", ">experience<", ">skills<", ">initiatives<", ">cv<" }.Select(l => nav.IndexOf(l, StringComparison.Ordinal)).ToList();
        Assert.DoesNotContain(-1, positions);
        Assert.Equal(positions.OrderBy(p => p), positions);
    }

    [Fact]
    public void Education_UsesItsOwnDistinctTheme_NotHealthcares()
    {
        var education = Definition("education");
        var healthcare = Definition("healthcare");

        Assert.NotEqual(healthcare.Theme.Default, education.Theme.Default);
        Assert.NotEqual(healthcare.Theme.Accents.Select(a => a.Key), education.Theme.Accents.Select(a => a.Key));
        Assert.Equal(new[] { "green", "terracotta", "blue", "gold" }, education.Theme.Accents.Select(a => a.Key));
        Assert.NotEqual(healthcare.Theme.ThemeColor, education.Theme.ThemeColor);
    }
```

Note `TemplateDirs.Web` is the existing helper the Results tests use for `src/Portfolio.Web`.

- [ ] **Step 3: Update the folder list and the catalog order**

In `TemplateManifestTests.cs`, replace line 56:

```csharp
        Assert.Equal(new[] { "design-ux", "education", "finance-accounting", "healthcare", "marketing-content", "photography-film", "sales-bd", "software" }, TemplateFolders().Select(Path.GetFileName).OrderBy(x => x).ToArray());
```

and line 58:

```csharp
        Assert.Equal(new[] { "software", "design-ux", "education", "finance-accounting", "healthcare", "marketing-content", "photography-film", "sales-bd" }, catalog.All.Select(t => t.Key));
```

(Keep the exact text after the lines' `Assert.Equal(` that the file already has; only the two arrays change. If the line breaks differ from the above, change only the arrays.)

- [ ] **Step 4: Update the onboarding counts**

In `OnboardingFlowTests.cs`, replace line 302 and add Education assertions before the final count (line 315):

```csharp
        Assert.Equal(7, Regex.Matches(html, "Before you publish").Count); // the two sensitive templates, the two gallery templates and the three results templates carry guidance, software does not
```

```csharp
        Assert.Contains(">School teacher</h2>", html);
        Assert.Contains("src=\"/admin/welcome/thumb/education\"", html);
        Assert.Contains("pupil-identifiable", html);
        Assert.Equal(8, Regex.Matches(html, "type=\"radio\" name=\"Key\"").Count);
```

(The last line replaces the old `Assert.Equal(7, ... radio ...)`.)

- [ ] **Step 5: Run to see them fail for the right reason**

Run: `dotnet test $T --nologo --filter "FullyQualifiedName~CredentialsArchetypeTests|FullyQualifiedName~TemplateManifestTests|FullyQualifiedName~OnboardingFlowTests"`
Expected: FAIL. The Healthcare cases still pass; every `education` case fails with a missing template (for example `KeyNotFoundException`, "does not contain the key", or a missing `Templates/education` folder), and the two list tests and the onboarding test fail on the count or the order. If a **healthcare** case fails, stop: Task 2 or 4 broke it.

- [ ] **Step 6: Commit the red tests**

```bash
git checkout -- src/Portfolio.Web/wwwroot/css/app.css
git add tests/Portfolio.Web.Tests/Templates/CredentialsArchetypeTests.cs tests/Portfolio.Web.Tests/Templates/TemplateManifestTests.cs tests/Portfolio.Web.Tests/Admin/OnboardingFlowTests.cs
git commit -m "test: Education on the Credentials archetype (red until the preset exists)"
```

---

### Task 5: The Education manifest and theme

**Files:**
- Create: `src/Portfolio.Web/Templates/education/template.json`
- Create: `src/Portfolio.Web/Styles/themes/education.css`
- Modify: `src/Portfolio.Web/Styles/app.css` (one `@import` line)

**Interfaces:**
- Consumes: the strings in `Wordings["education"]` (Task 4).
- Produces: the template key `education`, theme default `education`, accent keys `green`, `terracotta`, `blue`, `gold`, and the copy the flow tests read (`Copy.Home.WorkEmptyTitle`, `Copy.Experience.Heading`).

- [ ] **Step 1: Create the manifest**

Create `src/Portfolio.Web/Templates/education/template.json`:

```json
{
  "$schema": "../template.schema.json",
  "key": "education",
  "name": "Education",
  "profession": "School teacher",
  "description": "Qualifications-first layout for school teachers: teaching registration, classroom experience and curriculum work.",
  "archetype": "credentials",
  "sensitive": true,
  "guidance": "Never publish pupil-identifiable information: no names, photographs, work samples that carry a name, behaviour, attendance, special educational needs or safeguarding details, or anything that could identify a pupil, family or colleague. Check your school's and employer's policies on confidentiality and social media before publishing.",
  "theme": {
    "default": "education",
    "themeColor": "#101613",
    "accents": [
      { "key": "green", "label": "Green", "dark": { "accent": "#4ade80", "strong": "#86efac" }, "light": { "accent": "#166534", "strong": "#14532d" } },
      { "key": "terracotta", "label": "Terracotta", "dark": { "accent": "#f4a07a", "strong": "#f9c3a8" }, "light": { "accent": "#a2431f", "strong": "#862f10" } },
      { "key": "blue", "label": "Blue", "dark": { "accent": "#7cb4f5", "strong": "#a9cdf8" }, "light": { "accent": "#1d4ed8", "strong": "#1e40af" } },
      { "key": "gold", "label": "Gold", "dark": { "accent": "#e9c25a", "strong": "#f2d78d" }, "light": { "accent": "#7a5a06", "strong": "#634a05" } }
    ]
  },
  "heroSuggestions": {
    "title": "Primary School Teacher",
    "summary": "Primary school teacher committed to well-planned, inclusive teaching that helps every pupil make progress, with a focus on clear feedback and supporting colleagues."
  },
  "sections": [
    { "key": "about", "label": "about" },
    { "key": "certifications", "label": "teaching qualifications & registration", "shortLabel": "qualifications" },
    { "key": "experience", "label": "teaching experience", "shortLabel": "experience" },
    { "key": "skills", "label": "teaching skills", "shortLabel": "skills" },
    { "key": "projects", "label": "curriculum & initiatives", "shortLabel": "initiatives", "itemLabel": "Initiative" },
    { "key": "blog", "label": "articles", "itemLabel": "Article", "enabled": false },
    { "key": "talks", "label": "workshops", "itemLabel": "Workshop", "enabled": false },
    { "key": "testimonials", "label": "references" },
    { "key": "contact", "label": "contact" }
  ],
  "skillGroups": ["Teaching and learning", "Curriculum and assessment", "Pastoral and inclusion", "Technology", "Languages"],
  "seo": {
    "title": "{name} — {title}",
    "description": "{name}, {title}. Qualifications, teaching experience and professional references.",
    "aboutDescription": "About {name}, {title}: training, registration and approach to teaching and learning.",
    "credentialsJsonLd": true
  },
  "editor": {
    "experience": {
      "itemLabel": "Position",
      "fields": {
        "company": { "label": "School or employer" },
        "title": { "label": "Role" },
        "summary": { "hint": "Describe your responsibilities and outcomes in general terms. Never include pupil information." },
        "highlights": { "label": "Key responsibilities", "hint": "One per line, up to 12. No pupil details." },
        "tech": { "label": "Resources and programmes" }
      }
    },
    "skills": {
      "itemLabel": "Teaching skill",
      "fields": {
        "name": { "label": "Skill or competency" }
      }
    },
    "projects": {
      "fields": {
        "org": { "label": "School or organisation" },
        "problem": { "label": "Aim", "hint": "Why the work was needed. Plain text. No pupil information." },
        "approach": { "label": "What was done", "hint": "Plain text. No pupil information." },
        "impact": { "label": "Outcomes", "hint": "One outcome per line, up to 8. Whole-class or whole-school figures only, never individual pupils." },
        "tech": { "label": "Methods and resources" },
        "category": { "hint": "A type such as Curriculum or Enrichment. Used by the filter on the listing page." }
      }
    },
    "certifications": {
      "itemLabel": "Qualification or registration",
      "fields": {
        "name": { "label": "Qualification or registration" },
        "issuer": { "label": "Awarding or registering body" },
        "credentialUrl": { "label": "Register entry link", "hint": "Where someone can check the registration (http or https)." },
        "badgeImageUrl": { "hidden": true }
      }
    },
    "talks": {
      "itemLabel": "Workshop",
      "fields": {
        "title": { "label": "Workshop title", "hint": "Plain text. No pupil information." },
        "event": { "label": "Event or training day" },
        "typeVideo": { "label": "Recording" },
        "typeTalk": { "label": "Live session" },
        "youtubeId": { "hidden": true }
      }
    },
    "testimonials": {
      "itemLabel": "Reference",
      "fields": {
        "quote": { "label": "Reference text", "hint": "Plain text. Only with the referee's permission, and never any pupil information." },
        "author": { "label": "Referee" },
        "authorTitle": { "label": "Referee's role and school" }
      }
    },
    "blog": {
      "itemLabel": "Article",
      "fields": {
        "title": { "label": "Article title", "hint": "Plain text. No pupil information." },
        "body": { "label": "Article text", "hint": "Markdown. Raw HTML is shown as text, not run. Describe classroom examples in general terms only. Never include pupil information." },
        "cover": { "hint": "JPEG, PNG or WebP. Never use a photograph that could identify a pupil, family or colleague." }
      }
    },
    "hero": {
      "fields": {
        "heading": { "label": "Home page introduction", "hint": "The first thing visitors read. Write about your teaching in general terms. Never include pupil information. Changes apply as soon as you save." },
        "eyebrow": { "label": "Role line", "hint": "For example your phase and subject. Plain text." },
        "headlineAccent": { "hidden": true },
        "lede": { "label": "Professional summary" }
      }
    },
    "metrics": {
      "itemLabel": "Figure",
      "fields": {
        "heading": { "label": "Key figures", "hint": "General figures about your teaching, such as years of experience. Never figures about individual pupils. Changes apply as soon as you save." },
        "unit": { "hidden": true },
        "label": { "label": "What it measures" }
      }
    },
    "sections": {
      "itemLabel": "Page",
      "fields": {
        "heading": { "label": "Pages of your site", "hint": "Choose which pages the site has, what the menu calls them and in which order. Articles and workshops are off until you switch them on. Hiding a page never deletes it. Saves apply at once." },
        "label": { "label": "Menu name" },
        "defaultOff": { "label": "Off until you switch it on", "hint": "Not shown by default for this profession. Think about what you would publish there first." }
      }
    },
    "about": {
      "itemLabel": "Highlight",
      "fields": {
        "heading": { "label": "About you", "hint": "Describe your teaching and values. Never include pupil information. Changes apply as soon as you save." },
        "valueProps": { "label": "Highlights", "hint": "Short statements about how you teach, shown above the text." },
        "currentlyNote": { "label": "Current role or interest" },
        "focus": { "label": "Teaching focus", "hint": "One area per line." }
      }
    },
    "transfer": {
      "itemLabel": "Entry",
      "fields": {
        "heading": { "label": "Save and restore your portfolio", "hint": "Keep your portfolio in one file, or restore one. You see what would change first, and nothing is ever removed. Never put pupil information in an entry." },
        "exportHeading": { "label": "Save a copy", "hint": "One file with your details and every entry, including unpublished ones. Pictures you uploaded are listed, not included." },
        "exportButton": { "label": "Download my portfolio file" },
        "importHeading": { "label": "Restore from a file", "hint": "Each entry in the file is matched to one of yours by its name or title: a match is updated, anything else is added." },
        "fileLabel": { "label": "Portfolio file (.json)" },
        "applyProfile": { "label": "Also restore my details and page choices", "hint": "Left unticked, only the entries are restored. Ticked, your details, introduction, key figures, about text, template and page choices come from the file; the preview lists each change." },
        "previewButton": { "label": "Check the file" },
        "confirmButton": { "label": "Restore now" },
        "discardButton": { "label": "Cancel" },
        "profile": { "label": "Details" },
        "authoritative": { "label": "Your content is rebuilt at every start", "hint": "This site refills its content from its seed files whenever it starts, so what you restore would be replaced. Ask whoever runs the site to change that setting first." }
      }
    }
  },
  "starter": { "file": "starter.json" },
  "copy": {
    "layout": { "footerNote": "Professional portfolio. It contains no pupil information." },
    "home": {
      "workEyebrow": "Curriculum & initiatives",
      "workHeading": "Curriculum & initiatives",
      "workEmptyTitle": "Initiatives coming soon",
      "workEmptyBody": "Add the curriculum, enrichment or improvement projects you have led or contributed to. Describe the work and its outcome, never individual pupils.",
      "testimonialsEyebrow": "References",
      "testimonialsHeading": "What colleagues say",
      "contactHeading": "Get in touch about teaching roles, mentoring or collaboration.",
      "experienceEyebrow": "Classroom",
      "experienceHeading": "Teaching experience",
      "credentialsEyebrow": "Qualifications",
      "credentialsHeading": "Teaching qualifications & registration",
      "skillsHeading": "Teaching skills"
    },
    "projects": {
      "title": "Initiatives",
      "description": "Curriculum, enrichment and improvement initiatives: the aim, the approach and the outcome.",
      "eyebrow": "Curriculum & initiatives",
      "heading": "Curriculum & initiatives",
      "lead": "Projects led or contributed to: the aim, what was done and the outcome.",
      "emptyTitle": "No initiatives yet",
      "emptyBody": "Add your first initiative. Describe the work and its outcome, never individual pupils.",
      "filterLabel": "Filter initiatives by area",
      "featuredBadge": "featured",
      "problemHeading": "Aim",
      "approachHeading": "Approach",
      "noWriteup": "A full description of this initiative is on the way.",
      "stackHeading": "Methods & resources",
      "linksHeading": "Links",
      "proofHeading": "Evidence",
      "screenshotsHeading": "Images",
      "imageAlt": "{title} image",
      "galleryLabel": "Images",
      "openImageLabel": "Open image"
    },
    "experience": {
      "title": "Experience",
      "description": "{name}'s teaching experience: schools, roles and responsibilities, most recent first.",
      "eyebrow": "Classroom",
      "heading": "Teaching experience",
      "lead": "Most recent first: where I have taught, the responsibilities I held and what I contributed.",
      "empty": "No roles have been added yet."
    },
    "skills": {
      "title": "Skills",
      "description": "Teaching skills grouped by area: teaching and learning, curriculum, pastoral care, technology and languages.",
      "eyebrow": "Skills",
      "heading": "Teaching skills",
      "lead": "Grouped by area: what I am trained and confident to do.",
      "empty": "No skills have been added yet."
    },
    "certifications": {
      "title": "Qualifications",
      "description": "Teaching qualifications, registration and training: current, in progress and planned.",
      "eyebrow": "Qualifications",
      "heading": "Qualifications, registration & training",
      "lead": "Current registration and qualifications, training under way and planned professional development.",
      "emptyTitle": "No qualifications yet",
      "emptyBody": "Add your teaching registration, degrees and mandatory training.",
      "filterLabel": "Filter by awarding body",
      "statusEarned": "Current",
      "statusInProgress": "In progress",
      "statusPlanned": "Planned",
      "viewCertificate": "View certificate",
      "viewCredential": "Verify registration →"
    },
    "blog": {
      "title": "Articles",
      "description": "Articles on teaching, learning and professional development.",
      "eyebrow": "Articles",
      "heading": "Writing",
      "lead": "Reflections on teaching and professional development.",
      "empty": "Articles coming soon."
    },
    "talks": {
      "title": "Workshops",
      "description": "Workshops and training sessions I have led.",
      "eyebrow": "Workshops",
      "heading": "Coming soon",
      "empty": "No workshops listed yet. Check back soon.",
      "watch": "Watch →"
    },
    "contact": {
      "description": "Contact {name}: email, phone or LinkedIn.",
      "heading": "Get in touch about teaching roles, mentoring or collaboration.",
      "lead": "The best ways to reach me. Please do not send pupil information."
    },
    "cv": {
      "description": "Curriculum vitae — {name}, {title}.",
      "experienceHeading": "Teaching experience",
      "skillsHeading": "Teaching skills",
      "projectsHeading": "Curriculum & initiatives",
      "certificationsHeading": "Qualifications & registration"
    }
  }
}
```

- [ ] **Step 2: Create the theme**

Create `src/Portfolio.Web/Styles/themes/education.css`. Only variables, no class selectors (a test rejects them). Every value was checked: dark text/accent on surfaces at least 6.75:1, light at least 5.87:1, and the four accent choices at least 5.30:1.

```css
/* Education theme: an ink-green dark mode (a warm cream in light) with a deep green accent, a humanist serif for headings and the system sans for everything else.
   Only variables are set here (colours as RGB channels like the base tokens in app.css, plus fonts and radii), so the
   whole site switches look with the data-template attribute and no rebuild. Specificity beats the base :root tokens. */
:root[data-template="education"] {
  --font-sans: ui-sans-serif, system-ui, -apple-system, "Segoe UI", Roboto, Helvetica, Arial, sans-serif;
  --font-display: Charter, "Bitstream Charter", "Iowan Old Style", "Palatino Linotype", Palatino, Georgia, "Times New Roman", serif;
  --font-mono: ui-sans-serif, system-ui, -apple-system, "Segoe UI", Roboto, Helvetica, Arial, sans-serif;
  --radius-lg: 0.75rem;
  --radius-md: 0.5rem;
}

/* Dark is the default look (the base tokens do the same on :root), so it also applies before the theme script has set data-theme or when it cannot run. */
:root[data-template="education"],
:root[data-template="education"][data-theme="dark"] {
  --c-bg: 16 22 19;
  --c-surface: 23 31 27;
  --c-surface-2: 31 42 36;
  --c-border: 52 68 59;
  --c-text: 240 238 228;
  --c-muted: 166 178 168;
  --c-accent: 110 200 150;
  --c-accent-strong: 156 224 187;
}

:root[data-template="education"][data-theme="light"] {
  --c-bg: 250 247 238;
  --c-surface: 255 253 247;
  --c-surface-2: 241 236 222;
  --c-border: 221 213 192;
  --c-text: 28 36 31;
  --c-muted: 84 92 82;
  --c-accent: 26 102 66;
  --c-accent-strong: 18 78 49;
}
```

- [ ] **Step 3: Import the theme**

In `src/Portfolio.Web/Styles/app.css`, add the import in alphabetical position, between the `design-ux` and `finance-accounting` lines:

```css
@import "./themes/design-ux.css";
@import "./themes/education.css";
@import "./themes/finance-accounting.css";
```

- [ ] **Step 4: Run the manifest and theme checks**

Run: `dotnet test $T --nologo --filter "FullyQualifiedName~TemplateManifestTests|FullyQualifiedName~TemplateOnboardingManifestTests|FullyQualifiedName~ThemeStylesheetTests|FullyQualifiedName~ATemplateOnTheCredentialsArchetype|FullyQualifiedName~Education_|FullyQualifiedName~EveryCredentialsPreset|FullyQualifiedName~EveryAccentTone"`

Expected: the manifest validity, folder list, catalog order, schema drift, accent contrast, theme contrast, theme-import and `Education_*` checks PASS. Still failing and expected to: `EveryShippedTemplate_HasA1200x800PngThumbnail` and `ATemplateOnTheCredentialsArchetype_HasItsThemeStarterAndThumbnail` (no `preview.png` or `starter.json` yet). `ThemeStylesheetTests` may report the committed `wwwroot/css/app.css` lacks the education rules if it has a coverage check: that is resolved in Task 8.

If the validator rejects a string (a length limit or a token), shorten it and keep the meaning; do not weaken the validator. If a `SoftwareWording` match shows up in a later rendering test, the offending word is in this file: reword it.

- [ ] **Step 5: Commit**

```bash
git checkout -- src/Portfolio.Web/wwwroot/css/app.css
git add src/Portfolio.Web/Templates/education/template.json src/Portfolio.Web/Styles/themes/education.css src/Portfolio.Web/Styles/app.css
git commit -m "feat: Education template manifest and theme on the Credentials layout"
```

---

### Task 6: The Education starter content and pictures

**Files:**
- Create: `src/Portfolio.Web/Templates/education/starter.json`
- Create: `src/Portfolio.Web/wwwroot/img/starter/education/reading-for-pleasure-programme.svg`
- Create: `src/Portfolio.Web/wwwroot/img/starter/education/reading-for-pleasure-programme-1.svg`
- Create: `src/Portfolio.Web/wwwroot/img/starter/education/mathematics-mastery-pilot.svg`
- Create: `src/Portfolio.Web/wwwroot/img/starter/education/outdoor-learning-day.svg`

**Interfaces:**
- Consumes: `Wordings["education"].DetailSlug == "reading-for-pleasure-programme"`, the About headline text, and the wording the home page must show.
- Produces: starter rows. Held qualifications: four. Not held: one in progress, one planned.

- [ ] **Step 1: Generate the four pictures**

Save this script outside the repo (for example in the session scratchpad as `gen_education_svgs.py`) and run it with the output folder: `python gen_education_svgs.py src/Portfolio.Web/wwwroot/img/starter/education`. The pictures are abstract, contain no text, no script and no link, and are each under 3 KB.

```python
import os, sys

BG, PANEL, TILE = "#101613", "#17201b", "#25332b"
GREEN, CREAM, TERRA, GOLD, SKY = "#6ec896", "#f2efe3", "#f4a07a", "#e9c25a", "#7cb4f5"

def wrap(body):
    return (f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1200 750" width="1200" height="750">'
            f'<rect width="1200" height="750" fill="{BG}"/><rect x="60" y="60" width="1080" height="630" rx="24" fill="{PANEL}"/>{body}</svg>')

def rect(x, y, w, h, fill, rx=8):
    return f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="{rx}" fill="{fill}"/>'

# Cover: two shelves of books.
body = ""
colours = [GREEN, TERRA, GOLD, SKY, CREAM]
for row, y0 in enumerate((130, 360)):
    x = 140
    for i in range(11):
        h = 150 + ((i * 37 + row * 53) % 70)
        w = 54 + ((i * 13) % 22)
        body += rect(x, y0 + (220 - h), w, h, colours[(i + row) % 5], 6)
        x += w + 14
    body += rect(120, y0 + 226, 960, 16, TILE)
shelf = wrap(body)

# Picture: an open book.
body = (f'<path d="M600 190 C520 150 400 150 300 180 L300 560 C400 530 520 530 600 570 Z" fill="{CREAM}"/>'
        f'<path d="M600 190 C680 150 800 150 900 180 L900 560 C800 530 680 530 600 570 Z" fill="{GREEN}"/>')
for k in range(5):
    y = 250 + k * 50
    body += rect(350, y, 190, 14, TILE, 7) + rect(660, y, 190, 14, CREAM, 7)
body += f'<rect x="596" y="190" width="8" height="380" fill="{BG}"/>'
book = wrap(body)

# Cover: a grid of tiles beside rising bars.
body = ""
highlighted = {(1, 1), (3, 2), (5, 0), (2, 3), (6, 3), (4, 1)}
for r in range(4):
    for c in range(8):
        body += rect(130 + c * 86, 150 + r * 86, 72, 72, GOLD if (c, r) in highlighted else TILE, 12)
for i in range(5):
    h = 70 + i * 55
    body += rect(860 + i * 50, 560 - h, 38, h, GREEN if i < 4 else TERRA, 8)
body += rect(840, 570, 270, 10, CREAM, 5)
maths = wrap(body)

# Cover: hills, a low sun and two trees.
body = (f'<circle cx="930" cy="230" r="80" fill="{GOLD}"/>'
        f'<path d="M60 560 C300 420 520 470 700 540 C860 470 1000 440 1140 520 L1140 666 Q1140 690 1116 690 L84 690 Q60 690 60 666 Z" fill="{GREEN}"/>'
        f'<path d="M60 620 C260 540 460 580 640 630 C820 570 980 560 1140 600 L1140 666 Q1140 690 1116 690 L84 690 Q60 690 60 666 Z" fill="#3f8f64"/>'
        + rect(300, 360, 26, 150, TERRA, 6)
        + f'<circle cx="313" cy="330" r="82" fill="{CREAM}"/><circle cx="270" cy="360" r="52" fill="{CREAM}"/><circle cx="356" cy="360" r="52" fill="{CREAM}"/>'
        + rect(520, 420, 22, 100, TERRA, 6) + f'<circle cx="531" cy="395" r="56" fill="{SKY}"/>')
outdoor = wrap(body)

out = sys.argv[1]
os.makedirs(out, exist_ok=True)
for name, svg in (("reading-for-pleasure-programme", shelf), ("reading-for-pleasure-programme-1", book),
                  ("mathematics-mastery-pilot", maths), ("outdoor-learning-day", outdoor)):
    # A cheap well-formedness check: one root element, balanced angle brackets, nothing that needs a font, a script or a download.
    assert svg.startswith("<svg ") and svg.endswith("</svg>") and svg.count("<") == svg.count(">"), name
    assert "<text" not in svg and "<script" not in svg and "http" not in svg.replace("http://www.w3.org/2000/svg", ""), name
    with open(os.path.join(out, name + ".svg"), "w", newline="\n") as f:
        f.write(svg)
    print(name, len(svg), "bytes")
```

Expected output: four lines, sizes 1869, 1156, 2836 and 875 bytes.

- [ ] **Step 2: Create the starter**

Create `src/Portfolio.Web/Templates/education/starter.json` (comments are allowed in starter files, as in the others):

```json
// Fictional sample content for the Education template. Every person, school, awarding body and
// qualification below is invented. Nothing here describes a real person, school or pupil.
{
  "fictional": true,
  "profile": {
    "ownerName": "Taylor Ashdown",
    "brandName": "Taylor",
    "role": "Primary School Teacher · Upper primary (sample profile)",
    "jobTitle": "Primary School Teacher",
    "summary": "Sample profile (fictional). Primary school teacher with nine years across upper primary classes, experienced in curriculum planning, inclusive teaching, assessment and mentoring student teachers.",
    "knowsAbout": ["Primary education", "Curriculum planning", "Inclusive teaching", "Formative assessment", "Mentoring student teachers"],
    "hero": {
      "eyebrow": "Primary School Teacher · Upper primary",
      "headline": "Well-planned lessons",
      "headlineAccent": "where every learner can succeed.",
      "lede": "Sample profile (fictional). Nine years in upper primary classrooms: careful planning, clear feedback and a calm, inclusive approach."
    },
    "metrics": [
      { "value": "9", "unit": "years", "label": "teaching" },
      { "value": "3", "unit": "", "label": "schools" },
      { "value": "2", "unit": "", "label": "curriculum initiatives led" },
      { "value": "40+", "unit": "hours", "label": "mentoring student teachers" }
    ],
    "about": {
      "headline": "Careful planning and inclusive classrooms, one lesson at a time.",
      "paragraphs": [
        "Sample profile (fictional). I am a primary school teacher working with upper primary classes. My practice is built on careful planning, clear and kind feedback, and classrooms where every learner can take part.",
        "I enjoy sharing what works. I have supported student teachers through their placements and led two small curriculum initiatives, one on reading for pleasure and one on mathematics mastery.",
        "This page is sample content for the Education template. Replace it with your own details, and never add anything that could identify a pupil."
      ],
      "valueProps": [
        { "title": "Inclusive by design", "body": "Lessons planned so that every learner can access the learning and be challenged by it." },
        { "title": "Clear feedback", "body": "Short, specific next steps that learners understand and act on." },
        { "title": "Sharing practice", "body": "Mentoring student teachers and sharing planning with colleagues." },
        { "title": "Working with families", "body": "Open, respectful communication that keeps families informed." }
      ],
      "currentlyNote": "Studying for a postgraduate certificate in inclusive education while mentoring two student teachers.",
      "focus": ["Inclusive teaching", "Reading and writing", "Mathematics mastery", "Mentoring student teachers"]
    },
    "email": "taylor.sample@example.org",
    "linkedIn": "https://www.linkedin.com/in/example-taylor-ashdown/",
    "templateKey": "education"
  },
  "experiences": [
    {
      "company": "Example Primary School (fictional)",
      "title": "Class Teacher, Upper Primary",
      "start": "2022",
      "end": null,
      "current": true,
      "summary": "Teaching a mixed-ability upper primary class across the full curriculum, with responsibility for reading across the school.",
      "highlights": [
        "Led the school's reading for pleasure programme",
        "Mentor to two student teachers each year",
        "Contributed to the school improvement plan for English"
      ],
      "tech": [],
      "sortOrder": 1
    },
    {
      "company": "Sample Hill Academy (fictional)",
      "title": "Class Teacher and Mathematics Lead",
      "start": "2019",
      "end": "2022",
      "current": false,
      "summary": "Taught a lower upper primary class and led mathematics for the year group.",
      "highlights": [
        "Piloted a mastery approach to number across the year group",
        "Ran termly staff training on planning and assessment"
      ],
      "tech": [],
      "sortOrder": 2
    },
    {
      "company": "Demo Valley Junior School (fictional)",
      "title": "Newly Qualified Teacher",
      "start": "2017",
      "end": "2019",
      "current": false,
      "summary": "Induction programme with a class of her own from the first term, supported by a mentor and a reduced timetable.",
      "highlights": ["Completed the induction programme on schedule"],
      "tech": [],
      "sortOrder": 3
    }
  ],
  "skills": [
    { "name": "Lesson planning", "group": "Teaching and learning", "sortOrder": 1 },
    { "name": "Differentiation", "group": "Teaching and learning", "sortOrder": 2 },
    { "name": "Questioning and feedback", "group": "Teaching and learning", "sortOrder": 3 },
    { "name": "Classroom routines", "group": "Teaching and learning", "sortOrder": 4 },
    { "name": "Guided reading", "group": "Curriculum and assessment", "sortOrder": 1 },
    { "name": "Mathematics mastery", "group": "Curriculum and assessment", "sortOrder": 2 },
    { "name": "Formative assessment", "group": "Curriculum and assessment", "sortOrder": 3 },
    { "name": "Moderation", "group": "Curriculum and assessment", "sortOrder": 4 },
    { "name": "Inclusive practice", "group": "Pastoral and inclusion", "sortOrder": 1 },
    { "name": "Behaviour for learning", "group": "Pastoral and inclusion", "sortOrder": 2 },
    { "name": "Working with families", "group": "Pastoral and inclusion", "sortOrder": 3 },
    { "name": "Safeguarding practice", "group": "Pastoral and inclusion", "sortOrder": 4 },
    { "name": "Interactive whiteboard", "group": "Technology", "sortOrder": 1 },
    { "name": "Digital learning platform", "group": "Technology", "sortOrder": 2 },
    { "name": "Online assessment tools", "group": "Technology", "sortOrder": 3 },
    { "name": "English (native)", "group": "Languages", "sortOrder": 1 },
    { "name": "French (conversational)", "group": "Languages", "sortOrder": 2 }
  ],
  "projects": [
    {
      "title": "Reading for pleasure programme (sample)",
      "slug": "reading-for-pleasure-programme",
      "role": "Lead",
      "org": "Example Primary School (fictional)",
      "period": "2024",
      "summary": "A whole-school programme to make reading for pleasure part of every week. Sample initiative (fictional).",
      "category": "Curriculum",
      "problem": "Reading for pleasure was patchy between classes, and few books went home.",
      "approach": "Refreshed the class libraries, timetabled a weekly reading session, trained colleagues to recommend books and run short discussions, and started a simple borrowing record.",
      "impact": ["Weekly borrowing rose from about half the school to nearly nine in ten", "The weekly session is now timetabled in every class"],
      "tech": ["Class libraries", "Reading records", "Staff training"],
      "heroImage": "/img/starter/education/reading-for-pleasure-programme.svg",
      "heroImageAlt": "Abstract illustration of two shelves of colourful books.",
      "screenshots": [
        { "url": "/img/starter/education/reading-for-pleasure-programme-1.svg", "caption": "An open book, the emblem the programme used on its posters" }
      ],
      "featured": true,
      "sortOrder": 1
    },
    {
      "title": "Mathematics mastery pilot (sample)",
      "slug": "mathematics-mastery-pilot",
      "role": "Contributor",
      "org": "Sample Hill Academy (fictional)",
      "period": "2022",
      "summary": "A year-group pilot of a mastery approach to number, shared with the rest of the school. Sample initiative (fictional).",
      "category": "Curriculum",
      "problem": "Number fluency varied widely and teaching approaches differed between classes.",
      "approach": "Agreed a shared sequence of small steps, planned lessons together, used short fluency checks and reviewed the results each half term.",
      "impact": ["Fluency check scores rose across the year group", "The approach was shared at a whole-staff training day"],
      "tech": ["Fluency checks", "Shared planning", "Concrete resources"],
      "heroImage": "/img/starter/education/mathematics-mastery-pilot.svg",
      "heroImageAlt": "Abstract illustration of a grid of tiles with a few highlighted, beside a rising set of bars.",
      "featured": true,
      "sortOrder": 2
    },
    {
      "title": "Outdoor learning day (sample)",
      "slug": "outdoor-learning-day",
      "role": "Organiser",
      "org": "Example Primary School (fictional)",
      "period": "2025",
      "summary": "A termly day of cross-curricular lessons held outdoors. Sample initiative (fictional).",
      "category": "Enrichment",
      "problem": "Few lessons took place outdoors, and some topics were hard to bring to life in a classroom.",
      "approach": "Mapped outdoor tasks to the curriculum, completed risk assessments with the leadership team and trained colleagues to lead each station.",
      "impact": ["Every class took part in the first two days", "Staff feedback asked for the day to become a termly event"],
      "tech": ["Risk assessment", "Curriculum mapping", "Station planning"],
      "heroImage": "/img/starter/education/outdoor-learning-day.svg",
      "heroImageAlt": "Abstract illustration of green hills, a low sun and two trees.",
      "featured": false,
      "sortOrder": 3
    }
  ],
  "certifications": [
    { "name": "Qualified Teacher registration (sample)", "issuer": "Example Teaching Council", "status": "Earned", "date": "2017", "category": "Registration", "sortOrder": 1 },
    { "name": "Bachelor of Education (sample)", "issuer": "Example University College", "status": "Earned", "date": "2016", "category": "Degree", "sortOrder": 2 },
    { "name": "Safeguarding and child protection training", "issuer": "Example Local Authority", "status": "Earned", "date": "2025", "category": "Mandatory training", "sortOrder": 3 },
    { "name": "Paediatric first aid", "issuer": "Example First Aid Training", "status": "Earned", "date": "2024", "category": "Mandatory training", "sortOrder": 4 },
    { "name": "Postgraduate Certificate in Inclusive Education (sample)", "issuer": "Example Institute of Education", "status": "InProgress", "date": "2026", "category": "Postgraduate", "sortOrder": 5 },
    { "name": "Mentor and Coaching Programme (sample)", "issuer": "Example Institute of Education", "status": "Planned", "category": "Professional development", "sortOrder": 6 }
  ],
  "testimonials": [
    {
      "quote": "Sample reference (fictional). Taylor plans with great care and is a calm, encouraging colleague; student teachers ask to be placed in their class.",
      "author": "Sample Colleague",
      "authorTitle": "Head Teacher, Example Primary School",
      "featured": true,
      "sortOrder": 1
    },
    {
      "quote": "Sample reference (fictional). Their mathematics pilot changed how our whole year group teaches number, and they shared it generously.",
      "author": "Demo Colleague",
      "authorTitle": "Deputy Head, Sample Hill Academy",
      "featured": true,
      "sortOrder": 2
    }
  ]
}
```

- [ ] **Step 3: Run the starter and rendering checks**

Run: `dotnet test $T --nologo --filter "FullyQualifiedName~CredentialsArchetypeTests"`
Expected: all `education` cases PASS except `ATemplateOnTheCredentialsArchetype_HasItsThemeStarterAndThumbnail` (no thumbnail yet; it fails at `GetPreviewImagePath`). All healthcare cases still PASS.

Likely first-run failures and what they mean:
- `SoftwareWording` or `Foreign` match in a rendering check: the matched word is in the starter or manifest; reword it (never loosen the regex).
- `alt=` assertion in `ProjectImages_UseNeutralWording`: the test clears `HeroImageAlt`; if it still fails the home or list page is not using the template's `imageAlt` fallback, which is a real finding to report.
- `OnlyHeldQualifications...`: the held names must appear in the JSON-LD and the two unheld ones must not. A name with a character the JSON encoder escapes (`&`, `<`, `>`, `'`, `+`) is compared in its escaped form by the test, so the names above are safe.

- [ ] **Step 4: Commit**

```bash
git checkout -- src/Portfolio.Web/wwwroot/css/app.css
git add src/Portfolio.Web/Templates/education/starter.json src/Portfolio.Web/wwwroot/img/starter/education
git commit -m "feat: Education starter content and generated starter pictures"
```

---

### Task 7: The thumbnail

A real 1200 x 800 dark-mode screenshot of the rendered home page with the sample content, as for every other template.

**Files:**
- Create: `src/Portfolio.Web/Templates/education/preview.png`

**Interfaces:**
- Produces: `Templates/education/preview.png`, found by `TemplateCatalog.GetPreviewImagePath("education")` and served at `/admin/welcome/thumb/education`.

- [ ] **Step 1: Export the branch and run it**

The export is LF, like CI. From the worktree root (use the scratchpad folder named in the environment block for `EXPORT`):

```bash
EXPORT="<scratchpad>/edu-export"
rm -rf "$EXPORT" && mkdir -p "$EXPORT"
git archive HEAD | tar -x -C "$EXPORT"
cd "$EXPORT"
dotnet build src/Portfolio.Web -c Release --nologo
```

Start the site with a scratch database and a setup key (the README's LocalDB is the default; use any scratch database name, and a random key of 16 or more characters that is never written to a file in the repo):

```bash
ConnectionStrings__Default="Server=(localdb)\\MSSQLLocalDB;Database=EducationThumbScratch;Trusted_Connection=True" \
Setup__Key="<16+ random characters>" ASPNETCORE_ENVIRONMENT=Development \
dotnet run --project src/Portfolio.Web -c Release --no-build --urls https://localhost:7239
```

Run it in the background; do not poll its output while waiting.

- [ ] **Step 2: Publish the sample site through the wizard**

Open the site with the Playwright browser tools. Claim the site as `docs/new-user-guide.md` section 3 describes (the `/setup` page takes the key), then in the wizard choose **Education**, enter any name, choose **sample content** on the review step and publish.

- [ ] **Step 3: Take the screenshot**

With the browser tools: set the colour scheme to dark (`browser_emulate_media`), resize to 1200 x 800 (`browser_resize`), navigate to `/` as a visitor (a fresh context, not the owner session, so no preview banner), wait for the page to settle, and save a viewport screenshot (not full page) as `src/Portfolio.Web/Templates/education/preview.png` in the worktree.

Look at the picture (open it with the Read tool). It must show the Education home page: the hero, the qualifications strip and the dark green theme. Retake it if it shows a banner, a cookie dialog, a scrollbar or a partly loaded page.

- [ ] **Step 4: Check the file, then stop the site and drop the scratch database**

Run: `dotnet test $T --nologo --filter "FullyQualifiedName~HasA1200x800PngThumbnail|FullyQualifiedName~ATemplateOnTheCredentialsArchetype"`
Expected: PASS. The file should be well under 600 KB (`ls -l src/Portfolio.Web/Templates/education/preview.png`); if not, retake at the same size with a smaller viewport content (fewer images in view), never rescale.

Stop the background site. Drop the scratch database: `sqllocaldb` or `sqlcmd -S "(localdb)\MSSQLLocalDB" -Q "DROP DATABASE EducationThumbScratch"`.

- [ ] **Step 5: Commit**

```bash
git add src/Portfolio.Web/Templates/education/preview.png
git commit -m "feat: Education thumbnail"
```

---

### Task 8: Regenerate the committed stylesheet from an LF export

CI builds the stylesheet and runs `git diff --exit-code` on `wwwroot/css/app.css`, so the committed file must be exactly the LF build output.

**Files:**
- Modify: `src/Portfolio.Web/wwwroot/css/app.css`

- [ ] **Step 1: Build from a fresh LF export of HEAD**

```bash
EXPORT="<scratchpad>/edu-css"
rm -rf "$EXPORT" && mkdir -p "$EXPORT"
git archive HEAD | tar -x -C "$EXPORT"
(cd "$EXPORT" && dotnet build src/Portfolio.Web -c Release --nologo -p:TailwindRequired=true)
cp "$EXPORT/src/Portfolio.Web/wwwroot/css/app.css" src/Portfolio.Web/wwwroot/css/app.css
```

Expected: the build succeeds with `TailwindRequired=true` (a failed Tailwind download is an error, not a silent fallback).

- [ ] **Step 2: Prove the only change is the Education theme**

Run: `git diff -U0 -- src/Portfolio.Web/wwwroot/css/app.css | grep '^[-+]' | grep -v '^+++\|^---' | grep -vi 'education'`
Expected: no output. Every added line mentions the `education` theme. If other lines differ, the export is not LF or a Tailwind version differs: do not commit; investigate (compare with `git show HEAD:src/Portfolio.Web/wwwroot/css/app.css`).

Run: `git diff --stat -- src/Portfolio.Web/wwwroot/css/app.css`
Expected: a small insertion count (the three theme blocks), no deletions apart from lines that a merged selector list changes.

- [ ] **Step 3: Run the stylesheet checks**

Run: `dotnet test $T --nologo --filter "FullyQualifiedName~ThemeStylesheetTests"`
Expected: PASS. Then **undo the local build's CRLF rewrite** that this run caused, but keep the intended file: the test run rebuilds `app.css` in the worktree, so re-copy from the export and re-check:

```bash
cp "$EXPORT/src/Portfolio.Web/wwwroot/css/app.css" src/Portfolio.Web/wwwroot/css/app.css
git diff --stat -- src/Portfolio.Web/wwwroot/css/app.css
```

- [ ] **Step 4: Commit**

```bash
git add src/Portfolio.Web/wwwroot/css/app.css
git commit -m "build: regenerate the committed stylesheet from an LF export, with the Education theme"
```

From here on, run tests only in an LF export (Task 9) or restore the file with `git checkout -- src/Portfolio.Web/wwwroot/css/app.css` after any local run.

---

### Task 9: Full verification on an LF export, and the before and after render

**Files:** none changed (a scratch test is copied into two throwaway exports).

- [ ] **Step 1: Run the whole suite the way CI builds it**

```bash
EXPORT="<scratchpad>/edu-full"
rm -rf "$EXPORT" && mkdir -p "$EXPORT"
git archive HEAD | tar -x -C "$EXPORT"
cd "$EXPORT" && dotnet test Portfolio.slnx --nologo -p:TailwindRequired=true
```

Run it in the background with a 10-minute timeout; it takes several minutes. Then check the committed stylesheet is unchanged by the build:

```bash
cd <worktree root>
diff <(git show HEAD:src/Portfolio.Web/wwwroot/css/app.css) "$EXPORT/src/Portfolio.Web/wwwroot/css/app.css" && echo "app.css matches the build"
```

Expected: `Passed!` with 0 failed and 0 skipped (the previous head passed 2388 tests; this branch adds the keyed Credentials and Education cases and the flow cases for two more keys, so the total is higher), the build warning count unchanged at 16, and "app.css matches the build". Report the real numbers. If anything fails, fix the cause (superpowers:systematic-debugging), never skip or loosen a test, and rerun the whole suite.

Do not run this in the worktree: it is a Windows CRLF checkout where two tests fail on clean develop too (`SoftwareParityTests.DemoPersona_RendersByteForByteLikeTheSnapshots` and `OnboardingAccessibilityAccentTests.WithNoAccentPicked_ThePublicPageHasNoAccentOverride`), which would hide a real regression.

- [ ] **Step 2: Write the scratch render test**

Save as `<scratchpad>/ScratchRenderDump.cs`. It is never added to the repo. It renders 13 routes plus the sitemap for each existing template, in two content modes, and writes the normalised pages to the folder named by `RENDER_DUMP_DIR`.

```csharp
using Portfolio.Web.Services;
using Portfolio.Web.Tests.Support;
using Xunit;

namespace Portfolio.Web.Tests.Templates;

public class ScratchRenderDump
{
    private static readonly string[] Keys = ["software", "healthcare", "design-ux", "photography-film", "marketing-content", "sales-bd", "finance-accounting"];

    [Fact]
    public async Task Dump()
    {
        var dir = Environment.GetEnvironmentVariable("RENDER_DUMP_DIR");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        var catalog = new TemplateCatalog(Path.Combine(AppContext.BaseDirectory, "Templates"));

        foreach (var key in Keys)
            foreach (var mode in new[] { "seed", "starter" })
            {
                using var app = new AdminApp();
                string project, post = "no-such-post";
                if (mode == "seed")
                {
                    await DemoSite.SeedAsync(app);
                    (project, post) = DemoSite.FirstSlugs(app);
                    if (key != "software") Assert.Equal(System.Net.HttpStatusCode.Redirect, (await DemoSite.SwitchTemplateAsync(await app.SignedInClientAsync(), key)).StatusCode);
                }
                else
                {
                    if (key == "software") continue;   // software has no starter; its seed mode is the baseline
                    await DemoSite.ApplyStarterAsync(app, key);
                    project = catalog.GetStarter(key)!.Projects[0].Slug;
                }

                var client = app.NewClient();
                var routes = new[] { "/", "/about", "/experience", "/projects", $"/projects/{project}", "/projects/no-such-project", "/skills", "/certifications", "/cv", "/contact", "/sitemap.xml", "/blog", $"/blog/{post}", "/talks" };
                foreach (var route in routes)
                {
                    var response = await client.GetAsync(route);
                    var body = DemoSite.Normalise(await response.Content.ReadAsStringAsync());
                    var name = $"{key}.{mode}.{(route == "/" ? "home" : route.Trim('/').Replace('/', '_'))}.{(int)response.StatusCode}.html";
                    await File.WriteAllTextAsync(Path.Combine(dir, name), body);
                }
            }
    }
}
```

- [ ] **Step 3: Render the base commit and the branch head, then compare**

```bash
BASE=feature/phase-5c-finance-template
for WHAT in base head; do
  REF=$([ "$WHAT" = base ] && echo "$BASE" || echo HEAD)
  EXPORT="<scratchpad>/edu-render-$WHAT"
  rm -rf "$EXPORT" && mkdir -p "$EXPORT"
  git archive "$REF" | tar -x -C "$EXPORT"
  cp "<scratchpad>/ScratchRenderDump.cs" "$EXPORT/tests/Portfolio.Web.Tests/Templates/"
  (cd "$EXPORT" && RENDER_DUMP_DIR="<scratchpad>/edu-dump-$WHAT" dotnet test tests/Portfolio.Web.Tests --nologo --filter "FullyQualifiedName~ScratchRenderDump")
done
diff -r "<scratchpad>/edu-dump-base" "<scratchpad>/edu-dump-head" && echo "IDENTICAL"
ls "<scratchpad>/edu-dump-head" | wc -l
```

Expected: `IDENTICAL`, and 13 or 14 files per template per mode (about 190 in all). If `diff` reports a difference, look at it: a stylesheet fingerprint that `Normalise` does not remove is the only acceptable cause, and then say so and show the diff. Any other difference is a regression: the change set was meant to touch nothing those templates read.

- [ ] **Step 4: Confirm the change set's footprint**

Run: `git diff --stat feature/phase-5c-finance-template..HEAD -- src`
Expected: only `Templates/education/*`, `Styles/themes/education.css`, `Styles/app.css`, `wwwroot/css/app.css` and `wwwroot/img/starter/education/*`. Anything else under `src` is out of scope: investigate.

---

### Task 10: Mutation tests

Each guard must fail when its rule is broken. Do this in a scratch export, never in the worktree. After each mutation run the named test and expect FAIL, then restore the file.

**Files:** none changed in the repo.

- [ ] **Step 1: Make a scratch export**

```bash
EXPORT="<scratchpad>/edu-mut"
rm -rf "$EXPORT" && mkdir -p "$EXPORT"
git archive HEAD | tar -x -C "$EXPORT"
cd "$EXPORT"
```

- [ ] **Step 2: Apply each mutant, run its test, expect FAIL, restore**

Use `cp` to keep the original (`cp FILE FILE.orig` first, `mv FILE.orig FILE` after). `M` is `src/Portfolio.Web/Templates/education`.

| # | Mutation | Command to apply | Test that must fail |
|---|---|---|---|
| 1 | Credentials markup off | `sed -i 's/"credentialsJsonLd": true/"credentialsJsonLd": false/' $M/template.json` | `OnlyHeldQualifications_AreListedAsCredentials_InThePersonMarkup`, `ATemplateOnTheCredentialsArchetype_HasItsThemeStarterAndThumbnail` |
| 2 | A course in progress counted as held | `sed -i 's/"status": "InProgress"/"status": "Earned"/' $M/starter.json` | `OnlyHeldQualifications_AreListedAsCredentials_InThePersonMarkup` |
| 3 | Qualifications and experience swapped | swap the `certifications` and `experience` lines in `sections` | `Education_ListsItsSections_QualificationsFirst_WithArticlesAndWorkshopsOff`, `Education_MenuFollowsTheSectionOrder` |
| 4 | Not marked sensitive | `sed -i 's/"sensitive": true/"sensitive": false/' $M/template.json` | `TheGuidance_IsShownOnEveryEditor_OfASensitiveCredentialsTemplate`, `ATemplateOnTheCredentialsArchetype_...` |
| 5 | Light accent below 4.5:1 | `sed -i 's/--c-accent: 26 102 66;/--c-accent: 150 200 170;/' src/Portfolio.Web/Styles/themes/education.css` | `EveryTextAndAccentToken...` is Results-only, so the one that must fail is `ThemeColours_MeetBodyTextContrast` |
| 6 | An accent choice below 4.5:1 | `sed -i 's/"accent": "#166534"/"accent": "#90d0a8"/' $M/template.json` | `EveryAccentTone_KeepsBodyTextContrast_OnItsTemplateBackgrounds` (education case) |
| 7 | Legal-form suffix on an employer | `sed -i 's/Sample Hill Academy (fictional)/Sample Hill Academy Ltd (fictional)/' $M/starter.json` | `TheStarterContent_IsFictional_UsesOnlyBundledPictures_AndNamesNoPerson` |
| 8 | A pupil named in the starter | `sed -i 's/A whole-school programme/A whole-school programme that helped pupil Jamie/' $M/starter.json` | `TheStarterContent_IsFictional_...` |
| 9 | Another profession's wording on the home page (a string no expectation names, so only the `Foreign` guard can catch it) | `sed -i 's/mentoring or collaboration\./mentoring or patient collaboration./' $M/template.json` (this changes `home.contactHeading` and `contact.heading`) | `EveryPublicRoute_RendersInTheTemplatesOwnWording_WithNoSoftwareWording` (education case) |
| 10 | YouTube field left visible | `sed -i 's/"youtubeId": { "hidden": true }/"youtubeId": { "hidden": false }/' $M/template.json` | `EveryCredentialsPreset_HidesTheTalksYouTubeField_BecauseNoLayoutPlaysVideoForIt` |
| 11 | A starter picture with text | add `<text>x</text>` inside `$M/../../wwwroot/img/starter/education/outdoor-learning-day.svg` | `TheStarterContent_IsFictional_...` |
| 12 | Guidance wording lost | `sed -i 's/Never publish pupil-identifiable information/Be careful with pupil information/' $M/template.json` | `TheGuidance_IsShownOnEveryEditor_...`, `Education_ListsItsSections_...`, the onboarding count test |

Run each with: `dotnet test tests/Portfolio.Web.Tests --nologo --filter "FullyQualifiedName~<TestName>"` and read the real result. A mutant that **survives** (the test passes) means the guard is weak: fix the test, commit the fix in the worktree, and rerun that mutant.

Note: edits to `template.json` need the build to copy it, which `dotnet test` does.

- [ ] **Step 3: Record**

Write the list of mutants and which test caught each into your notes for Task 12 (docs). Nothing is committed in this task unless a mutant survived.

---

### Task 11: Real-browser pass

Exercise the template as a person would, at three widths in both themes, against an LF export (the export from Task 7, rebuilt at HEAD).

**Files:** none changed unless a defect is found.

- [ ] **Step 1: Run the exported branch head** as in Task 7 Step 1 (fresh scratch database), claim the site and publish the Education sample content through the wizard.

- [ ] **Step 2: Walk this checklist with the Playwright browser tools** at 390, 768 and 1280 px wide, light and dark (set the colour scheme with `browser_emulate_media`, and use the page's theme toggle for the stored choice):
  - Wizard: the Education card shows its thumbnail, "Before you publish" with the pupil guidance, and the review step shows the guidance again; publishing the sample content succeeds.
  - Public pages: `/`, `/about`, `/experience`, `/certifications`, `/projects`, one initiative, a missing initiative (404), `/skills`, `/cv`, `/contact`, `/sitemap.xml`. For each: the template's wording, no horizontal overflow (`document.documentElement.scrollWidth <= window.innerWidth`), no broken images, no console errors apart from the intentional 404.
  - `/blog` and `/talks` answer 404 with the same page.
  - Admin: Appearance, Template (preview and switch to Software and back, nothing deleted, Software pages identical on a second visit); Sections (reorder, hide, rename, switch Workshops on, the no-menu refusal); Transfer (export from Education, import into a Software site and back); the guidance text on the experience, skills, initiatives, qualifications, workshops, references and articles editors.
  - Keyboard: skip link, tab order through the menu, visible focus, the mobile menu, the lightbox on the initiative with two pictures (open, Escape closes, focus returns).
  - Look at screenshots of the home page at 1280 px (light and dark), the hero at 390 px (dark), a qualifications page at 390 px (light), an initiative with pictures, and the open mobile menu. Say which you opened and which you only measured.

- [ ] **Step 3: Triage**

Anything caused by this change (wording, colours, the starter, the theme) is fixed with a test-first commit in the worktree. Anything in shared code that every template shows (for example the Credentials hero's fixed "Download CV" and "LinkedIn", the lightbox focus trap, the empty About heading after the wizard) is **not changed**: list it as a carried-forward gap for the notes, as the previous steps did.

Stop the site and drop the scratch database.

---

### Task 12: Documentation and the Phase 5 notes

Write this after Tasks 9 to 11 so every number is the real one.

**Files:**
- Modify: `README.md`, `docs/new-user-guide.md`, `docs/template-authoring-guide.md`, `docs/templates-and-onboarding-plan.md`

- [ ] **Step 1: README**

In `README.md` (around lines 48 to 54, where the Gallery and Results templates are described) add one sentence that Education is a template for school teachers on the same layout as Healthcare, leading with qualifications and registration, with pupil-privacy guidance. If the list of templates elsewhere in the README names them, add Education there too.

- [ ] **Step 2: New user guide**

In `docs/new-user-guide.md`:
- Line 72 (the wizard's template step): extend the sentence about guidance so it mentions that Education reminds you never to publish pupil information.
- Around line 368 (the template list at `/admin/template`): add **Education** to the names and describe it: a school teacher's site that opens with qualifications and registration, then teaching experience, then curriculum initiatives; articles and workshops are off until you switch them on; the sample content is fictional.

- [ ] **Step 3: Template authoring guide**

In `docs/template-authoring-guide.md`:
- Line 16: Credentials is now used by Healthcare and Education.
- In section 4, step 4 ("Add the new template to the rendering tests' wording lists"): say that for a Credentials template the list is `CredentialsArchetypeTests.Keys` and its `Wordings` entry, and that every keyed check, including `ArchetypeTemplateFlowTests`, then runs for it.
- Add a short "Credentials templates through data only (Education)" note: what Education added, that it needed no layout change, the editor guidance repeat for `sensitive`, and the `Foreign` wording guard.

- [ ] **Step 4: Plan document**

In `docs/templates-and-onboarding-plan.md`:
- Phase 5 status paragraph (line 537): add that Phase 5 step 4 (Education on the Credentials layout) is done, pending independent review, and that item 3 of the numbered list is therefore done; item 4 (Data & Analytics, Product/Project Management on Showcase) is the remaining work.
- In the numbered list, mark item 3 **Done**.
- Add a "#### Phase 5 step 4: Education on the Credentials layout (done, pending independent review)" section after step 3, in the same shape as the earlier steps, with these headings: **Scope**; **How "qualifications first" is expressed**; **The preset**; **Starter content**; **Surfaces checked for the new template (data only)**; **Tests** (including the keyed Credentials class, the flow checks now covering three archetypes, the mutants from Task 10 and the real result of the full LF suite and the build warning count); **Real-browser pass** (Task 11, what was seen and what was only measured); **Known gaps / owner decisions carried forward** (every shared-code finding from Task 11, plus: Credentials hero keeps "Download CV" and "LinkedIn" as fixed words; only the home page differs by archetype for Credentials; sample `/cv.pdf` needs the glyph fonts; not run against SQL Server; the "(sample)" qualification names).
- Update the "What remains in Phase 5" sentence to name only item 4.

- [ ] **Step 5: Check the links and wording, then commit**

Run: `grep -rn "patient" docs/template-authoring-guide.md | head` (only to confirm Healthcare wording was not changed by accident), and re-read the four edits.

```bash
git checkout -- src/Portfolio.Web/wwwroot/css/app.css 2>/dev/null; git status --short
git add README.md docs/new-user-guide.md docs/template-authoring-guide.md docs/templates-and-onboarding-plan.md
git commit -m "docs: Education template (README, user guide, authoring guide, Phase 5 step 4 notes)"
```

---

### Task 13: Independent review and fixes

**Files:** whatever the review finds.

- [ ] **Step 1: Review the branch**

Dispatch the `quality-reviewer` agent (Opus, read-only). Give it, since it starts with no context: the repo path of the worktree, the diff range `feature/phase-5c-finance-template..HEAD`, the spec path, this plan's path, and the acceptance criteria (spec section 5). Ask it to check, specifically: correctness bugs; that no file outside the stated footprint changed; that each criterion maps to a test or a stated manual check; weak or tautological assertions in the new tests; silent failure paths; unreachable features; wording that leaks Software or Healthcare terms; the pupil-privacy copy on every editor.

- [ ] **Step 2: Fix every finding test-first** (superpowers:receiving-code-review: verify each against the code before changing anything; push back with evidence if a finding is wrong). One commit per fix. Rerun Task 9 Step 1 after the last fix and report the real numbers.

---

### Task 14: Hand-off

- [ ] **Step 1: Run superpowers:verification-before-completion.** State, with real output: the full-suite result on the LF export, the build warning count, what was not run (SQL Server, a real uploaded photo, screen-reader output), and how a person sees the change: sign in at `/admin/login`, Appearance, Template (or Tools, Setup wizard, step 1), choose Education, publish the sample content, then open `/`, `/experience`, `/certifications`, `/projects` and `/cv`.

- [ ] **Step 2: Offer the options** (superpowers:finishing-a-development-branch): the branch is stacked on the Finance branch and needs a rebase onto `develop` once the Finance PR merges. Do not push, open a PR or merge unless the user asks.

---

## Self-review against the spec

**Spec coverage**

| Spec item | Task |
|---|---|
| Criterion 1 (catalog, wizard step, picker, thumbnail, guidance, validation) | 4 (onboarding and manifest tests), 5, 7; flow wizard test in 3 and 4 |
| Criterion 2 (own wording, no Software or Healthcare wording, off sections 404) | 2 (moved checks), 4 (`Foreign`, education wording), 6 |
| Criterion 3 (home leads with qualifications and recent roles, menu order) | 4 (`Education_MenuFollowsTheSectionOrder`, home wording), 6 |
| Criterion 4 (held-only qualifications, both directions) | 4, 6 |
| Criterion 5 (guidance on wizard card, review step, template page, every editor) | 4 (editor test, onboarding count), 5; template page by the existing flow wizard and template tests |
| Criterion 6 (4.5:1 tokens and accents) | 4, 5, mutants 5 and 6 |
| Criterion 7 (switching deletes nothing, row counts) | 3 and 4 (flow switching test over the new key), Task 11 |
| Criterion 8 (export and import across templates, inert section keys) | 3 and 4 (flow export tests), Task 11 |
| Criterion 9 (headers equal Software's, parity snapshots unedited) | Task 9 (full suite; parity snapshots not edited; the headers check is Results-only today, so the render comparison and the footprint check carry it) |
| Criterion 10 (before and after render) | Task 1 (corrected), Task 9 Step 3 |
| Criterion 11 (starter shape, no legal-form suffix, no pupil named) | 4, 6, mutants 7 and 8 |
| Spec section 3 constraints | Global Constraints; Task 8 (stylesheet); Task 9 Step 4 (footprint) |
| Spec section 6 mutation testing, LF suite, real-browser pass | Tasks 10, 9, 11 |

**Known soft spot, stated plainly:** criterion 9's "public response headers equal Software's" has no Credentials-keyed test. The Gallery and Results classes each assert it for their keys; the credentials layout adds no header, script or CSP change (the footprint check proves the change set touches no code), but if the reviewer wants a keyed assertion, add `APublicCredentialsPage_HasTheSameSecurityHeadersAsSoftware` to `CredentialsArchetypeTests`, modelled on `ResultsArchetypeTests.APublicResultsPage_HasTheSameSecurityHeadersAsSoftware_AndNoThirdPartyScriptOrFrame`.

**Type and name consistency:** `CredentialsArchetypeTests.Keys`, `KeyData`, `Wordings`, `Wording`, `Definition`, `TemplatesDir` are defined in Task 2 and used under those names in Tasks 3 and 4. `Wordings["education"].DetailSlug` (`reading-for-pleasure-programme`) matches the starter slug in Task 6. The held and unheld qualifications in the starter (four `Earned`, one `InProgress`, one `Planned`) are what `OnlyHeldQualifications...` needs. Accent keys `green`, `terracotta`, `blue`, `gold` match `Education_UsesItsOwnDistinctTheme_NotHealthcares`. Section order and short labels in the manifest match `Education_ListsItsSections_...` and the nav labels in `Wordings`.
