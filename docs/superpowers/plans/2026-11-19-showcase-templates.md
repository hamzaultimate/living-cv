# Data & Analytics and Product / Project Management Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the `data-analytics` and `product-management` templates as data-only presets on the existing Showcase layout.

**Architecture:** Task 1 (lead) writes the failing tests and registers both keys in the shared lists. Tasks 2 and 3 run in parallel, one subagent each in its own git worktree, and each owns only its template's files. Task 4 (lead) merges, rebuilds the stylesheet from an LF export, takes the thumbnails, updates docs, and verifies. Spec: `docs/superpowers/specs/2026-11-19-showcase-templates-design.md`.

**Tech Stack:** ASP.NET Core Razor Pages, xUnit, Tailwind (standalone CLI, runs inside `dotnet build`), JSON manifests, SVG placeholders.

## Global Constraints

- No C# change in `src/`, no Razor change, no migration, no CSP change, no new script, no new copy key. `/cv`, `/cv.pdf`, `/og.png` keep the Software look.
- The Software parity snapshots pass unedited; Healthcare, Gallery and Results public output does not change.
- Everything visible comes from the manifest. No layout partial names a template by key, theme or profession.
- Both templates: `archetype: "showcase"`, not `sensitive`, `theme.default` equals the key, 4 accent choices, section order `about, experience, projects, skills, certifications, blog, talks, testimonials, contact` with `talks` listed as `"enabled": false` and its `youtubeId` editor field `"hidden": true`.
- Every `copy` string is plain text (no `<` or `>`), differs from Software's wording unless it is in this neutral list: `Skills`, `Experience`, `Links`, `featured`, `View certificate`, `Curriculum vitae — {name}, {title}.`, `Selected work`. The serialised copy must not match `\.NET|GitHub|software|developer|clinical|patient|licen[cs]e`. The serialised editor block must not match `(?i)tech stack|throughput|uptime|\brps\b|distributed|\.NET|case stud`.
- SEO: `seo.title` at most 65 characters and `seo.description` / `seo.aboutDescription` at most 160 once expanded; word them so they read correctly for any title.
- Contrast: every text and accent token at least 4.5:1 on bg, surface and surface-2 in light and dark, including all four accent choices, and bg on accent. `ThemeStylesheetTests` and the accent-contrast test decide; adjust a hex value until they pass.
- Starter content is fictional: `"fictional": true`, the profile summary contains the word "fictional", `example.org` email and links, every project title contains `(sample)`, every employer contains `(fictional)`, no employer or client name carries a legal-form suffix (Inc, Ltd, LLC, LLP, LP, GmbH, PLC, apart or glued on), every cover has `heroImageAlt`, every extra picture has a caption, every picture is a site path under `/img/starter/<key>/`.
- Starter pictures are generated SVG: under 60 KB each, no `<script>`, no `<text>`, no `<image>`, no `http` address except the `www.w3.org` namespace. Do not download images.
- The committed `src/Portfolio.Web/wwwroot/css/app.css` is CI output built from LF sources. A local `dotnet build` on Windows rewrites it with different bytes. **Never commit it from a subagent**: run `git checkout -- src/Portfolio.Web/wwwroot/css/app.css` before every commit. The lead rebuilds it in Task 4.
- Commit messages end with the line `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`. Never push, merge to another branch or deploy.
- Test command (from the worktree root): `dotnet test tests/Portfolio.Web.Tests --filter "<expr>"`. A pass that ran zero tests is not a pass: check the reported count.

## File Structure

| File | Owner | Responsibility |
|---|---|---|
| `tests/Portfolio.Web.Tests/Templates/ShowcaseArchetypeTests.cs` (create) | Task 1 | Keyed checks for every template on the Showcase archetype |
| `tests/Portfolio.Web.Tests/Templates/ArchetypeTemplateFlowTests.cs` (modify) | Task 1 | Admin flows run for the two new keys |
| `tests/Portfolio.Web.Tests/Templates/TemplateManifestTests.cs` (modify) | Task 1 | Shipped folder list and catalog order |
| `tests/Portfolio.Web.Tests/Admin/OnboardingFlowTests.cs` (modify) | Task 1 | Wizard card headings, thumbnails, card count |
| `src/Portfolio.Web/Styles/app.css` (modify), `Styles/themes/data-analytics.css`, `Styles/themes/product-management.css` (create as stubs) | Task 1 | Theme imports, so the build resolves them |
| `src/Portfolio.Web/Templates/data-analytics/{template.json,starter.json}`, `Styles/themes/data-analytics.css`, `wwwroot/img/starter/data-analytics/*.svg` | Task 2 | Data & Analytics |
| `src/Portfolio.Web/Templates/product-management/{template.json,starter.json}`, `Styles/themes/product-management.css`, `wwwroot/img/starter/product-management/*.svg` | Task 3 | Product / Project Management |
| `Templates/<key>/preview.png`, `wwwroot/css/app.css`, `README.md`, `docs/template-authoring-guide.md`, `docs/templates-and-onboarding-plan.md` | Task 4 | Thumbnails, built stylesheet, docs |

---

### Task 1: Failing tests and shared registration (lead, this worktree)

**Files:**
- Create: `tests/Portfolio.Web.Tests/Templates/ShowcaseArchetypeTests.cs`
- Modify: `tests/Portfolio.Web.Tests/Templates/ArchetypeTemplateFlowTests.cs:13-24,118`
- Modify: `tests/Portfolio.Web.Tests/Templates/TemplateManifestTests.cs:56,58`
- Modify: `tests/Portfolio.Web.Tests/Admin/OnboardingFlowTests.cs:302,312-315`
- Modify: `src/Portfolio.Web/Styles/app.css:1-8`
- Create: `src/Portfolio.Web/Styles/themes/data-analytics.css`, `src/Portfolio.Web/Styles/themes/product-management.css` (one-line stubs)

**Interfaces:**
- Produces: `ShowcaseArchetypeTests.Keys` (`string[]`), `KeyData`, `KeyAndContent` (`TheoryData`); the two keys `"data-analytics"` and `"product-management"`; theme file names equal to the keys.
- Consumes: `GalleryArchetypeTests` internal helpers (`G.Definition`, `G.SiteAsync`, `G.GetAsync`, `G.MainOf`, `G.Escaped`, `G.TemplatesDir`, `G.Layouts`, `G.Wwwroot`), `ResultsArchetypeTests.AssertNoLegalFormSuffix(string)`, `TemplateDirs.WithExtra`, `AdminApp.WithTemplates`, `ContentFlow.BlankSiteAsync`, `DemoSite.SeedAsync`.

- [ ] **Step 1: Write the keyed test class**

Create `tests/Portfolio.Web.Tests/Templates/ShowcaseArchetypeTests.cs`:

