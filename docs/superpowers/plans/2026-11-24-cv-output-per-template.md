# CV output per template (Phase 6a) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `/cv` (HTML) and `/cv.pdf` (QuestPDF) follow the active template: section order, accent colour, and certification status words come from the template manifest.

**Architecture:** `copy.cv` gains two optional manifest fields, `order` and `accent`. `PortfolioContentService.BuildCvDataAsync` resolves them (plus the owner's picked accent) into two new `CvData` members, `SectionOrder` and `Accent`. Both renderers loop over `SectionOrder` instead of a fixed sequence and read `Accent` instead of a constant. Work is strictly sequential in this worktree: each task leaves the suite green and ends in one commit.

**Tech Stack:** ASP.NET Core Razor Pages (.NET 10), QuestPDF, xUnit, JSON manifests with a JSON schema file.

Spec: `docs/superpowers/specs/2026-11-22-cv-output-per-template-design.md`. Read it first, then **"Where this plan departs from the spec"** below. The plan is right where the two differ, because each departure was measured on this branch.

## Global Constraints

- Work only in the worktree `e:\Repos\living-cv\.claude\worktrees\cv-per-template` (branch `feature/phase-6a-cv-per-template`). Never switch branches in `e:\Repos\living-cv` (other sessions share it). Never push, merge or deploy.
- Default CV section order is `experience, skills, projects, certifications` (today's order). Default accent is `#1D4ED8` (today's PDF colour). The summary is always first and is not part of `order`.
- `copy.cv.order` is a subset of `experience, skills, projects, certifications`, no duplicates. `copy.cv.accent` is `#rrggbb`. Both optional; all nine shipped manifests set both.
- Accent resolution: the owner's picked `theme.accents[]` option, using its `light.strong`; otherwise `copy.cv.accent`; otherwise `#1D4ED8`.
- Every `copy.cv.accent` and every accent option's `light.strong` reaches 4.5:1 contrast on white.
- Manifests are loaded with `UnmappedMemberHandling.Disallow` and checked against `Templates/template.schema.json` by a drift test. A manifest field must exist in `CvCopy` **and** in the schema before any manifest uses it.
- Do not add Tailwind utility classes anywhere. `wwwroot/css/app.css` must not change.
- `wwwroot/css/app.css` is rewritten by every local `dotnet build` on this Windows host (CRLF). Before every commit run `git checkout -- src/Portfolio.Web/wwwroot/css/app.css`. Never `git add -A` or `git commit -a`; add files by name.
- On this host `SoftwareParityTests.DemoPersona_RendersByteForByteLikeTheSnapshots` already fails on all 14 pages at the parent commit (fingerprinted asset names, CRLF). Parity is therefore proven by diffing renders against a baseline taken from the parent commit (Task 0 and Task 4), not by that test's pass or fail. `OnboardingAccessibilityAccentTests.WithNoAccentPicked_ThePublicPageHasNoAccentOverride` is the other known baseline failure.
- Test command (worktree root): `dotnet test tests/Portfolio.Web.Tests --filter "<expr>"`. A pass that ran zero tests is not a pass: check the reported count.
- Commit messages end with the line `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`.
- Keep diffs minimal and match surrounding code.

## Where this plan departs from the spec

Measured on this branch while writing the plan. Task 5 amends the spec to match.

1. **Software's `/cv` snapshot changes by exactly one line.** The spec says `SoftwareParityTests` passes with no snapshot edit. But the bundled fix "HTML shows the template's certification status word" turns `Microsoft (InProgress)` into `Microsoft (In progress)` in `Snapshots/software/cv.html` (line 216; `Earned` and `Planned` are unchanged because Software's words equal the enum names). That one line is the only permitted snapshot edit.
2. **Software's PDF headings change case.** The spec says Software output is unchanged byte for byte. Dropping `ToUpperInvariant()` (bundled fix) makes Software's PDF print `Selected projects` instead of `SELECTED PROJECTS`. Software's HTML is byte-identical apart from item 1. The PDF is not snapshot-tested.
3. **`/cv.pdf` is not byte-deterministic.** QuestPDF embeds a creation time (`/CreationDate (D:2026…)`), so two renders more than a second apart differ. PDF tests compare bytes after masking `D:\d{14}[^)]*\)`; this was verified (masked renders of equal input are equal). "Deterministic for equal input" holds in production only through the output cache.
4. **The HTML half of the accent rule already works.** The layout injects the owner's picked accent as CSS variables (`TemplateView.AccentStyle`), so `/cv` HTML follows the pick today. `copy.cv.accent` is a print colour used only by the PDF. The HTML gets a characterisation test, not new code.
5. **Razor must keep Software's bytes.** Razor prints source indentation literally, so wrapping the four `/cv` section blocks in a loop and re-indenting them changes the output. A structure that keeps all 15 snapshot pages byte-identical was proven before this plan was written: markup stays at its original column, and `@:` at column 0 prints each blank separator line. Task 4 reproduces it exactly.
6. **Proposed CV orders and accents (content decision, not in the spec).** Rule: a template's CV order is its own `sections[]` order restricted to the four CV sections; Software stays on today's order. Accent: the template's light-mode `--c-accent-strong` from its theme stylesheet (all pass 4.5:1 on white). The table below is what Task 2 ships. **The owner should confirm it** (changing a row later is a one-line manifest edit plus the expectation table).

| Template key | `copy.cv.order` | `copy.cv.accent` |
|---|---|---|
| software | experience, skills, projects, certifications | `#1D4ED8` |
| data-analytics | experience, projects, skills, certifications | `#115e59` |
| product-management | experience, projects, skills, certifications | `#92400e` |
| design-ux | projects, experience, skills, certifications | `#92320e` |
| photography-film | projects, experience, skills, certifications | `#6b4608` |
| marketing-content | projects, experience, skills, certifications | `#9d174d` |
| sales-bd | projects, experience, skills, certifications | `#065f46` |
| finance-accounting | experience, certifications, projects, skills | `#15375c` |
| healthcare | certifications, experience, skills, projects | `#115e59` |

No non-Software accent may equal `#1D4ED8`: the three archetype wording tests (`AShowcaseTemplate_DoesNotInheritSoftwareWording` and its Gallery and Results twins) fail when a string in `copy` equals Software's.

## File Structure

| File | Task | Responsibility |
|---|---|---|
| `src/Portfolio.Web/Services/CvSections.cs` (create) | 1 | The four CV section keys, default order, default accent |
| `src/Portfolio.Web/Services/TemplateDefinition.cs` (modify `CvCopy`) | 1 | `Order`, `Accent`, `OrderList()`, `AccentText()` |
| `src/Portfolio.Web/Services/TemplateValidator.cs` (modify) | 1 | `ValidateCv`: known keys, no repeats, hex accent |
| `src/Portfolio.Web/Templates/template.schema.json` (modify) | 1 | `copy.cv.order` and `copy.cv.accent` |
| `src/Portfolio.Web/Templates/*/template.json` (9, modify) | 2 | Each template's `order` and `accent` |
| `tests/Portfolio.Web.Tests/Support/Contrast.cs` (create) | 1 | WCAG contrast ratio helper |
| `tests/Portfolio.Web.Tests/Templates/CvManifestTests.cs` (create) | 1, 2 | Validator rules, expectation table, contrast on white |
| `src/Portfolio.Web/Services/CvDocument.cs` (modify `CvData`, `CvHeadings`) | 3 | `SectionOrder`, `Accent`, `StatusWord` |
| `src/Portfolio.Web/Services/TemplateView.cs` (modify) | 3 | `CvAccent` |
| `src/Portfolio.Web/Services/PortfolioContentService.cs` (modify) | 3 | Fill the new `CvData` members |
| `tests/Portfolio.Web.Tests/Templates/CvDataTests.cs` (create) | 3 | Order, disabled sections, accent resolution (no database) |
| `src/Portfolio.Web/Services/CvDocument.cs` (modify `CvDocument`) | 4 | Loop over `SectionOrder`, instance accent, headings as written |
| `src/Portfolio.Web/Pages/Cv.cshtml` (modify) | 4 | Loop over `SectionOrder`, template status words |
| `tests/Portfolio.Web.Tests/Snapshots/software/cv.html` (modify, one line) | 4 | `(InProgress)` to `(In progress)` |
| `tests/Portfolio.Web.Tests/Templates/CvOutputTests.cs` (create) | 4 | HTML order, status words, picked accent; PDF order and accent |
| `docs/template-authoring-guide.md`, `docs/templates-and-onboarding-plan.md`, the spec | 5 | Documentation and spec amendments |

---

### Task 0: Baseline renders from the parent commit

**Files:** none changed. Output goes to a folder outside the repo.

**Interfaces:**
- Produces: a folder `BASE` of 15 rendered pages (`cv.html`, `home.html`, ...) that Task 4 diffs against.

- [ ] **Step 1: Confirm the starting state**

Run: `git status --short && git log --oneline -1`
Expected: no output from `git status`; HEAD is `9488463` (the spec commit, or a later docs-only commit).

- [ ] **Step 2: Dump the baseline**

```bash
BASE=<scratchpad>/cv6a-base && rm -rf "$BASE" && mkdir -p "$BASE"
SNAPSHOT_ACTUAL_DIR="$BASE" dotnet test tests/Portfolio.Web.Tests --filter "FullyQualifiedName~DemoPersona_RendersByteForByteLikeTheSnapshots"
git checkout -- src/Portfolio.Web/wwwroot/css/app.css
ls "$BASE" | wc -l
```
Expected: the test reports `Failed: 1` (known baseline failure), and `ls | wc -l` prints `15`. Keep `$BASE` for Task 4. Write its absolute path down in the task notes.

- [ ] **Step 3: Record the baseline of the suites this plan touches**

Run: `dotnet test tests/Portfolio.Web.Tests --filter "FullyQualifiedName~TemplateManifestTests|FullyQualifiedName~ArchetypeTests|FullyQualifiedName~TemplateOnboardingManifestTests"` then `git checkout -- src/Portfolio.Web/wwwroot/css/app.css`.
Expected: all pass. Note the passed count; Task 1 and 2 must not lower it.

No commit.

---

### Task 1: Manifest model, validator and schema (no manifest uses the fields yet)

**Files:**
- Create: `src/Portfolio.Web/Services/CvSections.cs`
- Modify: `src/Portfolio.Web/Services/TemplateDefinition.cs` (the `CvCopy` class, currently lines 460-467)
- Modify: `src/Portfolio.Web/Services/TemplateValidator.cs` (call site after `ValidateCopy(t, errors);` at line 72; new method next to `ValidateAccents`)
- Modify: `src/Portfolio.Web/Templates/template.schema.json` (the `cv` object, currently lines 598-622)
- Create: `tests/Portfolio.Web.Tests/Support/Contrast.cs`
- Create: `tests/Portfolio.Web.Tests/Templates/CvManifestTests.cs`

**Interfaces:**
- Produces:
  - `CvSections.Default : IReadOnlyList<string>` (`experience, skills, projects, certifications`), `CvSections.DefaultAccent : string` (`"#1D4ED8"`), `CvSections.IsKnown(string?) : bool`.
  - `CvCopy.Order : List<string>?`, `CvCopy.Accent : string` (`[OptionalCopy]`, default `""`), `CvCopy.OrderList() : IReadOnlyList<string>`, `CvCopy.AccentText() : string`.
  - Test helpers `CvManifestTests.Load(string key) : TemplateDefinition` (internal static) and `Contrast.OnWhite(string hex) : double`.

- [ ] **Step 1: Write the contrast helper**

Create `tests/Portfolio.Web.Tests/Support/Contrast.cs`:

```csharp
using System.Globalization;

namespace Portfolio.Web.Tests.Support;

/// <summary>WCAG contrast ratio between two #rrggbb colours.</summary>
public static class Contrast
{
    public static double OnWhite(string hex) => Ratio(hex, "#ffffff");

    public static double Ratio(string a, string b)
    {
        var (la, lb) = (Luminance(a), Luminance(b));
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double Luminance(string hex)
    {
        static double Channel(string h, int at) { var s = int.Parse(h.AsSpan(at, 2), NumberStyles.HexNumber) / 255.0; return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
        return 0.2126 * Channel(hex, 1) + 0.7152 * Channel(hex, 3) + 0.0722 * Channel(hex, 5);
    }
}
```

- [ ] **Step 2: Write the failing validator tests**

Create `tests/Portfolio.Web.Tests/Templates/CvManifestTests.cs`:

```csharp
using System.Text.Json;
using Portfolio.Web.Services;
using Portfolio.Web.Tests.Support;
using Xunit;

namespace Portfolio.Web.Tests.Templates;

/// <summary>The CV fields of the manifest (copy.cv.order and copy.cv.accent): what the validator accepts, and what every shipped template sets.</summary>
public class CvManifestTests
{
    private static string TemplatesDir => Path.Combine(AppContext.BaseDirectory, "Templates");

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };

    internal static TemplateDefinition Load(string key) =>
        JsonSerializer.Deserialize<TemplateDefinition>(File.ReadAllText(Path.Combine(TemplatesDir, key, "template.json")), Json)!;

    private static List<string> Errors(Action<CvCopy> change)
    {
        var t = Load("software");
        change(t.Copy.Cv);
        return TemplateValidator.Validate(t, "software");
    }

    [Fact]
    public void TheDefaults_AreTodaysOrderAndTodaysPdfColour()
    {
        Assert.Equal(new[] { "experience", "skills", "projects", "certifications" }, CvSections.Default);
        Assert.Equal("#1D4ED8", CvSections.DefaultAccent);
        var cv = new CvCopy();
        Assert.Equal(CvSections.Default, cv.OrderList());
        Assert.Equal(CvSections.DefaultAccent, cv.AccentText());
    }

    [Fact]
    public void AnOrderIsHonoured_AndASubsetMayLeaveSectionsOut()
    {
        Assert.Equal(new[] { "projects", "experience" }, new CvCopy { Order = ["projects", "experience"] }.OrderList());
        Assert.Equal("#115e59", new CvCopy { Accent = "#115e59" }.AccentText());
    }

    [Fact]
    public void Validator_AcceptsNoOrderAndNoAccent_AndAnyKnownSubset()
    {
        Assert.Empty(Errors(cv => { cv.Order = null; cv.Accent = ""; }));
        Assert.Empty(Errors(cv => cv.Order = ["certifications", "experience"]));
        Assert.Empty(Errors(cv => cv.Accent = "#1D4ED8"));
        Assert.Empty(Errors(cv => cv.Accent = "#abcdef"));
    }

    [Fact]
    public void Validator_RejectsASectionKeyTheCvCannotShow()
    {
        var errors = Errors(cv => cv.Order = ["experience", "blog"]);
        Assert.Contains(errors, e => e.Contains("copy.cv.order") && e.Contains("'blog'"));
        Assert.Contains(Errors(cv => cv.Order = ["about"]), e => e.Contains("copy.cv.order") && e.Contains("'about'"));
        Assert.Contains(Errors(cv => cv.Order = ["Experience"]), e => e.Contains("copy.cv.order") && e.Contains("'Experience'"));
    }

    [Fact]
    public void Validator_RejectsARepeatedKey_AndABlankEntry()
    {
        Assert.Contains(Errors(cv => cv.Order = ["experience", "skills", "experience"]), e => e.Contains("copy.cv.order") && e.Contains("'experience'") && e.Contains("twice"));
        Assert.Contains(Errors(cv => cv.Order = ["experience", ""]), e => e.Contains("copy.cv.order"));
        Assert.Contains(Errors(cv => cv.Order = ["experience", null!]), e => e.Contains("copy.cv.order"));
    }

    [Theory]
    [InlineData("red")]
    [InlineData("#12345")]
    [InlineData("#1234567")]
    [InlineData("#gggggg")]
    [InlineData("1D4ED8")]
    [InlineData("url(#x)")]
    [InlineData("#1D4ED8;}")]
    public void Validator_RejectsAnAccentThatIsNotSixDigitHex(string accent) =>
        Assert.Contains(Errors(cv => cv.Accent = accent), e => e.Contains("copy.cv.accent"));
}
```

- [ ] **Step 3: Run to verify it fails to compile**

Run: `dotnet test tests/Portfolio.Web.Tests --filter "FullyQualifiedName~CvManifestTests"`
Expected: build FAILS with `CS0103: The name 'CvSections' does not exist` and `CS1061: 'CvCopy' does not contain a definition for 'Order'` / `'Accent'`. (Compile failure is the red state here.)

- [ ] **Step 4: Create `CvSections`**

Create `src/Portfolio.Web/Services/CvSections.cs`:

```csharp
namespace Portfolio.Web.Services;

/// <summary>The sections a CV can show after its summary, and what a template that says nothing about them gets.</summary>
public static class CvSections
{
    /// <summary>Today's order, and the order of a template whose manifest has no <c>copy.cv.order</c>.</summary>
    public static readonly IReadOnlyList<string> Default =
        [SectionRegistry.Experience, SectionRegistry.Skills, SectionRegistry.Projects, SectionRegistry.Certifications];

    /// <summary>Today's PDF colour, and the colour of a template whose manifest has no <c>copy.cv.accent</c>.</summary>
    public const string DefaultAccent = "#1D4ED8";

    public static bool IsKnown(string? key) => key is not null && Default.Contains(key, StringComparer.Ordinal);
}
```

- [ ] **Step 5: Extend `CvCopy`**

In `src/Portfolio.Web/Services/TemplateDefinition.cs`, replace the end of `CvCopy` (after `CertificationsHeading`):

```csharp
    public string CertificationsHeading { get; set; } = "";

    /// <summary>Optional. The CV sections after the summary, in print order: a subset of <see cref="CvSections.Default"/>, no repeats. Absent uses that default order.</summary>
    public List<string>? Order { get; set; }

    /// <summary>Optional print colour as #rrggbb, at least 4.5:1 on white (a manifest test checks it). Absent uses <see cref="CvSections.DefaultAccent"/>.
    /// An accent the owner picked takes precedence (see <see cref="TemplateView.CvAccent"/>).</summary>
    [OptionalCopy] public string Accent { get; set; } = "";

    public IReadOnlyList<string> OrderList() => Order is null ? CvSections.Default : Order;
    public string AccentText() => string.IsNullOrWhiteSpace(Accent) ? CvSections.DefaultAccent : Accent;
}
```

Methods are used instead of computed properties on purpose: `TemplateValidator.ValidateCopy` and the schema drift test reflect over properties, and a computed `string` property would be treated as a copy string.

- [ ] **Step 6: Add the validator**

In `src/Portfolio.Web/Services/TemplateValidator.cs` change line 72 and add the method before `ValidateAccents`:

```csharp
        ValidateCopy(t, errors);
        ValidateCv(t, errors);
```

```csharp
    /// <summary>The CV order is a closed list of section keys and the CV accent ends up in a PDF colour call, so both are checked strictly.</summary>
    private static void ValidateCv(TemplateDefinition t, List<string> errors)
    {
        var cv = t.Copy.Cv;
        if (cv.Order is not null)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var key in cv.Order)
            {
                if (string.IsNullOrWhiteSpace(key) || !CvSections.IsKnown(key))
                    errors.Add($"copy.cv.order lists '{key}'; it may only list {string.Join(", ", CvSections.Default)}.");
                else if (!seen.Add(key))
                    errors.Add($"copy.cv.order lists '{key}' twice.");
            }
        }
        if (!string.IsNullOrWhiteSpace(cv.Accent) && !HexColour().IsMatch(cv.Accent))
            errors.Add($"copy.cv.accent '{cv.Accent}' must look like #1d4ed8.");
    }
```

- [ ] **Step 7: Run the validator tests**

Run: `dotnet test tests/Portfolio.Web.Tests --filter "FullyQualifiedName~CvManifestTests"`
Expected: PASS, 12 tests (5 facts + 7 theory cases). Check the reported count is not zero.

- [ ] **Step 8: Run the schema drift test and watch it fail**

Run: `dotnet test tests/Portfolio.Web.Tests --filter "FullyQualifiedName~SchemaFile_ListsExactlyThePropertiesOfTheModel"`
Expected: FAIL (the `cv` property names differ: the model now has `accent` and `order`, the schema does not).

- [ ] **Step 9: Update the schema**

In `src/Portfolio.Web/Templates/template.schema.json`, in the `cv` object's `properties`, after `certificationsHeading` add (keep `required` unchanged, so both are optional):

```json
            "certificationsHeading": {
              "type": "string"
            },
            "order": {
              "type": "array",
              "description": "Optional. CV sections after the summary, in print order. Default: experience, skills, projects, certifications.",
              "uniqueItems": true,
              "items": {
                "enum": [
                  "experience",
                  "skills",
                  "projects",
                  "certifications"
                ]
              }
            },
            "accent": {
              "type": "string",
              "description": "Optional. Print colour of the CV as #rrggbb, at least 4.5:1 contrast on white. Default: #1D4ED8. An accent the owner picked takes precedence.",
              "pattern": "^#[0-9a-fA-F]{6}$"
            }
```

- [ ] **Step 10: Run the manifest suites**

Run: `dotnet test tests/Portfolio.Web.Tests --filter "FullyQualifiedName~TemplateManifestTests|FullyQualifiedName~CvManifestTests|FullyQualifiedName~TemplateOnboardingManifestTests"`
Expected: all PASS, count at least the Task 0 baseline plus 12.

- [ ] **Step 11: Commit**

```bash
git checkout -- src/Portfolio.Web/wwwroot/css/app.css
git add src/Portfolio.Web/Services/CvSections.cs src/Portfolio.Web/Services/TemplateDefinition.cs src/Portfolio.Web/Services/TemplateValidator.cs src/Portfolio.Web/Templates/template.schema.json tests/Portfolio.Web.Tests/Support/Contrast.cs tests/Portfolio.Web.Tests/Templates/CvManifestTests.cs
git commit -m "feat: copy.cv.order and copy.cv.accent in the template manifest, validated and in the schema

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Every shipped manifest sets its CV order and accent

**Files:**
- Modify: `src/Portfolio.Web/Templates/{software,data-analytics,product-management,design-ux,photography-film,marketing-content,sales-bd,finance-accounting,healthcare}/template.json` (the `copy.cv` object of each)
- Modify: `tests/Portfolio.Web.Tests/Templates/CvManifestTests.cs`

**Interfaces:**
- Consumes: `CvManifestTests.Load`, `Contrast.OnWhite`, `CvSections`, `TemplateOnboardingManifestTests.ShippedKeys` (public static `TheoryData<string>`, one entry per catalog template).
- Produces: `CvManifestTests.Expected : Dictionary<string, (string[] Order, string Accent)>` (internal static), the single source of truth Tasks 3 and 4 assert against.

- [ ] **Step 1: Write the failing tests**

Add to `CvManifestTests` (add `using System.Text.RegularExpressions;` is not needed):

```csharp
    /// <summary>What each shipped template's CV must do. A new template adds its row here; the test below fails until it does.</summary>
    internal static readonly Dictionary<string, (string[] Order, string Accent)> Expected = new()
    {
        ["software"] = (new[] { "experience", "skills", "projects", "certifications" }, "#1D4ED8"),
        ["data-analytics"] = (new[] { "experience", "projects", "skills", "certifications" }, "#115e59"),
        ["product-management"] = (new[] { "experience", "projects", "skills", "certifications" }, "#92400e"),
        ["design-ux"] = (new[] { "projects", "experience", "skills", "certifications" }, "#92320e"),
        ["photography-film"] = (new[] { "projects", "experience", "skills", "certifications" }, "#6b4608"),
        ["marketing-content"] = (new[] { "projects", "experience", "skills", "certifications" }, "#9d174d"),
        ["sales-bd"] = (new[] { "projects", "experience", "skills", "certifications" }, "#065f46"),
        ["finance-accounting"] = (new[] { "experience", "certifications", "projects", "skills" }, "#15375c"),
        ["healthcare"] = (new[] { "certifications", "experience", "skills", "projects" }, "#115e59"),
    };

    [Fact]
    public void TheExpectationTable_CoversEveryTemplateInTheCatalog() =>
        Assert.Equal(new TemplateCatalog(TemplatesDir).All.Select(t => t.Key).OrderBy(x => x), Expected.Keys.OrderBy(x => x));

    [Fact]
    public void Software_SetsTheDefaultsExplicitly()
    {
        Assert.Equal(CvSections.Default, Expected["software"].Order);
        Assert.Equal(CvSections.DefaultAccent, Expected["software"].Accent);
    }

    [Theory]
    [MemberData(nameof(TemplateOnboardingManifestTests.ShippedKeys), MemberType = typeof(TemplateOnboardingManifestTests))]
    public void EveryManifest_SetsItsCvOrderAndAccent(string key)
    {
        var cv = Load(key).Copy.Cv;
        Assert.NotNull(cv.Order);
        Assert.Equal(Expected[key].Order, cv.Order);
        Assert.Equal(Expected[key].Accent, cv.Accent);
    }

    [Theory]
    [MemberData(nameof(TemplateOnboardingManifestTests.ShippedKeys), MemberType = typeof(TemplateOnboardingManifestTests))]
    public void EveryCvOrder_HasOnlyKnownUniqueKeys(string key)
    {
        var order = Load(key).Copy.Cv.OrderList();
        Assert.All(order, k => Assert.True(CvSections.IsKnown(k), $"{key}: '{k}' is not a CV section."));
        Assert.Equal(order.Count, order.Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(TemplateOnboardingManifestTests.ShippedKeys), MemberType = typeof(TemplateOnboardingManifestTests))]
    public void TheCvAccent_AndEveryPickableAccent_ReachBodyTextContrastOnWhite(string key)
    {
        var t = Load(key);
        Assert.True(Contrast.OnWhite(t.Copy.Cv.AccentText()) >= 4.5, $"{key}: copy.cv.accent {t.Copy.Cv.AccentText()} is {Contrast.OnWhite(t.Copy.Cv.AccentText()):F2}:1 on white, below 4.5:1.");
        Assert.NotEmpty(t.Theme.Accents);
        foreach (var a in t.Theme.Accents)
            Assert.True(Contrast.OnWhite(a.Light.Strong) >= 4.5, $"{key} {a.Key}: light.strong {a.Light.Strong} is {Contrast.OnWhite(a.Light.Strong):F2}:1 on white, below 4.5:1.");
    }
```

- [ ] **Step 2: Run to verify the red state**

Run: `dotnet test tests/Portfolio.Web.Tests --filter "FullyQualifiedName~CvManifestTests"`
Expected: `EveryManifest_SetsItsCvOrderAndAccent` FAILS for all 9 keys (`Assert.NotNull() Failure`); the other new tests PASS (the contrast test passes because absent accent resolves to `#1D4ED8`, 6.70:1).

- [ ] **Step 3: Set the fields in all nine manifests**

In each `src/Portfolio.Web/Templates/<key>/template.json`, the `copy.cv` object currently ends with `"certificationsHeading": "<text>"` (Software's looks like the block at lines 217-223). Add a comma after that value and the two fields, with the values from the table in "Where this plan departs from the spec", item 6. Software's, as the example of the shape:

```json
    "cv": {
      "description": "Curriculum vitae — {name}, {title}.",
      "experienceHeading": "Experience",
      "skillsHeading": "Skills",
      "projectsHeading": "Selected projects",
      "certificationsHeading": "Certifications",
      "order": ["experience", "skills", "projects", "certifications"],
      "accent": "#1D4ED8"
    }
```

Healthcare's two lines are `"order": ["certifications", "experience", "skills", "projects"],` and `"accent": "#115e59"`. Edit only the `copy.cv` object of each file; match each file's existing indentation and quote style.

- [ ] **Step 4: Run the manifest and archetype suites**

Run: `dotnet test tests/Portfolio.Web.Tests --filter "FullyQualifiedName~CvManifestTests|FullyQualifiedName~TemplateManifestTests|FullyQualifiedName~TemplateOnboardingManifestTests|FullyQualifiedName~ArchetypeTests|FullyQualifiedName~ThemeStylesheetTests"`
Expected: all PASS. If an `AShowcaseTemplate_/AGalleryTemplate_/AResultsTemplate_DoesNotInheritSoftwareWording` case fails with `copy.cv.accent is identical to software's`, a non-Software accent was typed as `#1D4ED8`: fix the typo.

- [ ] **Step 5: Commit**

```bash
git checkout -- src/Portfolio.Web/wwwroot/css/app.css
git add src/Portfolio.Web/Templates/*/template.json tests/Portfolio.Web.Tests/Templates/CvManifestTests.cs
git commit -m "feat: every template declares its CV section order and print accent

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 3: `CvData` carries the order and accent, built from the template

**Files:**
- Modify: `src/Portfolio.Web/Services/CvDocument.cs` (`CvData` at lines 9-19 and `CvHeadings` at lines 21-25 only; the `CvDocument` class is Task 4)
- Modify: `src/Portfolio.Web/Services/TemplateView.cs` (add `CvAccent` after `AccentStyle`, around line 66)
- Modify: `src/Portfolio.Web/Services/PortfolioContentService.cs` (`BuildCvDataAsync`, lines 111-115)
- Create: `tests/Portfolio.Web.Tests/Templates/CvDataTests.cs`

**Interfaces:**
- Consumes: `CvCopy.OrderList()`, `CvCopy.AccentText()`, `CvSections.DefaultAccent`, `CvManifestTests.Load`, `CvManifestTests.Expected`.
- Produces:
  - `CvData(..., string? Website = null, CvHeadings? Headings = null, IReadOnlyList<string>? SectionOrder = null, string? Accent = null)`. Both new members optional, so `CvAtsRuleScorerTests` and `tools/CvAtsReview` compile unchanged.
  - `CvHeadings.StatusWord(CertificationStatus status) : string`.
  - `TemplateView.CvAccent : string`, always `#rrggbb`.
  - `BuildCvDataAsync` now returns `SectionOrder` (the template's order with disabled sections removed; empty sections stay, the renderers skip them) and `Accent` (`view.CvAccent`).

- [ ] **Step 1: Write the failing tests**

Create `tests/Portfolio.Web.Tests/Templates/CvDataTests.cs`. No database is needed: with no connection string `PortfolioContentService` returns empty content lists, which is enough because the order and accent depend on the template only.

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Portfolio.Data.Entities;
using Portfolio.Web.Configuration;
using Portfolio.Web.Services;
using Portfolio.Web.Tests.Support;
using Xunit;

namespace Portfolio.Web.Tests.Templates;

/// <summary>What <see cref="PortfolioContentService.BuildCvDataAsync"/> hands the two CV renderers: section order, accent and status words.</summary>
public class CvDataTests
{
    private static string TemplatesDir => Path.Combine(AppContext.BaseDirectory, "Templates");

    private static (PortfolioContentService Service, TemplateResolver Resolver) Build()
    {
        var resolver = new TemplateResolver(new TemplateCatalog(TemplatesDir));
        var service = new PortfolioContentService(null!, new ConfigurationBuilder().Build(), resolver, NullLogger<PortfolioContentService>.Instance);
        return (service, resolver);
    }

    private static SiteOptions Site(string key, string? accent = null, params SectionOption[] sections) =>
        new() { TemplateKey = key, OwnerName = "Casey Morgan", Role = "Specialist", Summary = "A summary.", AccentColor = accent, Sections = sections.ToList() };

    [Theory]
    [MemberData(nameof(TemplateOnboardingManifestTests.ShippedKeys), MemberType = typeof(TemplateOnboardingManifestTests))]
    public async Task TheOrder_IsTheTemplatesOwn(string key)
    {
        var (service, _) = Build();

        var data = await service.BuildCvDataAsync(Site(key));

        Assert.Equal(CvManifestTests.Expected[key].Order, data.SectionOrder);
    }

    [Fact]
    public async Task ADisabledSection_IsLeftOutOfTheOrder_AndTheRestKeepTheirPlaces()
    {
        var (service, _) = Build();

        var data = await service.BuildCvDataAsync(Site("finance-accounting", null, new SectionOption { Key = "certifications", Enabled = false }));

        Assert.Equal(new[] { "experience", "projects", "skills" }, data.SectionOrder);
    }

    [Fact]
    public async Task ATemplateWithoutAnOrder_GetsTodaysDefaultOrder()
    {
        var (service, resolver) = Build();
        var site = Site("software");
        var definition = CvManifestTests.Load("software");
        definition.Copy.Cv.Order = null;
        definition.Copy.Cv.Accent = "";
        var view = new TemplateView(site, definition, resolver.Resolve(site).Sections, isPreview: false);

        var data = await service.BuildCvDataAsync(site, view);

        Assert.Equal(CvSections.Default, data.SectionOrder);
        Assert.Equal(CvSections.DefaultAccent, data.Accent);
    }

    [Theory]
    [MemberData(nameof(TemplateOnboardingManifestTests.ShippedKeys), MemberType = typeof(TemplateOnboardingManifestTests))]
    public async Task WithNoPickedAccent_TheAccentIsTheTemplatesCvAccent(string key)
    {
        var (service, _) = Build();

        var data = await service.BuildCvDataAsync(Site(key));

        Assert.Equal(CvManifestTests.Expected[key].Accent, data.Accent);
    }

    [Theory]
    [MemberData(nameof(TemplateOnboardingManifestTests.ShippedKeys), MemberType = typeof(TemplateOnboardingManifestTests))]
    public async Task APickedAccent_GivesItsLightStrongTone_ForEveryOptionOfEveryTemplate(string key)
    {
        var (service, _) = Build();
        var options = CvManifestTests.Load(key).Theme.Accents;
        Assert.NotEmpty(options);

        foreach (var option in options)
        {
            var data = await service.BuildCvDataAsync(Site(key, option.Key));
            Assert.Equal(option.Light.Strong, data.Accent);
        }
    }

    [Theory]
    [InlineData("evil")]
    [InlineData("#ff0000")]
    [InlineData("ROSE")]
    [InlineData("rose;}body{display:none")]
    public async Task AnAccentTheTemplateDoesNotOffer_IsIgnored(string stored)
    {
        var (service, _) = Build();

        var data = await service.BuildCvDataAsync(Site("finance-accounting", stored));

        Assert.Equal("#15375c", data.Accent); // finance-accounting offers none of these, so its own CV accent stands
    }

    [Fact]
    public void ABrokenPickedTone_FallsBackToTheTemplatesAccent_NeverToJunk()
    {
        var definition = CvManifestTests.Load("software");
        definition.Theme.Accents[0].Light.Strong = "red";
        var view = new TemplateView(Site("software", definition.Theme.Accents[0].Key), definition, Array.Empty<ResolvedSection>(), isPreview: false);

        Assert.Equal("#1D4ED8", view.CvAccent);
    }

    [Fact]
    public void TheStatusWords_AreTheTemplatesOwn_AndTheDefaultsAreTheEnumWords()
    {
        var healthcare = new CvHeadings("e", "s", "p", "c", "Current", "In progress", "Planned");
        Assert.Equal("Current", healthcare.StatusWord(CertificationStatus.Earned));
        Assert.Equal("In progress", healthcare.StatusWord(CertificationStatus.InProgress));
        Assert.Equal("Planned", healthcare.StatusWord(CertificationStatus.Planned));
        Assert.Equal("In progress", CvHeadings.Default.StatusWord(CertificationStatus.InProgress));
    }
}
```

- [ ] **Step 2: Run to verify it fails to compile**

Run: `dotnet test tests/Portfolio.Web.Tests --filter "FullyQualifiedName~CvDataTests"`
Expected: build FAILS: `'CvData' does not contain a definition for 'SectionOrder'` / `'Accent'`, `'TemplateView' does not contain a definition for 'CvAccent'`, `'CvHeadings' does not contain a definition for 'StatusWord'`.

- [ ] **Step 3: Extend `CvData` and `CvHeadings`**

In `src/Portfolio.Web/Services/CvDocument.cs`:

```csharp
public record CvData(
    string Name,
    string Title,
    string Summary,
    IReadOnlyList<string> Contacts,
    IReadOnlyList<Experience> Experience,
    IReadOnlyList<IGrouping<string, Skill>> SkillGroups,
    IReadOnlyList<Project> Projects,
    IReadOnlyList<Certification> Certifications,
    string? Website = null,
    CvHeadings? Headings = null,
    IReadOnlyList<string>? SectionOrder = null,
    string? Accent = null);

/// <summary>Section headings and certification status words on the CV; the template supplies them.</summary>
public record CvHeadings(string Experience, string Skills, string Projects, string Certifications, string Earned, string InProgress, string Planned)
{
    public static readonly CvHeadings Default = new("Experience", "Skills", "Selected Projects", "Certifications", "Earned", "In progress", "Planned");

    /// <summary>The word the CV prints for a certification's status. One mapping for both renderers.</summary>
    public string StatusWord(CertificationStatus status) => status switch
    {
        CertificationStatus.Earned => Earned,
        CertificationStatus.InProgress => InProgress,
        _ => Planned,
    };
}
```

`SectionOrder` and `Accent` are null for a `CvData` built by anything that does not set them (the ATS scorer tests, `tools/CvAtsReview`); the renderers read `?? CvSections.Default` and `?? CvSections.DefaultAccent`.

- [ ] **Step 4: Add `TemplateView.CvAccent`**

In `src/Portfolio.Web/Services/TemplateView.cs`, after the `AccentStyle` property and before `Channels`:

```csharp
    /// <summary>The CV's print colour as #rrggbb: the owner's picked accent in its <c>light.strong</c> tone (the tone body text uses on a light page),
    /// otherwise the template's <c>copy.cv.accent</c>, otherwise <see cref="CvSections.DefaultAccent"/>. A value that is not six-digit hex is skipped,
    /// so junk can never reach the PDF's colour call.</summary>
    public string CvAccent
    {
        get
        {
            if (Accent is { } picked && IsHex(picked.Light.Strong)) return picked.Light.Strong;
            var own = Copy.Cv.AccentText();
            return IsHex(own) ? own : CvSections.DefaultAccent;
        }
    }

    private static bool IsHex(string? value) => value is not null && System.Text.RegularExpressions.Regex.IsMatch(value, "^#[0-9a-fA-F]{6}$");
```

- [ ] **Step 5: Fill the new members**

In `src/Portfolio.Web/Services/PortfolioContentService.cs` replace the last two statements of `BuildCvDataAsync` (lines 113-115):

```csharp
        var headings = new CvHeadings(cv.ExperienceHeading, cv.SkillsHeading, cv.ProjectsHeading, cv.CertificationsHeading,
            status.StatusEarned, status.StatusInProgress, status.StatusPlanned);
        // The template's order minus the sections it (or the owner) switched off. An empty section stays in the order; the renderers skip it.
        var order = cv.OrderList().Where(template.IsEnabled).ToList();
        return new CvData(site.OwnerName, site.Role, site.Summary, contacts, roles, groups, projects, certs, site.Url, headings, order, template.CvAccent);
```

- [ ] **Step 6: Run the new tests and the ATS tests**

Run: `dotnet test tests/Portfolio.Web.Tests --filter "FullyQualifiedName~CvDataTests|FullyQualifiedName~CvAts"`
Expected: PASS. `CvDataTests` runs 9 cases of each of three `ShippedKeys` theories, a 4-case accent theory and 3 facts, plus the unchanged `CvAts*` tests; check the reported count is not zero. Then `dotnet build tools/CvAtsReview` → `Build succeeded`.

- [ ] **Step 7: Commit**

```bash
git checkout -- src/Portfolio.Web/wwwroot/css/app.css
git add src/Portfolio.Web/Services/CvDocument.cs src/Portfolio.Web/Services/TemplateView.cs src/Portfolio.Web/Services/PortfolioContentService.cs tests/Portfolio.Web.Tests/Templates/CvDataTests.cs
git commit -m "feat: CvData carries the template's section order and accent

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 4: Both renderers follow `CvData`

**Files:**
- Modify: `src/Portfolio.Web/Services/CvDocument.cs` (the `CvDocument` class, from the `Print-friendly CV` summary comment to the end of the file)
- Modify: `src/Portfolio.Web/Pages/Cv.cshtml`
- Modify: `tests/Portfolio.Web.Tests/Snapshots/software/cv.html` (one line)
- Create: `tests/Portfolio.Web.Tests/Templates/CvOutputTests.cs`

**Interfaces:**
- Consumes: `CvData.SectionOrder`, `CvData.Accent`, `CvHeadings.StatusWord`, `CvSections.Default`, `CvSections.DefaultAccent`, `CvManifestTests.Expected`, `DemoSite.ApplyStarterAsync(AdminApp, string)`, `AdminApp`.
- Correction found while implementing: `DemoSite.ApplyStarterAsync(app, "software")` throws, because Software has no `starter.json` (its sample content is the seed's demo persona). Wherever the test code below calls `ApplyStarterAsync` for `software`, the committed tests instead use a local `ApplyContentAsync(app, key)` helper in `CvOutputTests` (`DemoSite.SeedAsync(app)` for `software`, `ApplyStarterAsync` for every other key), and read certifications from the database rather than from `GetStarter(key)`.
- Produces: no new API.

- [ ] **Step 1: Write the failing tests**

Create `tests/Portfolio.Web.Tests/Templates/CvOutputTests.cs`. PDF tests run on `CvDocument` directly, with no `▸` glyph content (the test host lacks that glyph, the known font gap), and compare bytes with the creation time masked.

```csharp
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Portfolio.Data.Entities;
using Portfolio.Web.Configuration;
using Portfolio.Web.Services;
using Portfolio.Web.Tests.Support;
using QuestPDF.Fluent;
using Xunit;

namespace Portfolio.Web.Tests.Templates;

/// <summary>What visitors get on /cv and /cv.pdf: the template's section order, wording and accent.</summary>
public class CvOutputTests
{
    private static string TemplatesDir => Path.Combine(AppContext.BaseDirectory, "Templates");

    static CvOutputTests()
    {
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
        QuestPDF.Settings.ThrowOnMissingFontFamilies = false; // as AdminApp does
    }

    // ---------------------------------------------------------------- /cv (HTML)

    private static string HeadingOf(CvCopy cv, string key) => key switch
    {
        "experience" => cv.ExperienceHeading,
        "skills" => cv.SkillsHeading,
        "projects" => cv.ProjectsHeading,
        _ => cv.CertificationsHeading,
    };

    [Theory]
    [MemberData(nameof(TemplateOnboardingManifestTests.ShippedKeys), MemberType = typeof(TemplateOnboardingManifestTests))]
    public async Task TheCvPage_ListsItsSectionsInTheTemplatesOrder_WithItsHeadings(string key)
    {
        using var app = new AdminApp();
        await DemoSite.ApplyStarterAsync(app, key);
        var cv = new TemplateCatalog(TemplatesDir).Get(key).Copy.Cv;

        var html = await app.NewClient().GetStringAsync("/cv");

        var positions = CvManifestTests.Expected[key].Order
            .Select(k => (Key: k, At: html.IndexOf($">{TemplateView.Text(HeadingOf(cv, k))}</h2>", StringComparison.Ordinal))).ToList();
        Assert.All(positions, p => Assert.True(p.At >= 0, $"{key}: the {p.Key} heading is missing from /cv."));
        Assert.Equal(positions.Select(p => p.At).OrderBy(x => x), positions.Select(p => p.At));
        Assert.Equal(CvManifestTests.Expected[key].Order.Length, Regex.Matches(html, "<h2 class=\"mt-12 text-xl\">").Count);
    }

    [Theory]
    [MemberData(nameof(TemplateOnboardingManifestTests.ShippedKeys), MemberType = typeof(TemplateOnboardingManifestTests))]
    public async Task TheCvPage_PrintsTheTemplatesCertificationStatusWords_NotTheEnumNames(string key)
    {
        using var app = new AdminApp();
        await DemoSite.ApplyStarterAsync(app, key);
        var catalog = new TemplateCatalog(TemplatesDir);
        var words = catalog.Get(key).Copy.Certifications;
        var certs = catalog.GetStarter(key)!.Certifications;

        var html = WebUtility.HtmlDecode(await app.NewClient().GetStringAsync("/cv")); // Razor encodes apostrophes and typographic marks; compare the words, not the entities

        Assert.NotEmpty(certs);
        foreach (var c in certs)
        {
            var word = c.Status switch { CertificationStatus.Earned => words.StatusEarned, CertificationStatus.InProgress => words.StatusInProgress, _ => words.StatusPlanned };
            Assert.Contains($"{c.Issuer} ({word})", html);
        }
        Assert.DoesNotContain("(InProgress)", html);
    }

    [Fact]
    public async Task AnOwnerPickedAccent_StylesTheCvPage_ThroughTheSameVariablesAsEveryPage()
    {
        using var app = new AdminApp();
        await DemoSite.SeedAsync(app); // not ApplyStarterAsync: software has no starter file (see the correction in the header of this task)
        using (var db = app.CreateDbContext())
        {
            db.SiteProfiles.Single().AccentColor = "rose";
            await db.SaveChangesAsync();
        }
        app.Services.GetRequiredService<ISiteProfileProvider>().Invalidate();
        var css = new TemplateResolver(new TemplateCatalog(TemplatesDir)).Resolve(new SiteOptions { TemplateKey = "software", AccentColor = "rose" }).AccentStyle!;

        var html = await app.NewClient().GetStringAsync("/cv");

        Assert.Contains(css, html);
    }

    // ---------------------------------------------------------------- /cv.pdf

    private static CvData Data(IReadOnlyList<string>? order = null, string? accent = null) => new(
        "Casey Morgan", "Specialist", "A short summary.", new[] { "casey@owner.example" },
        new List<Experience> { new() { Company = "Example Co", Title = "Lead", Start = "2020", End = "2024", Summary = "Ran the place." } },
        new[] { new Skill { Name = "Planning", Group = "Core" } }.GroupBy(s => s.Group).ToList(),
        new List<Project> { new() { Title = "Rota", Slug = "rota", Summary = "A rota." } },
        new List<Certification> { new() { Name = "Cert", Issuer = "Body", Status = CertificationStatus.Earned } },
        "https://owner.example", null, order, accent);

    /// <summary>The PDF with its creation time masked: QuestPDF stamps the time of rendering, so two renders a second apart differ in that field alone.</summary>
    private static string Mask(byte[] pdf) => Regex.Replace(Encoding.Latin1.GetString(pdf), @"D:\d{14}[^)]*\)", "D:X)");

    private static string Pdf(CvData data) => Mask(new CvDocument(data).GeneratePdf());

    [Fact]
    public void ThePdf_WithNoOrderAndNoAccent_IsThePdfOfTodaysDefaults()
    {
        Assert.Equal(Pdf(Data(CvSections.Default, CvSections.DefaultAccent)), Pdf(Data()));
    }

    [Fact]
    public void ThePdf_FollowsTheSectionOrder()
    {
        var experienceFirst = Pdf(Data(new[] { "experience", "skills", "projects", "certifications" }));
        var certificationsFirst = Pdf(Data(new[] { "certifications", "projects", "skills", "experience" }));

        Assert.NotEqual(experienceFirst, certificationsFirst);
        Assert.Equal(certificationsFirst, Pdf(Data(new[] { "certifications", "projects", "skills", "experience" })));
    }

    [Fact]
    public void ThePdf_LeavesOutASectionThatIsNotInTheOrder()
    {
        Assert.NotEqual(Pdf(Data(new[] { "experience", "skills" })), Pdf(Data(new[] { "experience" })));
        Assert.NotEqual(Pdf(Data(new[] { "experience" })), Pdf(Data(Array.Empty<string>())));
    }

    [Fact]
    public void ThePdf_UsesTheAccentOfTheData()
    {
        var teal = Pdf(Data(accent: "#115e59"));
        var brown = Pdf(Data(accent: "#92400e"));

        Assert.NotEqual(teal, brown);
        Assert.NotEqual(teal, Pdf(Data())); // not the default blue
        Assert.Equal(teal, Pdf(Data(accent: "#115e59")));
    }

    [Fact]
    public async Task TheServedPdf_FollowsTheOwnersAccentPick_OnceTheProfileIsSaved()
    {
        // A blank site: no experience highlights or impact lines, so no "▸" glyph (the test host's known font gap).
        using var app = new AdminApp();
        await ContentFlow.BlankSiteAsync(app);
        var visitor = app.NewClient();
        var before = Mask(await visitor.GetByteArrayAsync("/cv.pdf"));
        Assert.Equal(before, Mask(await visitor.GetByteArrayAsync("/cv.pdf"))); // unchanged profile: same document

        using (var db = app.CreateDbContext())
        {
            db.SiteProfiles.Single().AccentColor = "rose";
            await db.SaveChangesAsync();
        }
        app.Services.GetRequiredService<ISiteProfileProvider>().Invalidate();
        var after = Mask(await visitor.GetByteArrayAsync("/cv.pdf"));

        Assert.NotEqual(before, after);
    }
}
```

- [ ] **Step 2: Run to verify the red state**

Run: `dotnet test tests/Portfolio.Web.Tests --filter "FullyQualifiedName~CvOutputTests"`
Expected: it builds, then FAILS as follows. `TheCvPage_ListsItsSectionsInTheTemplatesOrder_WithItsHeadings`: 8 of 9 keys fail (every template except `software`, whose order is today's). `TheCvPage_PrintsTheTemplatesCertificationStatusWords_NotTheEnumNames`: fails for `software` (`(InProgress)`) and for every template whose status words differ from the enum names. `ThePdf_FollowsTheSectionOrder`, `ThePdf_LeavesOutASectionThatIsNotInTheOrder`, `ThePdf_UsesTheAccentOfTheData` and `TheServedPdf_FollowsTheOwnersAccentPick_OnceTheProfileIsSaved` fail (the renderer ignores the data). `ThePdf_WithNoOrderAndNoAccent_IsThePdfOfTodaysDefaults` and `AnOwnerPickedAccent_StylesTheCvPage_ThroughTheSameVariablesAsEveryPage` PASS already: they are characterisation tests of behaviour that must not change.

- [ ] **Step 3: Render the PDF from `SectionOrder` and `Accent`**

In `src/Portfolio.Web/Services/CvDocument.cs`, replace everything from the `/// <summary>Print-friendly CV` comment to the end of the file with the code below. The visual output of each section is unchanged; the changes are: the `Accent` constant becomes an instance property, the body loops over the order, the status mapping moves to `CvHeadings.StatusWord`, and `ToUpperInvariant()` is gone.

```csharp
/// <summary>Print-friendly CV rendered with QuestPDF (light background). The accent and the order of the sections come from <see cref="CvData"/>.</summary>
public class CvDocument : IDocument
{
    private const string Ink = "#1A1A1A";
    private const string Muted = "#555555";

    private readonly CvData _d;
    private readonly CvHeadings _h;
    public CvDocument(CvData d)
    {
        _d = d;
        _h = d.Headings ?? CvHeadings.Default;
    }

    /// <summary>Print colour of this CV; the blue it always had when the data names none.</summary>
    private string Accent => _d.Accent ?? CvSections.DefaultAccent;

    public DocumentMetadata GetMetadata() => new() { Title = $"{_d.Name} — CV" };

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(1.4f, Unit.Centimetre);
            page.DefaultTextStyle(x => x.FontSize(10).FontColor(Ink).FontFamily("Arial"));

            page.Header().Element(Header);
            page.Content().Element(Body);
            var footer = (_d.Website ?? "").Replace("https://", "").Replace("http://", "").TrimEnd('/');
            if (!string.IsNullOrWhiteSpace(footer))
                page.Footer().PaddingTop(6).AlignCenter()
                    .Text(footer).FontSize(8).FontColor(Accent);
        });
    }

    private void Header(IContainer c) => c.Column(col =>
    {
        col.Item().Text(_d.Name).FontSize(22).Bold().FontColor(Ink);
        col.Item().Text(_d.Title).FontSize(11).FontColor(Accent);
        if (_d.Contacts.Count > 0)
            col.Item().PaddingTop(3).Text(string.Join("   ·   ", _d.Contacts)).FontSize(9).FontColor(Muted);
        col.Item().PaddingTop(8).LineHorizontal(1).LineColor(Accent);
    });

    private void Body(IContainer c) => c.PaddingTop(10).Column(col =>
    {
        col.Spacing(12);

        col.Item().Text(_d.Summary).FontSize(10.5f).FontColor(Muted);

        foreach (var key in _d.SectionOrder ?? CvSections.Default)
        {
            switch (key)
            {
                case SectionRegistry.Experience: ExperienceSection(col); break;
                case SectionRegistry.Skills: SkillsSection(col); break;
                case SectionRegistry.Projects: ProjectsSection(col); break;
                case SectionRegistry.Certifications: CertificationsSection(col); break;
            }
        }
    });

    private void ExperienceSection(ColumnDescriptor col)
    {
        if (_d.Experience.Count == 0) return;
        col.Item().Element(x => SectionTitle(x, _h.Experience));
        foreach (var r in _d.Experience)
        {
            col.Item().Column(e =>
            {
                e.Item().Row(row =>
                {
                    row.RelativeItem().Text(t =>
                    {
                        t.Span(r.Company).Bold();
                        t.Span($"  —  {r.Title}").FontColor(Muted);
                    });
                    row.ConstantItem(130).AlignRight()
                        .Text($"{r.Start} – {(r.Current ? "present" : r.End)}").FontSize(9).FontColor("#888");
                });
                if (!string.IsNullOrWhiteSpace(r.Summary))
                    e.Item().PaddingTop(1).Text(r.Summary).FontSize(9.5f).FontColor(Muted);
                foreach (var h in r.Highlights)
                    e.Item().Text($"▸  {h}").FontSize(9.5f).FontColor(Muted);
            });
        }
    }

    private void SkillsSection(ColumnDescriptor col)
    {
        if (_d.SkillGroups.Count == 0) return;
        col.Item().Element(x => SectionTitle(x, _h.Skills));
        foreach (var g in _d.SkillGroups)
        {
            col.Item().Text(t =>
            {
                t.Span($"{g.Key}:  ").Bold().FontColor(Accent);
                t.Span(string.Join(", ", g.Select(s => s.Name))).FontSize(9.5f);
            });
        }
    }

    private void ProjectsSection(ColumnDescriptor col)
    {
        if (_d.Projects.Count == 0) return;
        col.Item().Element(x => SectionTitle(x, _h.Projects));
        foreach (var p in _d.Projects)
        {
            col.Item().Column(pc =>
            {
                pc.Item().Text(p.Title).Bold();
                pc.Item().Text(p.Summary).FontSize(9.5f).FontColor(Muted);
                if (p.Impact.Count > 0)
                    pc.Item().Text($"▸  {string.Join("   ·   ", p.Impact)}").FontSize(9).FontColor(Accent);
            });
        }
    }

    private void CertificationsSection(ColumnDescriptor col)
    {
        if (_d.Certifications.Count == 0) return;
        col.Item().Element(x => SectionTitle(x, _h.Certifications));
        foreach (var cert in _d.Certifications)
        {
            col.Item().Text(t =>
            {
                t.Span(cert.Name).FontSize(9.5f);
                t.Span($"  —  {cert.Issuer} ({_h.StatusWord(cert.Status)})").FontSize(9).FontColor("#888");
            });
        }
    }

    /// <summary>The heading as the template wrote it, so the PDF and the HTML agree.</summary>
    private void SectionTitle(IContainer c, string title) =>
        c.PaddingTop(6).BorderBottom(1).BorderColor("#DDDDDD").PaddingBottom(2)
            .Text(title).FontSize(11).Bold().FontColor(Accent);
}
```

- [ ] **Step 4: Render `/cv` from `SectionOrder` without changing Software's bytes**

In `src/Portfolio.Web/Pages/Cv.cshtml`. Source indentation is printed literally, so **do not re-indent** the section blocks. Make exactly these edits (this was proven to leave all 15 snapshot pages byte-identical to the parent commit when the order is today's):

(a) In the top `@{ ... }` block, after `var contacts = data.Contacts;` (line 17) add:

```csharp
    var headings = data.Headings ?? CvHeadings.Default;
    // The section blocks below sit at their original columns on purpose: Razor prints source indentation, so re-indenting them would change
    // the bytes of Software's /cv snapshot. A line holding only "@:" at column 0 prints the blank separator line between two sections.
```

(b) Turn the four `@if` blocks into one loop. This is the complete change, as a unified diff of the proven version (the only difference from what was proven is that the loop reads `data.SectionOrder ?? CvSections.Default` instead of a literal array). Apply it hunk by hunk with the Edit tool; keep every `@:` at column 0 and do not touch any other line:

```diff
@@ -80,7 +80,9 @@

     <p class="mt-8 max-w-3xl text-muted">@site.Summary</p>

-    @if (roles.Count > 0)
+    @foreach (var sectionKey in data.SectionOrder ?? CvSections.Default)
+    {
+    if (sectionKey == "experience" && roles.Count > 0)
     {
         <h2 class="mt-12 text-xl">@Text(copy.Cv.ExperienceHeading)</h2>
         <div class="mt-4 space-y-6">
@@ -104,9 +106,9 @@
                 </div>
             }
         </div>
+@:
     }
-
-    @if (groups.Count > 0)
+    if (sectionKey == "skills" && groups.Count > 0)
     {
         <h2 class="mt-12 text-xl">@Text(copy.Cv.SkillsHeading)</h2>
         <div class="mt-4 space-y-2">
@@ -116,9 +118,9 @@
                     <span class="text-muted"> — @string.Join(", ", g.Select(s => s.Name))</span></p>
             }
         </div>
+@:
     }
-
-    @if (projects.Count > 0)
+    if (sectionKey == "projects" && projects.Count > 0)
     {
         <h2 class="mt-12 text-xl">@Text(copy.Cv.ProjectsHeading)</h2>
         <div class="mt-4 space-y-4">
@@ -134,9 +136,9 @@
                 </div>
             }
         </div>
+@:
     }
-
-    @if (certs.Count > 0)
+    if (sectionKey == "certifications" && certs.Count > 0)
     {
         <h2 class="mt-12 text-xl">@Text(copy.Cv.CertificationsHeading)</h2>
         <ul class="mt-4 space-y-1">
@@ -146,4 +148,5 @@
             }
         </ul>
     }
+    }
 </section>
```

Why it looks odd and must stay that way: the section markup keeps its original columns (Razor prints source indentation, so re-indenting it would change Software's bytes); the blank separator line moved from between the blocks to inside each block as a column-0 `@:` (a blank line inside a code block is not printed, and an indented `@:` prints its indentation as trailing spaces); and plain `if` lines replace `@if` because they now sit inside the `@foreach` code block.

(c) In the certifications list item, change `(@c.Status)` to `(@(headings.StatusWord(c.Status)))`.

- [ ] **Step 5: Run the new tests**

Run: `dotnet test tests/Portfolio.Web.Tests --filter "FullyQualifiedName~CvOutputTests|FullyQualifiedName~CvDataTests"`
Expected: PASS. If a `TheCvPage_ListsItsSectionsInTheTemplatesOrder_...` case reports a missing heading for a template, that template's starter has no featured project or no entries in that section: report it, do not weaken the test.

- [ ] **Step 6: Prove Software's HTML is unchanged except one line**

```bash
AFTER=<scratchpad>/cv6a-after && rm -rf "$AFTER" && mkdir -p "$AFTER"
SNAPSHOT_ACTUAL_DIR="$AFTER" dotnet test tests/Portfolio.Web.Tests --filter "FullyQualifiedName~DemoPersona_RendersByteForByteLikeTheSnapshots"
git checkout -- src/Portfolio.Web/wwwroot/css/app.css
for f in "$BASE"/*; do b=$(basename "$f"); cmp -s "$f" "$AFTER/$b" || echo "DIFFERS $b"; done
diff "$BASE/cv.html" "$AFTER/cv.html"
```
Expected: the loop prints exactly one line, `DIFFERS cv.html`, and `diff` shows exactly one changed line pair, the Microsoft AI-900 item: `(InProgress)` becomes `(In progress)`. Any other differing line (whitespace included) means Step 4 re-indented something or lost an `@:`: fix the file, do not touch the snapshot.

- [ ] **Step 7: Update the one snapshot line**

In `tests/Portfolio.Web.Tests/Snapshots/software/cv.html` line 216 change `Microsoft (InProgress)` to `Microsoft (In progress)`:

```bash
sed -i '216s/Microsoft (InProgress)/Microsoft (In progress)/' tests/Portfolio.Web.Tests/Snapshots/software/cv.html
git diff --stat -- tests/Portfolio.Web.Tests/Snapshots
```
Expected: `1 file changed, 1 insertion(+), 1 deletion(-)`. Also confirm the line endings of that file did not change: `git diff` must show only that line.

- [ ] **Step 8: Run the wider suites that render `/cv`**

Run: `dotnet test tests/Portfolio.Web.Tests --filter "FullyQualifiedName~Templates|FullyQualifiedName~Cv|FullyQualifiedName~Routes|FullyQualifiedName~ContentEndToEnd|FullyQualifiedName~SectionSettings|FullyQualifiedName~ExperienceEditor|FullyQualifiedName~OnboardingFlow|FullyQualifiedName~OnboardingPhotoPreview"` then `git checkout -- src/Portfolio.Web/wwwroot/css/app.css`.
Expected: all PASS except the known baseline failure `SoftwareParityTests.DemoPersona_RendersByteForByteLikeTheSnapshots` (still failing on fingerprints, same 14 pages, proven equal by Step 6). `EveryWrite_RefreshesTheGeneratedCvAtOnce` must pass.

- [ ] **Step 9: Commit**

```bash
git checkout -- src/Portfolio.Web/wwwroot/css/app.css
git status --short   # only the four files of this task
git add src/Portfolio.Web/Services/CvDocument.cs src/Portfolio.Web/Pages/Cv.cshtml tests/Portfolio.Web.Tests/Snapshots/software/cv.html tests/Portfolio.Web.Tests/Templates/CvOutputTests.cs
git commit -m "feat: /cv and /cv.pdf follow the template's section order, status words and accent

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 5: Mutation checks, full verification, documentation

**Files:**
- Modify: `docs/template-authoring-guide.md`
- Modify: `docs/templates-and-onboarding-plan.md`
- Modify: `docs/superpowers/specs/2026-11-22-cv-output-per-template-design.md`

- [ ] **Step 1: Mutation-test the new guards (in a scratch copy only)**

Never in this worktree. Make a copy: `SCRATCH=<scratchpad>/cv6a-mut && rm -rf "$SCRATCH" && git -c core.autocrlf=false -c core.eol=lf archive HEAD | (mkdir -p "$SCRATCH" && tar -x -C "$SCRATCH")`. In the copy, apply one mutation at a time, run the named test from the copy's root, expect FAIL, then restore the file from the archive:

| # | Mutation | Test that must fail |
|---|---|---|
| 1 | `ValidateCv`: delete the `!CvSections.IsKnown(key)` condition | `CvManifestTests.Validator_RejectsASectionKeyTheCvCannotShow` |
| 2 | `ValidateCv`: delete the `else if (!seen.Add(key))` branch | `CvManifestTests.Validator_RejectsARepeatedKey_AndABlankEntry` |
| 3 | `ValidateCv`: delete the accent `HexColour()` check | `CvManifestTests.Validator_RejectsAnAccentThatIsNotSixDigitHex` |
| 4 | `BuildCvDataAsync`: `cv.OrderList().Where(template.IsEnabled)` becomes `cv.OrderList()` | `CvDataTests.ADisabledSection_IsLeftOutOfTheOrder_AndTheRestKeepTheirPlaces` |
| 5 | `TemplateView.CvAccent`: return `Copy.Cv.AccentText()` first (ignore the pick) | `CvDataTests.APickedAccent_GivesItsLightStrongTone_ForEveryOptionOfEveryTemplate` |
| 6 | `TemplateView.CvAccent`: drop the `IsHex(picked.Light.Strong)` guard | `CvDataTests.ABrokenPickedTone_FallsBackToTheTemplatesAccent_NeverToJunk` |
| 7 | `CvDocument.Body`: loop over `CvSections.Default` instead of `_d.SectionOrder ?? ...` | `CvOutputTests.ThePdf_FollowsTheSectionOrder` |
| 8 | `CvDocument.Accent`: return `CvSections.DefaultAccent` always | `CvOutputTests.ThePdf_UsesTheAccentOfTheData` |
| 9 | `Cv.cshtml`: loop over `CvSections.Default` | `CvOutputTests.TheCvPage_ListsItsSectionsInTheTemplatesOrder_WithItsHeadings` |
| 10 | `Cv.cshtml`: print `@c.Status` again | `CvOutputTests.TheCvPage_PrintsTheTemplatesCertificationStatusWords_NotTheEnumNames` |
| 11 | `Healthcare` manifest: set `accent` to `#8fd3c9` (low contrast) | `CvManifestTests.TheCvAccent_AndEveryPickableAccent_ReachBodyTextContrastOnWhite` (and `EveryManifest_SetsItsCvOrderAndAccent`) |

Record the result table (mutation, failing test, pass or fail) for Step 4. A mutation that no test catches is a gap: add the test in the real worktree, re-run, and commit it with the docs.

- [ ] **Step 2: Whole suite on an LF export, as CI builds it**

```bash
SCRATCH=<scratchpad>/cv6a-lf && rm -rf "$SCRATCH" && mkdir -p "$SCRATCH"
git -c core.autocrlf=false -c core.eol=lf archive HEAD | tar -x -C "$SCRATCH"
cd "$SCRATCH" && dotnet build src/Portfolio.Web -p:TailwindRequired=true && dotnet test tests/Portfolio.Web.Tests
cmp "$SCRATCH/src/Portfolio.Web/wwwroot/css/app.css" <this worktree>/src/Portfolio.Web/wwwroot/css/app.css && echo "app.css identical to the committed file"
dotnet build tools/CvAtsReview
```
Expected: `app.css identical to the committed file` (this plan adds no Tailwind classes); the suite's only failures are the two known baseline tests (`SoftwareParityTests.DemoPersona_RendersByteForByteLikeTheSnapshots`, `OnboardingAccessibilityAccentTests.WithNoAccentPicked_ThePublicPageHasNoAccentOverride`), each failing the same way at `9488463`. Report the real totals. If any other test fails, fix it before continuing.

- [ ] **Step 3: Look at the real thing**

Use the `run` skill to start the app against this worktree, sign in, open `/admin/template` and switch to Finance & Accounting, then Healthcare, then Software (apply each template's sample content from the wizard if the site is empty). For each: open `/cv` and read the order of the four headings against the table in this plan; check certification status words; pick a different accent in `/admin/profile` and reload `/cv`. Download `/cv.pdf` and open it: on a host with the glyph fonts, confirm the same order, the heading text as the template wrote it (no upper-casing) and the accent colour; on a host without them, say so rather than claiming the PDF was seen. Record exactly what was and was not looked at.

- [ ] **Step 4: Docs**

- `docs/template-authoring-guide.md`: under the manifest example (line 89 area, the `"copy": { ... "cv": {} }` line) add a short subsection "CV output" stating: `copy.cv.order` (optional list, subset of `experience, skills, projects, certifications`, no repeats, default today's order, the summary is always first); `copy.cv.accent` (optional `#rrggbb`, default `#1D4ED8`, at least 4.5:1 on white, used for the PDF; an accent the owner picks overrides it in its `light.strong` tone; the HTML takes its colours from the theme as before); the rule used by the shipped templates (the template's own `sections[]` order restricted to the four CV sections, accent = the theme's light `--c-accent-strong`); and that a new template must also add its row to `CvManifestTests.Expected`.
- `docs/templates-and-onboarding-plan.md`: in the Phase 5 "Known gaps" paragraph (lines 196-198) replace "The CV page prints certification status with the enum names as before" and "/cv.pdf ... keep the software look (per-template CV output is Phase 6)" with a pointer to Phase 6a being done for `/cv` and `/cv.pdf`; leave `/og.png`, the ATS badge and the PDF font gap as open. Add a "Phase 6a" section after the Phase 5 notes recording: what shipped (the table of orders and accents), the one Software snapshot line, the PDF heading-case change, the mutation table from Step 1, the suite totals and the baseline failures from Step 2, and what Step 3 did and did not cover.
- Spec amendments in `docs/superpowers/specs/2026-11-22-cv-output-per-template-design.md`: (1) "Today", last bullets: add that the layout already applies the owner's accent to the HTML. (2) "Design / Bundled fixes": say that Software's HTML changes by one certification status word and Software's PDF headings change case. (3) "Testing" item 1: replace "passes with no snapshot edit" with "the `/cv` snapshot changes by the one status-word line and every other page is byte-identical (proved by diffing renders against the parent commit, because the test itself fails on this host's CRLF)". (4) "Testing" item 6: say PDF comparisons mask the embedded creation time and that determinism in production comes from the output cache. (5) Add the orders and accents table.

- [ ] **Step 5: Commit**

```bash
git checkout -- src/Portfolio.Web/wwwroot/css/app.css
git add docs/template-authoring-guide.md docs/templates-and-onboarding-plan.md docs/superpowers/specs/2026-11-22-cv-output-per-template-design.md docs/superpowers/plans/2026-11-24-cv-output-per-template.md
git commit -m "docs: record Phase 6a, its verification and the spec corrections

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

## Self-review against the spec

| Spec requirement | Where |
|---|---|
| `copy.cv.order` / `accent` in `CvCopy` and the schema, defaults when absent | Task 1 |
| All nine manifests set both; Software sets the defaults explicitly | Task 2 (`Software_SetsTheDefaultsExplicitly`) |
| Accent resolution: picked option's `light.strong`, else `copy.cv.accent` | Task 3 (`CvAccent`, `APickedAccent_...`, `AnAccentTheTemplateDoesNotOffer_IsIgnored`) |
| 4.5:1 on white for `copy.cv.accent` and every `light.strong` | Task 2 (`TheCvAccent_AndEveryPickableAccent_...`) |
| `CvData.SectionOrder` and `Accent`, defaults keep ATS scorer and tool compiling | Task 3 (optional members; `dotnet build tools/CvAtsReview`) |
| Both renderers loop over `SectionOrder`, use `Accent` | Task 4 Steps 3-4 |
| HTML shows the template's status words | Task 4 Step 4(e), `TheCvPage_PrintsTheTemplatesCertificationStatusWords_...` |
| Headings as written in both renderers | Task 4 Step 3 (`SectionTitle`); HTML already did |
| Test 1 Software unchanged | Task 4 Step 6, with the departure noted at the top |
| Test 2 manifest for all nine | Task 2 |
| Test 3 per-template `/cv` order, headings, status words; software-wording tests still pass | Task 4 Step 1 and Step 8 |
| Test 4 owner-picked accent changes HTML and PDF | Task 4 (`AnOwnerPickedAccent_StylesTheCvPage_...`, `ThePdf_UsesTheAccentOfTheData`, served-PDF test) |
| Test 5 disabled section absent from `/cv` and the PDF data | Task 3 (`ADisabledSection_...`); existing `SectionSettingsTests` covers `/cv` |
| Test 6 `/cv.pdf` determinism and refresh on write | Task 4 Step 8 (`EveryWrite_RefreshesTheGeneratedCvAtOnce`), masked comparisons |
| Test 7 mutation checks | Task 5 Step 1 |
| "How a human sees it" | Task 5 Step 3 |
| Out of scope (PDF layouts, font gap, og.png, filename, ATS, contact fields) | Not touched |

Type check: `CvData.SectionOrder : IReadOnlyList<string>?`, `CvData.Accent : string?`, `CvCopy.Order : List<string>?`, `CvCopy.OrderList() : IReadOnlyList<string>`, `CvCopy.AccentText() : string`, `TemplateView.CvAccent : string`, `CvHeadings.StatusWord(CertificationStatus) : string`, `CvSections.Default / DefaultAccent / IsKnown`, `CvManifestTests.Expected : Dictionary<string, (string[] Order, string Accent)>` are used with the same names and shapes in every task.