```csharp
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Portfolio.Web.Services;
using Portfolio.Web.Tests.Support;
using Xunit;
using G = Portfolio.Web.Tests.Templates.GalleryArchetypeTests;

namespace Portfolio.Web.Tests.Templates;

/// <summary>
/// Every template on the showcase layout other than Software & IT (which is guarded by the byte-for-byte parity snapshots). Driven by <see cref="Keys"/>: a further showcase
/// template is covered by adding its key here. The admin and cross-cutting paths run from <see cref="ArchetypeTemplateFlowTests"/>, which includes these keys.
/// </summary>
public class ShowcaseArchetypeTests
{
    /// <summary>Every shipped showcase template except Software & IT.</summary>
    public static readonly string[] Keys = ["data-analytics", "product-management"];

    public static TheoryData<string> KeyData => new(Keys);

    public static TheoryData<string, string> KeyAndContent
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var key in Keys) foreach (var content in new[] { "blank", "sample", "full" }) data.Add(key, content);
            return data;
        }
    }

    private static string ItemOf(TemplateDefinition t) => t.Sections.Single(s => s.Key == "projects").ItemLabel!;

    // ---------------------------------------------------------------- the manifest and its files

    [Theory]
    [MemberData(nameof(KeyData))]
    public void AShowcaseTemplate_HasItsThemeStarterAndThumbnail(string key)
    {
        var template = G.Definition(key);
        var catalog = new TemplateCatalog(G.TemplatesDir);

        Assert.Equal(key, template.Key);
        Assert.Equal(Archetypes.Showcase, template.Archetype);
        Assert.True(File.Exists(Path.Combine(G.Layouts, "showcase", "_Home.cshtml")));
        Assert.Equal(key, template.Theme.Default);
        Assert.True(File.Exists(Path.Combine(TemplateDirs.Web, "Styles", "themes", key + ".css")));
        Assert.NotNull(catalog.GetStarter(key));
        Assert.NotNull(catalog.GetPreviewImagePath(key));
        Assert.Equal(4, template.Theme.Accents.Count);
        Assert.Contains(template.Sections, s => s.Key == SectionRegistry.Projects && !string.IsNullOrWhiteSpace(s.ItemLabel));
        Assert.False(Archetypes.ShowsClientsOnHome(template.Archetype));
        Assert.False(template.Sensitive);
    }

    [Theory]
    [MemberData(nameof(KeyData))]
    public void TheMenuOrder_IsTheShowcaseOrder_WithTalksOffAndItsYouTubeFieldHidden(string key)
    {
        var template = G.Definition(key);

        Assert.Equal(["about", "experience", "projects", "skills", "certifications", "blog", "talks", "testimonials", "contact"], template.Sections.Select(s => s.Key));
        Assert.Contains(template.Sections, s => s.Key == "talks" && !s.Enabled);
        Assert.True(template.Editor.Talks.Fields.TryGetValue("youtubeId", out var field) && field!.Hidden, $"{key} must hide the YouTube field");
    }

    [Theory]
    [MemberData(nameof(KeyData))]
    public void TheThumbnail_IsARealScreenshotOfTheRightSize(string key)
    {
        var path = new TemplateCatalog(G.TemplatesDir).GetPreviewImagePath(key)!;
        var bytes = File.ReadAllBytes(path);

        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, bytes[..4]);
        Assert.Equal(1200, (bytes[16] << 24) | (bytes[17] << 16) | (bytes[18] << 8) | bytes[19]);
        Assert.Equal(800, (bytes[20] << 24) | (bytes[21] << 16) | (bytes[22] << 8) | bytes[23]);
        Assert.True(bytes.Length > 30_000, "a screenshot of a rendered page is not this small");
    }

    [Theory]
    [MemberData(nameof(KeyData))]
    public void TheStarterContent_IsFictional_UsesOnlyBundledPictures_AndDescribesEveryOne(string key)
    {
        var starter = new TemplateCatalog(G.TemplatesDir).GetStarter(key)!;

        Assert.True(starter.Fictional);
        Assert.Contains("fictional", starter.Profile.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("example.org", starter.Profile.Email);
        Assert.True(starter.Profile.Metrics.Count >= 3, "the metrics row needs at least three figures");
        Assert.True(starter.Projects.Count(p => p.Featured) >= 3, "the home page needs at least three featured projects");
        Assert.Contains(starter.Projects, p => !p.Featured);
        Assert.Contains(starter.Projects, p => p.Screenshots.Count > 0);
        Assert.All(starter.Projects, p => Assert.Contains("(sample)", p.Title));
        Assert.All(starter.Experiences, e => Assert.Contains("fictional", e.Company, StringComparison.OrdinalIgnoreCase));
        Assert.NotEmpty(starter.Skills);
        Assert.NotEmpty(starter.Certifications);
        Assert.NotEmpty(starter.Testimonials);
        foreach (var name in starter.Experiences.Select(e => e.Company.Replace("(fictional)", "", StringComparison.OrdinalIgnoreCase).Trim()).Concat(starter.Projects.Select(p => p.Org ?? "")))
            ResultsArchetypeTests.AssertNoLegalFormSuffix(name);

        var images = new List<string>();
        foreach (var p in starter.Projects)
        {
            Assert.False(string.IsNullOrWhiteSpace(p.HeroImage), $"{p.Slug} has no cover");
            Assert.False(string.IsNullOrWhiteSpace(p.HeroImageAlt), $"{p.Slug} cover has no description");
            Assert.False(Media.IsVideo(p.HeroImage));
            images.Add(p.HeroImage!);
            foreach (var shot in p.Screenshots)
            {
                Assert.False(string.IsNullOrWhiteSpace(shot.Caption), $"{p.Slug} has a picture without a description");
                images.Add(shot.Url);
            }
            Assert.All(p.Links, l => Assert.StartsWith("https://example.org/", l.Url));
        }
        foreach (var url in images)
        {
            Assert.StartsWith($"/img/starter/{key}/", url);
            var file = Path.Combine(G.Wwwroot, url.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(file), $"{url} is not bundled under wwwroot");
            Assert.True(new FileInfo(file).Length < 60 * 1024, $"{url} is larger than a starter placeholder should be");
            Assert.DoesNotMatch(@"(?i)<script|<text|<image|https?://(?!www\.w3\.org)", File.ReadAllText(file));
        }
        Assert.Equal(images.Count, images.Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(KeyData))]
    public void AShowcaseTemplate_DoesNotInheritSoftwareWording(string key)
    {
        var software = new TemplateCatalog(G.TemplatesDir).Get("software");
        var template = G.Definition(key);
        var neutral = new[] { "Skills", "Experience", "Links", "featured", "View certificate", "Curriculum vitae — {name}, {title}.", "Selected work" };

        foreach (var group in typeof(TemplateCopy).GetProperties())
            foreach (var prop in group.PropertyType.GetProperties().Where(p => p.PropertyType == typeof(string)))
            {
                var a = (string?)prop.GetValue(group.GetValue(software.Copy));
                var b = (string?)prop.GetValue(group.GetValue(template.Copy));
                if (neutral.Contains(b) || string.IsNullOrEmpty(b)) continue;
                Assert.True(a != b, $"copy.{group.Name}.{prop.Name} is identical to software's: {b}");
            }
        Assert.DoesNotMatch(@"\.NET|GitHub|software|developer|clinical|patient|licen[cs]e", JsonSerializer.Serialize(template.Copy));
        Assert.DoesNotMatch(@"(?i)tech stack|throughput|uptime|\brps\b|distributed|\.NET|case stud", JsonSerializer.Serialize(template.Editor));
    }

    // ---------------------------------------------------------------- rendering: blank, sample and full content

    [Theory]
    [MemberData(nameof(KeyAndContent))]
    public async Task EveryPublicPage_AnswersInTheShowcaseLayout_ForBlankSampleAndFullContent(string key, string content)
    {
        using var app = await G.SiteAsync(key, content);
        var client = app.NewClient();
        var home = await G.GetAsync(client, "/");

        Assert.Contains($"data-template=\"{G.Definition(key).Theme.Default}\"", home);
        Assert.Contains("grid-backdrop", home);          // the showcase hero
        Assert.DoesNotContain("data-results", home);
        foreach (var path in new[] { "/", "/projects", "/about", "/experience", "/skills", "/certifications", "/blog", "/contact", "/cv" })
            Assert.Single(Regex.Matches(G.MainOf(await G.GetAsync(client, path)), "<h1[ >]"));
        await G.GetAsync(client, "/talks", HttpStatusCode.NotFound);
    }

    [Theory]
    [MemberData(nameof(KeyData))]
    public async Task ABlankSite_ShowsTheTemplatesEmptyStatesAndItsNotFoundWords(string key)
    {
        using var app = await G.SiteAsync(key, "blank");
        var template = G.Definition(key);
        var client = app.NewClient();

        Assert.Contains(G.Escaped(template.Copy.Home.WorkEmptyTitle), await G.GetAsync(client, "/"));
        Assert.Contains(G.Escaped(template.Copy.Projects.EmptyTitle), await G.GetAsync(client, "/projects"));
        var missing = await G.GetAsync(client, "/projects/nothing-here", HttpStatusCode.NotFound);
        Assert.Contains(G.Escaped(ItemOf(template) + " not found"), missing);
    }

    [Theory]
    [MemberData(nameof(KeyData))]
    public async Task ResponseHeaders_EqualSoftwares_OnTheHomeAndTheWorkPage(string key)
    {
        using var site = await G.SiteAsync(key, "sample");
        using var software = new AdminApp();
        await DemoSite.SeedAsync(software);
        string[] volatileHeaders = ["Date", "ETag", "Content-Length", "Age", "Last-Modified", "Expires"];
        string Flat(HttpResponseMessage r) => string.Join("\n", r.Headers.Concat(r.Content.Headers)
            .Where(h => !volatileHeaders.Contains(h.Key, StringComparer.OrdinalIgnoreCase))
            .OrderBy(h => h.Key, StringComparer.OrdinalIgnoreCase).Select(h => h.Key + ": " + string.Join(",", h.Value)));

        foreach (var path in new[] { "/", "/projects" })
            Assert.Equal(Flat(await software.NewClient().GetAsync(path)), Flat(await site.NewClient().GetAsync(path)));
    }

    // ---------------------------------------------------------------- labels come from the manifest

    [Theory]
    [MemberData(nameof(KeyData))]
    public async Task ThePages_ReadTheirHeadingsAndEmptyStatesFromTheManifest(string key)
    {
        var original = G.Definition(key);
        var dir = TemplateDirs.WithExtra("zork-showcase", node =>
        {
            node["name"] = "Zork Showcase";
            foreach (var (group, value) in node["copy"]!.AsObject().ToList())
                foreach (var (prop, _) in value!.AsObject().ToList())
                    value[prop] = prop switch
                    {
                        "imageAlt" => $"zork-{group}-{prop} {{title}}",
                        "description" => $"zork-{group}-{prop} for {{name}}",
                        _ => $"zork-{group}-{prop}",
                    };
            foreach (var section in node["sections"]!.AsArray())
            {
                section!["label"] = "zorkword" + section["key"];
                if (section["shortLabel"] is not null) section["shortLabel"] = "zorkshort" + section["key"];
                if (section["itemLabel"] is not null) section["itemLabel"] = "Zorkitem" + section["key"];
            }
        }, baseTemplate: key, starterFrom: key);
        try
        {
            using var app = AdminApp.WithTemplates(dir);
            await ContentFlow.BlankSiteAsync(app, "zork-showcase");
            var client = app.NewClient();

            var home = await G.GetAsync(client, "/");
            var projects = await G.GetAsync(client, "/projects");
            Assert.Contains("zork-home-workEmptyTitle", home);
            Assert.Contains("zork-projects-emptyTitle", projects);
            Assert.Contains("zork-projects-heading", projects);
            Assert.Contains("zork-experience-heading", await G.GetAsync(client, "/experience"));
            Assert.Contains("zork-skills-heading", await G.GetAsync(client, "/skills"));
            Assert.Contains("zork-certifications-heading", await G.GetAsync(client, "/certifications"));
            Assert.Contains("zork-contact-heading", await G.GetAsync(client, "/contact"));
            var missing = await G.GetAsync(client, "/projects/none", HttpStatusCode.NotFound);
            Assert.Contains("Zorkitemprojects not found", missing);

            // None of the template's real wording survives on the pages the shared layout draws.
            var visible = WebUtility.HtmlDecode(DemoSite.WithoutScripts(string.Concat(home, projects, missing)));
            var words = original.Copy.Home.GetType().GetProperties().Select(p => (string?)p.GetValue(original.Copy.Home))
                .Concat(original.Copy.Projects.GetType().GetProperties().Select(p => (string?)p.GetValue(original.Copy.Projects)))
                .Where(s => !string.IsNullOrWhiteSpace(s) && !s.Contains("{title}") && s.Contains(' ')).Distinct().ToList();
            Assert.NotEmpty(words);
            foreach (var w in words) Assert.DoesNotContain(w!, visible);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
```

- [ ] **Step 2: Run the new tests to see them fail for the right reason**

Run: `dotnet test tests/Portfolio.Web.Tests --filter "FullyQualifiedName~ShowcaseArchetypeTests"`
Expected: the project compiles; every test FAILS. With no template folders the catalog falls back to Software, so `Assert.Equal(key, template.Key)` and similar report `data-analytics` vs `software`. A compile error means a helper name above is wrong: fix the name, not the intent.

- [ ] **Step 3: Include the new keys in the admin flow tests**

In `tests/Portfolio.Web.Tests/Templates/ArchetypeTemplateFlowTests.cs` replace lines 13-24 (the summary and `Keys` and `Card`) with:

```csharp
/// <summary>
/// The admin and cross-cutting paths for every template on an archetype other than Software and Healthcare (gallery, results and the showcase presets), through data only: wizard, preview,
/// switching, section settings, sitemap and structured data, export and import, and a blank wizard-finished site end to end. Driven by
/// <see cref="GalleryArchetypeTests.Keys"/>, <see cref="ResultsArchetypeTests.Keys"/> and <see cref="ShowcaseArchetypeTests.Keys"/>: a further template is covered by adding its key there.
/// </summary>
public class ArchetypeTemplateFlowTests
{
    public static readonly string[] Keys = [.. GalleryArchetypeTests.Keys, .. ResultsArchetypeTests.Keys, .. ShowcaseArchetypeTests.Keys];

    public static TheoryData<string> KeyData => new(Keys);

    /// <summary>What marks one work card in the archetype's own layout (the showcase presets use the shared work page, so its card).</summary>
    private static string Card(string key) =>
        Definition(key).Archetype == Archetypes.Results ? "data-results-card"
        : Definition(key).Archetype == Archetypes.Showcase ? "<article class=\"card group flex flex-col\""
        : "<article class=\"group relative";

    /// <summary>What marks a case-study page in the archetype's own layout.</summary>
    private static string DetailMarker(string key) =>
        Definition(key).Archetype == Archetypes.Showcase ? "<article class=\"container-page py-16 md:py-20\">" : "Next ";
```

Then change the preview assertion (was line 118):

```csharp
            if (path != "/") Assert.Contains(path == "/projects" ? Card(key) : DetailMarker(key), html);
```

- [ ] **Step 4: Update the shipped lists**

In `tests/Portfolio.Web.Tests/Templates/TemplateManifestTests.cs` replace lines 56 and 58:

```csharp
        Assert.Equal(new[] { "data-analytics", "design-ux", "finance-accounting", "healthcare", "marketing-content", "photography-film", "product-management", "sales-bd", "software" }, TemplateFolders().Select(Path.GetFileName).OrderBy(x => x).ToArray());
        var catalog = new TemplateCatalog(TemplatesDir);
        Assert.Equal(new[] { "software", "data-analytics", "design-ux", "finance-accounting", "healthcare", "marketing-content", "photography-film", "product-management", "sales-bd" }, catalog.All.Select(t => t.Key));
```

In `tests/Portfolio.Web.Tests/Admin/OnboardingFlowTests.cs` insert after the `confidential financial` assertion (line 314) and change the radio count on the next line:

```csharp
        Assert.Contains(">Data analyst or scientist</h2>", html);
        Assert.Contains("src=\"/admin/welcome/thumb/data-analytics\"", html);
        Assert.Contains(">Product or project manager</h2>", html);
        Assert.Contains("src=\"/admin/welcome/thumb/product-management\"", html);
        Assert.Equal(9, Regex.Matches(html, "type=\"radio\" name=\"Key\"").Count);
```

(the old `Assert.Equal(7, ...)` line is removed; the `Before you publish` count of 6 stays, because neither new template carries guidance.)

- [ ] **Step 5: Register the theme imports with stub files**

In `src/Portfolio.Web/Styles/app.css` replace lines 1-7 with:

```css
/* One file per template theme: variables only, scoped to [data-template="<key>"]. */
@import "./themes/data-analytics.css";
@import "./themes/design-ux.css";
@import "./themes/finance-accounting.css";
@import "./themes/healthcare.css";
@import "./themes/marketing-content.css";
@import "./themes/photography-film.css";
@import "./themes/product-management.css";
@import "./themes/sales-bd.css";
```

Create `src/Portfolio.Web/Styles/themes/data-analytics.css` containing exactly `/* Data & Analytics theme: written in Task 2. */` and `src/Portfolio.Web/Styles/themes/product-management.css` containing exactly `/* Product & Project Management theme: written in Task 3. */`.

- [ ] **Step 6: Build and confirm the red state is the intended one**

Run: `dotnet build tests/Portfolio.Web.Tests` then `dotnet test tests/Portfolio.Web.Tests --filter "FullyQualifiedName~ShowcaseArchetypeTests|FullyQualifiedName~ShipsExactlyTheShippedTemplates|FullyQualifiedName~TheTemplateStep_ShowsCardsGroupedByProfession"`
Expected: build succeeds; the filtered tests FAIL (missing folders, missing cards), nothing fails to compile.
Run: `dotnet test tests/Portfolio.Web.Tests --filter "FullyQualifiedName~SoftwareParityTests|FullyQualifiedName~GalleryArchetypeTests|FullyQualifiedName~ResultsArchetypeTests"`
Expected: same pass/fail result as on the parent commit `4c94c99` (on this Windows host `SoftwareParityTests.DemoPersona_RendersByteForByteLikeTheSnapshots` already fails because of CRLF; record the output and treat that one as a known baseline failure).

- [ ] **Step 7: Commit (restore the generated stylesheet first)**

```bash
git checkout -- src/Portfolio.Web/wwwroot/css/app.css
git add tests src/Portfolio.Web/Styles
git commit -m "test: register the showcase templates (data-analytics, product-management) in the shared lists

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

Note the commit hash (`git log -1 --format=%H`). Tasks 2 and 3 branch from it.

---

### Task 2: Data & Analytics (subagent A, own worktree)

Dispatch with the Agent tool: `subagent_type: "general-purpose"`, `isolation: "worktree"`, prompt = the **Global Constraints** section above plus this task, plus the line "Start by running `git log --oneline -1`; it must show the commit whose subject starts with `test: register the showcase templates`. If it does not, run `git reset --hard <that commit hash>` (the worktree is yours alone). Report your final commit hash and branch name."

**Files (this task owns only these):**
- Create: `src/Portfolio.Web/Templates/data-analytics/template.json`
- Create: `src/Portfolio.Web/Templates/data-analytics/starter.json`
- Modify: `src/Portfolio.Web/Styles/themes/data-analytics.css` (replace the stub)
- Create: `src/Portfolio.Web/wwwroot/img/starter/data-analytics/*.svg`
- Do not touch: `preview.png` (Task 4), `wwwroot/css/app.css`, any test file, README, docs.

**Interfaces:**
- Consumes: the Task 1 tests (`ShowcaseArchetypeTests`, `ArchetypeTemplateFlowTests`, `TemplateManifestTests`, `ThemeStylesheetTests`, the accent-contrast test). Reference shapes: `Templates/software/template.json` (key set and `editor` block), `Templates/finance-accounting/template.json` (talks block with the hidden `youtubeId`, a non-sensitive template with `theme.accents`), `Templates/finance-accounting/starter.json` (starter shape), `Styles/themes/finance-accounting.css` (theme shape).
- Produces: a template whose `key`, folder and `theme.default` are all `data-analytics`.

- [ ] **Step 1: Write the manifest**

Create `template.json` starting from `Templates/software/template.json` (same key set, so every required `copy` string exists) with these values:

```jsonc
{
  "$schema": "../template.schema.json",
  "key": "data-analytics",
  "name": "Data & Analytics",
  "profession": "Data analyst or scientist",
  "description": "Showcase for data analysts, scientists and analytics engineers: projects with the question, the method and the measured result.",
  "archetype": "showcase",
  "theme": {
    "default": "data-analytics",
    "themeColor": "#0c1418",
    "accents": [
      { "key": "teal",   "label": "Teal",   "dark": { "accent": "#2dd4bf", "strong": "#5eead4" }, "light": { "accent": "#0f766e", "strong": "#115e59" } },
      { "key": "indigo", "label": "Indigo", "dark": { "accent": "#818cf8", "strong": "#a5b4fc" }, "light": { "accent": "#4338ca", "strong": "#3730a3" } },
      { "key": "orange", "label": "Orange", "dark": { "accent": "#fb923c", "strong": "#fdba74" }, "light": { "accent": "#c2410c", "strong": "#9a3412" } },
      { "key": "rose",   "label": "Rose",   "dark": { "accent": "#fb7185", "strong": "#fda4af" }, "light": { "accent": "#be123c", "strong": "#9f1239" } }
    ]
  },
  "heroSuggestions": {
    "title": "Data Analyst",
    "summary": "Data analyst who turns messy data into clear answers, building reliable pipelines, models and dashboards that people use to decide."
  },
  "sections": [
    { "key": "about", "label": "about" },
    { "key": "experience", "label": "experience" },
    { "key": "projects", "label": "projects", "itemLabel": "Analysis" },
    { "key": "skills", "label": "tools & methods", "shortLabel": "tools" },
    { "key": "certifications", "label": "certifications", "shortLabel": "certs" },
    { "key": "blog", "label": "notes", "itemLabel": "Note" },
    { "key": "talks", "label": "talks", "enabled": false },
    { "key": "testimonials", "label": "testimonials" },
    { "key": "contact", "label": "contact" }
  ],
  "skillGroups": ["Analysis & statistics", "Data engineering", "Visualisation", "Machine learning", "Languages & tools"],
  "seo": {
    "title": "{name} — {title}",
    "description": "{name}, {title}. Analyses, models and dashboards, with the question asked and the result measured.",
    "aboutDescription": "About {name}, {title}: the questions asked, the methods used and the results measured."
  },
  "copy": {
    "layout": { "footerNote": "Data, analysis and insight." },
    "home": {
      "workEyebrow": "Selected analyses", "workHeading": "Projects and results",
      "workEmptyTitle": "Featured analyses coming soon",
      "workEmptyBody": "Once you add your featured projects, the question, the method and the result of each will appear here.",
      "testimonialsEyebrow": "Kind words", "testimonialsHeading": "Feedback from stakeholders",
      "contactHeading": "Have a question the data could answer?",
      "experienceEyebrow": "Experience", "experienceHeading": "Where I've worked with data",
      "credentialsEyebrow": "Learning and credentials", "credentialsHeading": "Courses and certifications",
      "skillsHeading": "Tools and methods"
    },
    "projects": {
      "title": "Projects", "description": "Data projects: the question, the method and the measured result.",
      "eyebrow": "Selected analyses", "heading": "Projects and results",
      "lead": "Each project states the question, how it was answered and what changed because of it.",
      "emptyTitle": "No analyses yet", "emptyBody": "Add your first project to show the question, the method and the result.",
      "filterLabel": "Filter projects by subject", "featuredBadge": "featured",
      "problemHeading": "The question", "approachHeading": "Method",
      "noWriteup": "A full write-up of this analysis is on the way.",
      "stackHeading": "Tools", "linksHeading": "Links", "proofHeading": "Evidence",
      "screenshotsHeading": "Charts and dashboards", "imageAlt": "{title} chart",
      "galleryLabel": "Charts and dashboards", "openImageLabel": "Open chart"
    },
    "experience": {
      "title": "Experience", "description": "{name}'s data and analytics experience, most recent first.",
      "eyebrow": "Experience", "heading": "Where I've worked with data",
      "lead": "Most recent first: the questions I owned and what the work changed.",
      "empty": "Experience will appear here once it is added."
    },
    "skills": {
      "title": "Tools & methods", "description": "Tools and methods by area: analysis, data engineering, visualisation and machine learning.",
      "eyebrow": "Tools & methods", "heading": "Tools and methods",
      "lead": "Grouped by what they are used for.", "empty": "Tools and methods will appear here once they are added."
    },
    "certifications": {
      "title": "Certifications", "description": "Courses and certifications completed, in progress and planned.",
      "eyebrow": "Certifications", "heading": "Courses and certifications",
      "lead": "What is completed, what is in progress and what is next.",
      "emptyTitle": "No certifications listed", "emptyBody": "Add courses or certifications to show your learning.",
      "filterLabel": "Filter certifications by issuer",
      "statusEarned": "Completed", "statusInProgress": "Studying", "statusPlanned": "Next up",
      "viewCertificate": "View certificate", "viewCredential": "View details →"
    },
    "blog": {
      "title": "Notes", "description": "Notes on analysis, modelling and communicating results.",
      "eyebrow": "Notes", "heading": "Notes on data",
      "lead": "What I am learning about analysis, modelling and communicating results.", "empty": "Notes coming soon."
    },
    "talks": {
      "title": "Talks", "description": "Talks, workshops and recordings.", "eyebrow": "Talks", "heading": "Talks & workshops",
      "empty": "No talks listed yet. Check back soon.", "watch": "Watch the recording →"
    },
    "contact": {
      "description": "Contact {name}: email, phone or LinkedIn.",
      "heading": "Have a question the data could answer?",
      "lead": "The best ways to reach me about analysis, modelling or reporting work."
    },
    "cv": {
      "description": "Curriculum vitae — {name}, {title}.",
      "experienceHeading": "Professional experience", "skillsHeading": "Tools & methods",
      "projectsHeading": "Projects and results", "certificationsHeading": "Courses and certifications"
    }
  },
  "starter": { "file": "starter.json" }
}
```

For `editor`, copy Software's `editor` block, then change exactly these things (everything else Software-specific that the block's wording test rejects must be reworded the same way):
- `experience.fields.company.label` → "Employer"; `experience.fields.tech.label` → "Tools and methods"; add `experience.fields.summary.hint` → "Describe the questions you owned. Never include client data, personal data or material under NDA."
- `projects.fields.category.hint` → "A subject area such as Forecasting. Used by the filter on the Projects page."; `projects.fields.tech.label` → "Tools"; add `projects.fields.problem.hint` and `projects.fields.approach.hint` → "Never include client data, personal data or material under NDA."
- `talks` → copy the whole `talks` block from `Templates/finance-accounting/template.json` (it hides `youtubeId`).
- `hero.fields.eyebrow.hint` → "A short line above the headline, such as your title and focus. Plain text."
- `metrics.fields.heading.hint` → "Numbers shown under the hero, such as rows processed or forecast accuracy. Only publish figures you can defend. Changes apply as soon as you save."; `metrics.fields.value.hint` → "For example 18 or 2.4M."; `metrics.fields.unit.hint` → "For example % or hours."
- `about.fields.focus.hint` → "One per line, for example the methods you are working with now."
- `blog.itemLabel` → "Note"; `blog.fields.body.label` → "Note text".
- Keep `sections`, `transfer`, `hero.heading`, `testimonials`, `certifications`, `skills` entries (the flow tests read `editor.sections.heading`, `editor.transfer.heading` and `editor.hero.heading`, so those three must exist).

- [ ] **Step 2: Write the starter**

Create `starter.json` in the shape of `Templates/finance-accounting/starter.json` with `"fictional": true` and this content (write the prose yourself, in plain, honest sample language):

| Part | Content |
|---|---|
| Profile | owner "Imani Vale", title "Data Analyst", summary says it is fictional sample content, `email` `imani@example.org`, 4 metrics: `6 yrs "Working with data"`, `14 "" "Analyses delivered"`, `-18 % "Forecast error after the rebuild"`, `30 + "Weekly dashboard users"` |
| Projects (all titles end with "(sample)") | 1. `forecast-rebuild` "Demand forecast rebuild", category "Forecasting", featured, org "Tarnwick", 2 captioned screenshots. 2. `churn-drivers` "Churn drivers study", category "Customer analytics", featured, org "Olliberry", 1 captioned screenshot. 3. `dashboard-rollout` "Operations dashboard rollout", category "Reporting", featured, org "Sundrel". 4. `comment-clustering` "Survey comment clustering", category "Text analysis", not featured, org "Pennagrove". Each has `problem`, `approach`, 2 `impact` lines, `tech` (tools), a `heroImage` and `heroImageAlt`, and one link to `https://example.org/...` |
| Experience | 2 roles, employers "Larkspur Group (fictional)" and "Mossfield Council (fictional)" |
| Skills | 10 skills spread over the 5 `skillGroups` |
| Certifications | 3: one completed, one in progress, one planned, issuers invented ("Example Statistics Society"), `example.org` credential links |
| Testimonials | 2, fictional authors, featured |

- [ ] **Step 3: Write the starter pictures**

Write a throwaway script in your scratchpad (not in the repo) that writes 8 SVG files into `src/Portfolio.Web/wwwroot/img/starter/data-analytics/`: one cover per project (`forecast-rebuild.svg`, `churn-drivers.svg`, `dashboard-rollout.svg`, `comment-clustering.svg`) and three screenshots (`forecast-rebuild-1.svg`, `forecast-rebuild-2.svg`, `churn-drivers-1.svg`). Each is a 1200 x 750 `viewBox` with a gradient background and simple chart-like shapes (bars, a polyline, a donut built from circles and paths) in the teal family. No text, no script, each under 60 KB. Point every `heroImage` and screenshot `url` at `/img/starter/data-analytics/<file>.svg`.

- [ ] **Step 4: Write the theme**

Replace the stub `src/Portfolio.Web/Styles/themes/data-analytics.css` using `finance-accounting.css` as the shape (a header comment, a `:root[data-template="data-analytics"]` block for fonts and radii, a dark block whose selector also lists the bare `:root[data-template=...]`, and a light block). Starting values (change a value only if a contrast test fails):

```css
:root[data-template="data-analytics"] {
  --font-sans: ui-sans-serif, system-ui, -apple-system, "Segoe UI", Roboto, Helvetica, Arial, sans-serif;
  --font-display: ui-sans-serif, system-ui, -apple-system, "Segoe UI", Roboto, Helvetica, Arial, sans-serif;
  --font-mono: ui-monospace, SFMono-Regular, Menlo, Consolas, "Liberation Mono", monospace;
  --radius-lg: 0.5rem;
  --radius-md: 0.375rem;
}
:root[data-template="data-analytics"],
:root[data-template="data-analytics"][data-theme="dark"] {
  --c-bg: 12 20 24; --c-surface: 18 29 34; --c-surface-2: 26 40 46; --c-border: 44 62 70;
  --c-text: 232 242 244; --c-muted: 150 172 178; --c-accent: 45 212 191; --c-accent-strong: 94 234 212;
}
:root[data-template="data-analytics"][data-theme="light"] {
  --c-bg: 246 250 250; --c-surface: 255 255 255; --c-surface-2: 232 242 242; --c-border: 205 220 222;
  --c-text: 15 30 36; --c-muted: 74 96 104; --c-accent: 15 118 110; --c-accent-strong: 17 94 89;
}
```

Write each declaration on its own line in the final file.

- [ ] **Step 5: Run the tests for this key until they pass**

Run: `dotnet test tests/Portfolio.Web.Tests --filter "FullyQualifiedName~ShowcaseArchetypeTests&DisplayName~data-analytics"` and `dotnet test tests/Portfolio.Web.Tests --filter "FullyQualifiedName~ArchetypeTemplateFlowTests&DisplayName~data-analytics"` and `dotnet test tests/Portfolio.Web.Tests --filter "FullyQualifiedName~ThemeStylesheetTests|FullyQualifiedName~TemplateOnboardingManifestTests|FullyQualifiedName~TemplateCatalogTests|FullyQualifiedName~SectionGateTests"`.
Expected: every `data-analytics` case passes except `TheThumbnail_IsARealScreenshotOfTheRightSize` (the thumbnail is Task 4) and the tests that need the other key (`ShipsExactlyTheShippedTemplates`, the wizard card count). Check the reported counts are non-zero. Any failure about wording identical to Software's, an editor word, or contrast is yours to fix in the files you own.

- [ ] **Step 6: Commit**

```bash
git checkout -- src/Portfolio.Web/wwwroot/css/app.css
git add src/Portfolio.Web/Templates/data-analytics src/Portfolio.Web/Styles/themes/data-analytics.css src/Portfolio.Web/wwwroot/img/starter/data-analytics
git commit -m "feat: Data & Analytics template on the showcase layout

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

Report: the commit hash, the branch, the exact test commands run with their pass counts, and every value you had to change from the starting values above.

---

### Task 3: Product / Project Management (subagent B, own worktree)

Dispatch in the same message as Task 2, with the same settings and the same first-line instruction.

**Files (this task owns only these):**
- Create: `src/Portfolio.Web/Templates/product-management/template.json`
- Create: `src/Portfolio.Web/Templates/product-management/starter.json`
- Modify: `src/Portfolio.Web/Styles/themes/product-management.css` (replace the stub)
- Create: `src/Portfolio.Web/wwwroot/img/starter/product-management/*.svg`
- Do not touch: `preview.png` (Task 4), `wwwroot/css/app.css`, any test file, README, docs.

**Interfaces:**
- Consumes and produces: as Task 2, with key `product-management`.

- [ ] **Step 1: Write the manifest**

Create `template.json` starting from `Templates/software/template.json` (same key set) with these values:

```jsonc
{
  "$schema": "../template.schema.json",
  "key": "product-management",
  "name": "Product & Project Management",
  "profession": "Product or project manager",
  "description": "Showcase for product and project managers: shipped work, the outcomes it produced and how it was delivered.",
  "archetype": "showcase",
  "theme": {
    "default": "product-management",
    "themeColor": "#141312",
    "accents": [
      { "key": "amber", "label": "Amber", "dark": { "accent": "#fbbf24", "strong": "#fcd34d" }, "light": { "accent": "#a84a09", "strong": "#92400e" } },
      { "key": "blue",  "label": "Blue",  "dark": { "accent": "#60a5fa", "strong": "#93c5fd" }, "light": { "accent": "#1d4ed8", "strong": "#1e40af" } },
      { "key": "green", "label": "Green", "dark": { "accent": "#34d399", "strong": "#6ee7b7" }, "light": { "accent": "#047857", "strong": "#065f46" } },
      { "key": "rose",  "label": "Rose",  "dark": { "accent": "#fb7185", "strong": "#fda4af" }, "light": { "accent": "#be123c", "strong": "#9f1239" } }
    ]
  },
  "heroSuggestions": {
    "title": "Product Manager",
    "summary": "Product manager who turns customer problems into shipped outcomes, aligning teams and stakeholders around clear goals and measurable results."
  },
  "sections": [
    { "key": "about", "label": "about" },
    { "key": "experience", "label": "experience" },
    { "key": "projects", "label": "shipped work", "shortLabel": "shipped", "itemLabel": "Release" },
    { "key": "skills", "label": "skills & methods", "shortLabel": "skills" },
    { "key": "certifications", "label": "certifications", "shortLabel": "certs" },
    { "key": "blog", "label": "writing", "itemLabel": "Article" },
    { "key": "talks", "label": "talks", "enabled": false },
    { "key": "testimonials", "label": "testimonials" },
    { "key": "contact", "label": "contact" }
  ],
  "skillGroups": ["Discovery", "Delivery", "Stakeholders", "Metrics", "Tools"],
  "seo": {
    "title": "{name} — {title}",
    "description": "{name}, {title}. Shipped work, the outcomes it produced and how the team got there.",
    "aboutDescription": "About {name}, {title}: how products and projects get from problem to measured outcome."
  },
  "copy": {
    "layout": { "footerNote": "Product and project delivery." },
    "home": {
      "workEyebrow": "Shipped work", "workHeading": "Outcomes delivered",
      "workEmptyTitle": "Shipped work coming soon",
      "workEmptyBody": "Add your featured releases and the outcome of each will appear here.",
      "testimonialsEyebrow": "Kind words", "testimonialsHeading": "Feedback from teams and stakeholders",
      "contactHeading": "Have a problem worth shipping a fix for?",
      "experienceEyebrow": "Experience", "experienceHeading": "Where I've led delivery",
      "credentialsEyebrow": "Learning and credentials", "credentialsHeading": "Courses and certifications",
      "skillsHeading": "Skills and methods"
    },
    "projects": {
      "title": "Shipped work", "description": "Shipped work: the problem, the approach and the measured outcome.",
      "eyebrow": "Shipped work", "heading": "Outcomes delivered",
      "lead": "Each release states the problem, how the team approached it and what changed.",
      "emptyTitle": "Nothing shipped yet", "emptyBody": "Add your first release to show the problem, the approach and the outcome.",
      "filterLabel": "Filter shipped work by area", "featuredBadge": "featured",
      "problemHeading": "The problem", "approachHeading": "How we got there",
      "noWriteup": "A full write-up of this release is on the way.",
      "stackHeading": "Tools", "linksHeading": "Links", "proofHeading": "Evidence",
      "screenshotsHeading": "Screens and artefacts", "imageAlt": "{title} image",
      "galleryLabel": "Screens and artefacts", "openImageLabel": "Open image"
    },
    "experience": {
      "title": "Experience", "description": "{name}'s product and project experience, most recent first.",
      "eyebrow": "Experience", "heading": "Where I've led delivery",
      "lead": "Most recent first: the teams, the goals and the outcomes.",
      "empty": "Experience will appear here once it is added."
    },
    "skills": {
      "title": "Skills & methods", "description": "Skills and methods by area: discovery, delivery, stakeholders and metrics.",
      "eyebrow": "Skills & methods", "heading": "Skills and methods",
      "lead": "Grouped by the part of the work they serve.", "empty": "Skills will appear here once they are added."
    },
    "certifications": {
      "title": "Certifications", "description": "Courses and certifications held, in progress and planned.",
      "eyebrow": "Certifications", "heading": "Courses and certifications",
      "lead": "What is held, what is in progress and what is next.",
      "emptyTitle": "No certifications listed", "emptyBody": "Add courses or certifications to show your learning.",
      "filterLabel": "Filter certifications by issuer",
      "statusEarned": "Certified", "statusInProgress": "Studying", "statusPlanned": "Next up",
      "viewCertificate": "View certificate", "viewCredential": "View details →"
    },
    "blog": {
      "title": "Writing", "description": "Writing on product discovery, delivery and decisions.",
      "eyebrow": "Writing", "heading": "Notes on product and delivery",
      "lead": "What I am learning about discovery, delivery and decisions.", "empty": "Articles coming soon."
    },
    "talks": {
      "title": "Talks", "description": "Talks, workshops and recordings.", "eyebrow": "Talks", "heading": "Talks & workshops",
      "empty": "No talks listed yet. Check back soon.", "watch": "Watch the recording →"
    },
    "contact": {
      "description": "Contact {name}: email, phone or LinkedIn.",
      "heading": "Let us talk about what to build next.",
      "lead": "The best ways to reach me about product, delivery or advisory conversations."
    },
    "cv": {
      "description": "Curriculum vitae — {name}, {title}.",
      "experienceHeading": "Professional experience", "skillsHeading": "Skills & methods",
      "projectsHeading": "Shipped work", "certificationsHeading": "Courses and certifications"
    }
  },
  "starter": { "file": "starter.json" }
}
```

For `editor`, copy Software's `editor` block and change exactly these things (anything else Software-specific that the wording test rejects is reworded the same way):
- `experience.fields.company.label` → "Employer"; `experience.fields.tech.label` → "Tools and methods"; add `experience.fields.summary.hint` → "Describe the teams, goals and outcomes. Never include confidential roadmaps, customer data or figures you are not allowed to share."
- `projects.itemLabel` is taken from the section ("Release"); `projects.fields.category.hint` → "An area such as Growth or Platform. Used by the filter on the Shipped work page."; `projects.fields.tech.label` → "Tools"; add `projects.fields.problem.hint` and `projects.fields.approach.hint` → "Never include confidential roadmaps, customer data or figures you are not allowed to share."
- `talks` → copy the whole `talks` block from `Templates/finance-accounting/template.json` (it hides `youtubeId`).
- `hero.fields.eyebrow.hint` → "A short line above the headline, such as your title and the products you work on. Plain text."
- `metrics.fields.heading.hint` → "Numbers shown under the hero, such as adoption, revenue or cycle time. Only publish figures you can defend. Changes apply as soon as you save."; `metrics.fields.value.hint` → "For example 22 or 1.4M."; `metrics.fields.unit.hint` → "For example % or days."
- `about.fields.focus.hint` → "One per line, for example the problems you are working on now."
- `blog.itemLabel` → "Article"; `blog.fields.body.label` → "Article text".
- Keep `sections`, `transfer`, `hero.heading`, `testimonials`, `certifications`, `skills` entries (the flow tests read `editor.sections.heading`, `editor.transfer.heading` and `editor.hero.heading`).

- [ ] **Step 2: Write the starter**

Create `starter.json` in the shape of `Templates/finance-accounting/starter.json` with `"fictional": true` and:

| Part | Content |
|---|---|
| Profile | owner "Tomasz Rhee", title "Product Manager", summary says it is fictional sample content, `email` `tomasz@example.org`, 4 metrics: `8 yrs "Shipping products"`, `11 "" "Releases led"`, `+22 % "Activation after the onboarding redesign"`, `4 "" "Teams aligned per release"` |
| Projects (all titles end with "(sample)") | 1. `onboarding-redesign` "Onboarding redesign", category "Growth", featured, org "Halvorne", 2 captioned screenshots. 2. `billing-migration` "Billing migration programme", category "Delivery", featured, org "Brindlecott", 1 captioned screenshot. 3. `roadmap-reset` "Roadmap reset", category "Strategy", featured, org "Wenlowe". 4. `support-portal` "Self-service support portal", category "Platform", not featured, org "Kestrelbay". Each has `problem`, `approach`, 2 `impact` lines, `tech` (tools), a `heroImage` and `heroImageAlt`, and one link to `https://example.org/...` |
| Experience | 2 roles, employers "Fenwick Works (fictional)" and "Aldermere Trust (fictional)" |
| Skills | 10 skills spread over the 5 `skillGroups` |
| Certifications | 3: one certified, one in progress, one planned, issuers invented ("Example Delivery Institute"), `example.org` credential links |
| Testimonials | 2, fictional authors, featured |

- [ ] **Step 3: Write the starter pictures**

Write a throwaway script in your scratchpad (not in the repo) that writes 8 SVG files into `src/Portfolio.Web/wwwroot/img/starter/product-management/`: covers `onboarding-redesign.svg`, `billing-migration.svg`, `roadmap-reset.svg`, `support-portal.svg` and screenshots `onboarding-redesign-1.svg`, `onboarding-redesign-2.svg`, `billing-migration-1.svg`. Each is a 1200 x 750 `viewBox` with a warm gradient background and simple product-like shapes (a kanban board of rounded rectangles, a timeline of dots on a line, a stepped funnel) in the amber and slate family. No text, no script, each under 60 KB. Point every `heroImage` and screenshot `url` at `/img/starter/product-management/<file>.svg`.

- [ ] **Step 4: Write the theme**

Replace the stub `src/Portfolio.Web/Styles/themes/product-management.css` in the same shape as `finance-accounting.css`. Starting values (change a value only if a contrast test fails):

```css
:root[data-template="product-management"] {
  --font-sans: ui-sans-serif, system-ui, -apple-system, "Segoe UI", Roboto, Helvetica, Arial, sans-serif;
  --font-display: ui-sans-serif, system-ui, -apple-system, "Segoe UI", Roboto, Helvetica, Arial, sans-serif;
  --font-mono: ui-sans-serif, system-ui, -apple-system, "Segoe UI", Roboto, Helvetica, Arial, sans-serif;
  --radius-lg: 0.625rem;
  --radius-md: 0.5rem;
}
:root[data-template="product-management"],
:root[data-template="product-management"][data-theme="dark"] {
  --c-bg: 20 19 18; --c-surface: 29 28 26; --c-surface-2: 41 39 36; --c-border: 66 63 58;
  --c-text: 244 241 236; --c-muted: 168 162 152; --c-accent: 251 191 36; --c-accent-strong: 252 211 77;
}
:root[data-template="product-management"][data-theme="light"] {
  --c-bg: 250 248 244; --c-surface: 255 255 255; --c-surface-2: 240 236 228; --c-border: 220 214 203;
  --c-text: 28 25 23; --c-muted: 87 83 78; --c-accent: 168 74 9; --c-accent-strong: 146 64 14;
}
```

Write each declaration on its own line in the final file.

- [ ] **Step 5: Run the tests for this key until they pass**

Run: `dotnet test tests/Portfolio.Web.Tests --filter "FullyQualifiedName~ShowcaseArchetypeTests&DisplayName~product-management"` and `dotnet test tests/Portfolio.Web.Tests --filter "FullyQualifiedName~ArchetypeTemplateFlowTests&DisplayName~product-management"` and `dotnet test tests/Portfolio.Web.Tests --filter "FullyQualifiedName~ThemeStylesheetTests|FullyQualifiedName~TemplateOnboardingManifestTests|FullyQualifiedName~TemplateCatalogTests|FullyQualifiedName~SectionGateTests"`.
Expected: every `product-management` case passes except `TheThumbnail_IsARealScreenshotOfTheRightSize` (Task 4) and the tests that need the other key. Check the reported counts are non-zero. Fix wording, editor or contrast failures in the files you own.

- [ ] **Step 6: Commit**

```bash
git checkout -- src/Portfolio.Web/wwwroot/css/app.css
git add src/Portfolio.Web/Templates/product-management src/Portfolio.Web/Styles/themes/product-management.css src/Portfolio.Web/wwwroot/img/starter/product-management
git commit -m "feat: Product & Project Management template on the showcase layout

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

Report: the commit hash, the branch, the exact test commands run with their pass counts, and every value you had to change from the starting values above.

---

### Task 4: Integrate, thumbnails, docs, verification (lead, this worktree)

**Files:**
- Modify: `src/Portfolio.Web/wwwroot/css/app.css` (generated)
- Create: `src/Portfolio.Web/Templates/data-analytics/preview.png`, `src/Portfolio.Web/Templates/product-management/preview.png`
- Modify: `README.md:45-55`, `docs/template-authoring-guide.md:16` and section 5, `docs/templates-and-onboarding-plan.md` (Phase 5 status, item 4, "What remains")

- [ ] **Step 1: Bring both subagent commits in**

Run `git cherry-pick <hash from Task 2>` then `git cherry-pick <hash from Task 3>` (hashes from their reports). The files do not overlap, so there is no conflict. If one appears, stop and read it: a subagent strayed outside its files.
Check: `git diff --stat 4c94c99..HEAD` lists only the files in the File Structure table (plus the spec and plan). Anything else is a defect to undo.

- [ ] **Step 2: Build the stylesheet from an LF export and commit it**

```bash
SCRATCH=<scratchpad>/lf-build && rm -rf "$SCRATCH" && mkdir -p "$SCRATCH"
git -c core.autocrlf=false -c core.eol=lf archive HEAD | tar -x -C "$SCRATCH"
cd "$SCRATCH" && dotnet build src/Portfolio.Web -p:TailwindRequired=true
cp "$SCRATCH/src/Portfolio.Web/wwwroot/css/app.css" <this worktree>/src/Portfolio.Web/wwwroot/css/app.css
```
Expected: build succeeds. `git diff --stat -- src/Portfolio.Web/wwwroot/css/app.css` shows additions that mention `data-template="data-analytics"` and `data-template="product-management"` only (no edits to other templates' rules). Commit: `git commit -am "build: regenerate the stylesheet with the two showcase themes"` (with the Co-Authored-By line).

- [ ] **Step 3: Take the thumbnails**

Use the `run` skill to start the app, sign in, open `/admin/template`, switch to Data & Analytics, publish its sample content from the wizard, then capture `/` in dark mode at exactly 1200 x 800 with the `superpowers-chrome:browsing` skill. Save it as `src/Portfolio.Web/Templates/data-analytics/preview.png`. Repeat for Product / Project Management. Each file is over 30 KB and under about 600 KB. Switch the site back to Software afterwards.
Run: `dotnet test tests/Portfolio.Web.Tests --filter "FullyQualifiedName~TheThumbnail_IsARealScreenshotOfTheRightSize"`. Expected: all thumbnail cases pass, including the four existing archetypes' and the two new ones.

- [ ] **Step 4: Update the docs**

- `README.md`: change "Seven ship today" to "Nine ship today" and add after the Finance sentence: "**Data & Analytics** and **Product & Project Management** use the same showcase layout as Software & IT, each with its own wording, theme, accent choices and fictional starter content."
- `docs/template-authoring-guide.md` line 16: add "`showcase` (Software & IT, Data & Analytics, Product & Project Management)". Add under section 5 a short "Adding a showcase template" paragraph: add the key to `ShowcaseArchetypeTests.Keys`, the hard-coded lists in `TemplateManifestTests`, the wizard card counts in `OnboardingFlowTests`, write the manifest, starter, theme, pictures and a real screenshot thumbnail; the layout needs no change; the hero's "LinkedIn", "GitHub" and "Download CV" words and the waveform backdrop are fixed and shared with Software.
- `docs/templates-and-onboarding-plan.md`: set item 4 to "Done (pending independent review)", replace the three "What remains in Phase 5" paragraphs' Data & Analytics and Product / Project Management clause with "none; Education is in `feature/phase-5d-education-template`", and add a "Phase 5 step 4" notes section with the branch, the two keys, the known gap (Showcase hero words and backdrop), the test results from Step 5 and the real-browser findings from Step 6.
Commit: `git commit -am "docs: record the showcase templates"` (with the Co-Authored-By line).

- [ ] **Step 5: Run the whole suite on an LF export**

Repeat the LF export from Step 2 into a fresh scratch folder (HEAD now includes the thumbnails and docs) and run `dotnet test tests/Portfolio.Web.Tests` and `dotnet test` for any other test project in `Portfolio.slnx`. Expected: everything passes. On the CRLF host `SoftwareParityTests.DemoPersona_RendersByteForByteLikeTheSnapshots` and `OnboardingAccessibilityAccentTests.WithNoAccentPicked_ThePublicPageHasNoAccentOverride` fail only when run from the CRLF working copy, so they must pass in the LF export. Report the real counts. Also record: the before and after render of 14 routes for Software, Healthcare, Design & UX, Photography & Film, Marketing, Sales and Finance, compared against the same renders taken from commit `4c94c99`, identical after normalising asset fingerprints (`DemoSite.Normalise`).

- [ ] **Step 6: Mutation-test the new guards**

In a scratch copy (never in this worktree), break each guard and confirm a named test fails, then discard the copy:
1. Delete `"heading"` from `copy.projects` in one template: `ShowcaseArchetypeTests` or the manifest validation fails.
2. Swap `skills` and `blog` in `sections`: `TheMenuOrder_IsTheShowcaseOrder_...` fails.
3. Set one `light.accent` to `#d1d5db`: the accent-contrast test fails.
4. Change `"enabled": false` on `talks` to true: `TheMenuOrder_IsTheShowcaseOrder_...` fails.
5. Rename one starter org to `Tarnwick Ltd`: `TheStarterContent_IsFictional_...` fails.
6. Remove `"hidden": true` from `youtubeId`: `TheMenuOrder_IsTheShowcaseOrder_...` fails.
7. Replace one `copy.home.workHeading` with Software's `Case studies`: `AShowcaseTemplate_DoesNotInheritSoftwareWording` fails.
Report which test caught each.

- [ ] **Step 7: Real-browser pass**

With the app running, at 390, 768 and 1280 px wide, in light and dark: the wizard card (thumbnail, profession heading), publish sample content for each template, `/`, `/projects`, a case study, `/experience`, `/skills`, `/certifications`, `/blog`, `/about`, `/contact`, `/cv`, switching to the template and back to Software (rows unchanged), the sections editor, export and import, and keyboard focus through the menu and the project filter. Record anything off in the plan doc's notes. A finding is reported, not hidden.

- [ ] **Step 8: Independent review and handoff**

Dispatch the `quality-reviewer` agent with the range `4c94c99..HEAD`, the spec path and the acceptance criteria in section 5 of the spec, and ask it for correctness bugs, silent failures, missing tests and unreachable features. Fix anything real, rerun the affected tests, and report the final state: what ran, what passed, what was not run, and the path a person follows to see the change (sign in at `/admin/login`, Appearance, Template, choose the template, publish sample content, open `/`, `/projects`, `/experience`, `/cv`). Do not push or open the pull request unless asked; when asked, base it on `feature/phase-5c-finance-template` and attach desktop and mobile screenshots, and state the archetype in the description.
