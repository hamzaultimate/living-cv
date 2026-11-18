# Admin Redesign PR C: Setup Wizard Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rebuild the setup wizard as one card per step with a clickable step indicator that jumps between steps, a status in words on every step, partial drafts saved on every jump, and a Review step that shows a "Ready to publish?" checklist beside a live preview. Delivers acceptance criteria 15 to 21 of `docs/superpowers/specs/2026-11-15-admin-redesign-design.md` (section 6).

**Architecture:** A pure `WizardProgress` computes each step's status and the publish checklist from the draft alone, so nothing gates on `OnboardingDraft.Step` any more. Every wizard page has one form, `id="wizard-form"`; the indicator buttons and the card's Continue button are submit buttons associated with it through the `form` attribute and carry `name="goto"`. A page saves its own fields under the hard rules only (length and plain text), then `WelcomePageModel.Leave()` redirects to the clamped `goto` step. Completeness (required fields, email and address format) is checked only at Publish, by the unchanged `OnboardingService.PublishAsync`, and the disabled Publish button is a convenience on top of that.

**Tech Stack:** .NET 10, ASP.NET Core Razor Pages, xUnit, hand-written `admin.css`. No script and no build step are added.

## Global Constraints

Copied from the spec (sections 2, 3 and 6), plus repo rules.

- The admin CSP (`AdminAuth.cs`) is `default-src 'none'; style-src 'self'; img-src 'self'`, with `script-src 'self'` only where `AllowsScript` permits. No inline script and no inline style. The Review preview iframe already has `frame-src 'self'` on `/admin/welcome/review`; no CSP change is needed.
- `admin.css` is one hand-written file with light and dark colour tokens. No CSS build step is added.
- Template wording drives labels. Nothing in the wizard names a profession or a section in code; it reads the manifest (`TemplateDefinition`).
- Every form must work without script. Step jumping, the Continue button and the preview tabs use links and form buttons only.
- `goto`, `show`, `tab` and `size` stay untrusted: `goto` is only ever clamped to the four known steps, `tab` only selects a known preview page or the search tab.
- Hard rules are enforced on every save (length limits, no markup characters, an accent or content kind the template offers). Completeness rules are enforced only at Publish (required fields, a valid email, a valid site address).
- `OnboardingDraft.Step` stays in the stored JSON and is still written, so existing drafts still parse; nothing gates on it.
- Status is conveyed in text as well as colour. The current step is marked separately with `aria-current="step"`.
- Out of scope: the public site, the content editors, authentication, the template manifests, the admin shell and the template picker (PR A and PR B).
- Repo rules (`CONTRIBUTING.md`): admin pages inherit `AdminPageModel`, state changes are POST, match the surrounding code style, `dotnet build` stays warning-clean (baseline: 16 pre-existing NuGet advisory warnings NU1902/NU1903, not ours) and `dotnet test` stays green.
- Git rules: work only in this worktree, `E:\Repos\living-cv\.claude\worktrees\admin-redesign-pr-c`, on branch `feature/admin-redesign-pr-c`. It was created from `feature/admin-redesign-pr-b` at `6f5dc32`, because PR B is not merged into `develop` yet and the wizard's step 1 depends on its shared picker. **When PR B merges, rebase this branch onto `develop` with `git rebase --onto develop 6f5dc32 feature/admin-redesign-pr-c`** (PR A was squash-merged, so expect the same). Do not push and do not open a PR. **Never stage `src/Portfolio.Web/wwwroot/css/app.css`**: the build regenerates it and it shows as modified; always `git add` explicit paths. Do not switch branches in `E:\Repos\living-cv` (other sessions use that folder).
- Commit messages end with `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`.

## Decisions and spec interpretations (flag these in review)

1. **The template step's Continue is the picker's own button.** PR B placed "Use [name] and continue" / "Keep [name] and continue" in the picker header, and it is the only button that changes the draft's template (spec 6.3). The card's bottom bar on step 1 therefore holds only Skip and Start over. Steps 2 and 3 have a "Continue" button at the bottom right; step 4 has "Save and update preview" and "Publish my site" in its form. One sentence is added to spec 6.1 in Task 7.
2. **The current step is a button too.** Clicking it saves the step and shows it again, which doubles as "save my draft" without leaving. The indicator therefore has four buttons on every page.
3. **A hidden default button comes first in the page.** Pressing Enter in a text field submits the form through its first submit button in tree order, and the indicator would otherwise win (Enter in the Basics name would jump to step 1). `_WizardHeader` starts with a visually hidden `tabindex="-1" aria-hidden="true"` button whose `goto` is the next step.
4. **"Draft saved at HH:mm UTC".** The spec says `HH:mm`; the draft stores a UTC instant and the server does not know the owner's time zone, so the time is shown with its zone.
5. **A draft may have no template.** Saving basics, a photo or the Review choices with no draft creates a draft without a template. Pages then show the template the site is on now (or the default on a first run) through `OnboardingService.TemplateFor`, the preview uses it too, and the Template step stays "To do" until the owner presses Use, Keep or jumps away from step 1 (which keeps that template, spec 6.3). Publishing without a template is refused by the service as before.
6. **The Location rule.** `Apply` used `Step >= 2` to decide whether an empty Location should clear the saved one. It now clears it only when the basics are complete (always true at publish), so a half-typed draft never blanks a saved location in the preview. A non-empty Location always applies.
7. **`DraftPreviewAsync` no longer returns null for a draft without a template.** It previews the draft on `TemplateFor`, so typed basics show in the Review preview even before the template step is confirmed.
8. **A refused publish returns to Review.** `Incomplete` (any earlier step) now redirects to step 4 with a flash, because the checklist there shows what is missing (spec 6.5). `NoMenu` still goes to step 1 (it is about the template) and `Changed` still goes to step 4.
9. **The checklist reflects the saved draft plus the address in the form.** On a first run the address field is prefilled from the request and is not yet saved, so counting only saved values would disable Publish until the owner pressed Save. The checklist treats the shown address as the address; the server still validates what is actually posted.
10. **Resume after the basics lands on Review.** The dashboard's "Continue setup" opens the first step that is not Done or Optional (spec 6.6), and the photo is Optional, so with a template and complete basics it opens step 4. Test `TheDraft_SurvivesARefreshAndANewSession...` is updated for this.
11. **Titles and breadcrumbs.** Step pages are titled "Basics · Step 2 of 4" and pass `ViewData["Crumb"] = "Step 2: Basics"`, so the breadcrumb reads Admin / Tools / Setup wizard / Step 2: Basics instead of repeating "Setup" (PR A left this for PR C). Step 1 is the wizard's own page and adds no extra crumb.
12. **Old tests that pinned removed behaviour are rewritten, not weakened.** The gating test (`AStepWithoutTheOnesBeforeIt_SendsTheOwnerBack`), the progress-links test (`TheProgressIndicator_OnlyLinksToStepsTheOwnerCanOpen`) and the Basics error test (invalid email) are replaced by tests of the new rules; each task lists them.

## File Structure

Create:

| File | Responsibility |
|---|---|
| `src/Portfolio.Web/Services/WizardProgress.cs` | `StepState`, `StepStatus`, `ChecklistLine`, `PublishChecklist` and the pure `WizardProgress` (`Steps`, `Compute`, `Checklist`, `Resume`, `ClampStep`). No HTML, no I/O. |
| `tests/Portfolio.Web.Tests/Services/WizardProgressTests.cs` | Unit: step status for each draft shape, checklist, resume, clamp. |
| `tests/Portfolio.Web.Tests/Admin/OnboardingPartialDraftTests.cs` | Service: partial saves, `TemplateFor`, `KeepTemplateAsync`, preview of a partial draft. |
| `tests/Portfolio.Web.Tests/Admin/WizardFrameTests.cs` | Integration: the indicator, footer, saved-at line and the template step's jump. |
| `tests/Portfolio.Web.Tests/Admin/WizardBasicsJumpTests.cs` | Integration: the Basics step's partial save, hard rules, Enter key. |
| `tests/Portfolio.Web.Tests/Services/BasicsRulesTests.cs` | Unit: hard rules versus completeness. |
| `tests/Portfolio.Web.Tests/Admin/WizardPhotoJumpTests.cs` | Integration: upload on jump, invalid file, layout. |
| `tests/Portfolio.Web.Tests/Admin/WizardReviewTests.cs` | Integration: checklist, disabled Publish, server refusal, search tab, jump. |

Modify:

| File | Change |
|---|---|
| `src/Portfolio.Web/Services/OnboardingDraft.cs` | `Clone()`; `BasicsForm` binds hard rules only and gains `AllEmpty`, `Missing`, `IsComplete` over a private `PublishRules`; `ReviewForm` loses `Required` and `HttpUrl` on the address. |
| `src/Portfolio.Web/Services/OnboardingService.cs` | `TemplateFor`, `ProgressOf`, `ChecklistFor`, `KeepTemplateAsync`; `SaveChoicesAsync` and `DraftPreviewAsync` work without a template; `Apply` Location rule. |
| `src/Portfolio.Web/Pages/Admin/Welcome/WelcomePageModel.cs` | `Goto`, `Progress`, `NextStep`, `ContinueLabel`, `Leave()`; `RequireStep` and `Reachable` removed in Task 6. |
| `src/Portfolio.Web/Pages/Admin/Welcome/_WizardHeader.cshtml` | Indicator as buttons with status, hidden default button, saved-at line. |
| `src/Portfolio.Web/Pages/Admin/Welcome/_WizardFooter.cshtml` | Bottom bar: Skip and Start over on the left, Continue on the right. |
| `src/Portfolio.Web/Pages/Admin/Welcome/Template.cshtml` and `.cs` | Card frame, `wizard-form`, `Jump` handler. |
| `src/Portfolio.Web/Pages/Admin/Welcome/Basics.cshtml` and `.cs` | Card frame, partial save, no gating, no Back, no `required`. |
| `src/Portfolio.Web/Pages/Admin/Welcome/Photo.cshtml` and `.cs` | Card frame, one-card layout, upload on jump. |
| `src/Portfolio.Web/Pages/Admin/Welcome/Review.cshtml` and `.cs` | Split layout, checklist, disabled Publish, search tab, jump handler. |
| `src/Portfolio.Web/Pages/Admin/Index.cshtml.cs` | "Continue setup" opens the first open step. |
| `src/Portfolio.Web/wwwroot/css/admin.css` | Indicator buttons, card frame, photo step, Review split, checklist. |
| `tests/Portfolio.Web.Tests/Support/WizardFlow.cs` | `JumpAsync`, `JumpFromPhotoAsync`, `DraftOrNullAsync`, `StepValues`, `StepStates`. |
| `docs/new-user-guide.md`, `docs/superpowers/specs/2026-11-15-admin-redesign-design.md` | Describe jumping and the checklist; one sentence in spec 6.1. |
| Existing tests | Listed per task. |

Commands used throughout (run from the worktree root):

- One class: `dotnet test tests/Portfolio.Web.Tests --filter "FullyQualifiedName~WizardProgressTests"`
- Whole suite: `dotnet test Portfolio.slnx`
- Build: `dotnet build Portfolio.slnx`

**Baseline before Task 1:** run `dotnet test Portfolio.slnx` once and write down every failure. The Admin Redesign PR A plan recorded two that already fail on a clean checkout (`SoftwareParityTests.DemoPersona_RendersByteForByteLikeTheSnapshots` and `OnboardingAccessibilityAccentTests.WithNoAccentPicked_ThePublicPageHasNoAccentOverride`, both line-ending related with `core.autocrlf=true`). Anything else that fails at baseline is not ours; anything that fails later and is not in that list is.

---

### Task 1: `WizardProgress`: step status, checklist, resume, clamp (criterion 18, and the model for 19)

**Files:**
- Create: `src/Portfolio.Web/Services/WizardProgress.cs`
- Modify: `src/Portfolio.Web/Services/OnboardingDraft.cs` (add `Clone`, and the new `BasicsForm` members `AllEmpty`, `Missing`, with `IsComplete` rebuilt on them)
- Test: `tests/Portfolio.Web.Tests/Services/WizardProgressTests.cs`

**Interfaces:**
- Produces (later tasks call these exactly):
  - `enum StepState { Done, ToDo, NeedsAttention, Optional }`
  - `record StepStatus(int Number, string Label, string Path, StepState State)` with `string Text` ("Done", "To do", "Needs attention", "Optional") and `bool IsOpen` (ToDo or NeedsAttention)
  - `record ChecklistLine(string Label, bool Done, string Detail, string Path, bool Required = true)`
  - `record PublishChecklist(IReadOnlyList<ChecklistLine> Lines)` with `bool Ready`, `IReadOnlyList<ChecklistLine> Missing`, `string? Reason`
  - `WizardProgress.Steps : IReadOnlyList<(int Number, string Label, string Path)>`
  - `WizardProgress.Compute(OnboardingDraft? draft, ITemplateCatalog catalog) -> IReadOnlyList<StepStatus>` (always four, in order)
  - `WizardProgress.Checklist(OnboardingDraft? draft, ITemplateCatalog catalog) -> PublishChecklist`
  - `WizardProgress.Resume(IReadOnlyList<StepStatus> steps) -> StepStatus`
  - `WizardProgress.ClampStep(string? requested, int fallback) -> int`
  - `OnboardingDraft.Clone() -> OnboardingDraft`
  - `BasicsForm.AllEmpty(OnboardingDraft d) -> bool`, `BasicsForm.Missing(OnboardingDraft? d) -> IReadOnlyList<string>` (lower-case field names, in form order), `BasicsForm.IsComplete(OnboardingDraft d) -> bool`

- [ ] **Step 1: Write the failing tests**

Create `tests/Portfolio.Web.Tests/Services/WizardProgressTests.cs`:

```csharp
using Portfolio.Web.Services;
using Xunit;

namespace Portfolio.Web.Tests.Services;

/// <summary>Step status and the publish checklist, computed from the draft alone. No HTML and no database are involved.</summary>
public class WizardProgressTests
{
    private static readonly TemplateCatalog Catalog = new(Path.Combine(AppContext.BaseDirectory, "Templates"));

    private static OnboardingDraft Complete() => new()
    {
        TemplateKey = "software", OwnerName = "Jordan Alexander", JobTitle = "Engineer", Email = "jordan@owner.example",
        Summary = "Builds things.", SiteUrl = "https://jordan.example",
    };

    private static IReadOnlyList<StepState> States(OnboardingDraft? draft) =>
        WizardProgress.Compute(draft, Catalog).Select(s => s.State).ToList();

    [Fact]
    public void WithNoDraft_TheTemplateBasicsAndReviewAreToDo_AndThePhotoIsOptional() =>
        Assert.Equal([StepState.ToDo, StepState.ToDo, StepState.Optional, StepState.ToDo], States(null));

    [Fact]
    public void TheFourSteps_AreInOrder_WithTheirPaths()
    {
        var steps = WizardProgress.Compute(null, Catalog);

        Assert.Equal([1, 2, 3, 4], steps.Select(s => s.Number));
        Assert.Equal(["Template", "Basics", "Photo", "Review and launch"], steps.Select(s => s.Label));
        Assert.Equal(["/admin/welcome", "/admin/welcome/basics", "/admin/welcome/photo", "/admin/welcome/review"], steps.Select(s => s.Path));
    }

    [Theory]
    [InlineData(null, StepState.ToDo)]
    [InlineData("", StepState.ToDo)]
    [InlineData("no-such-template", StepState.ToDo)]
    [InlineData("healthcare", StepState.Done)]
    public void TheTemplateIsDone_OnlyWhenTheDraftNamesACatalogTemplate(string? key, StepState expected) =>
        Assert.Equal(expected, States(new OnboardingDraft { TemplateKey = key })[0]);

    [Fact]
    public void Basics_AreToDo_WhenEveryFieldIsEmpty_EvenWithWhitespace() =>
        Assert.Equal(StepState.ToDo, States(new OnboardingDraft { OwnerName = "  ", Location = "" })[1]);

    [Fact]
    public void Basics_NeedAttention_WhenSomeAreFilledButNotComplete() =>
        Assert.Equal(StepState.NeedsAttention, States(new OnboardingDraft { OwnerName = "Jordan" })[1]);

    [Fact]
    public void Basics_NeedAttention_WhenTheEmailIsNotAnAddress()
    {
        var draft = Complete();
        draft.Email = "nope";

        Assert.Equal(StepState.NeedsAttention, States(draft)[1]);
    }

    [Fact]
    public void Basics_AreDone_WhenCompleteWithoutALocation() =>
        Assert.Equal(StepState.Done, States(Complete())[1]);

    [Fact]
    public void ThePhotoIsDone_WhenTheDraftHasOne_AndOptionalOtherwise()
    {
        var draft = Complete();
        Assert.Equal(StepState.Optional, States(draft)[2]);

        draft.PhotoDraft = "abc.jpg";
        Assert.Equal(StepState.Done, States(draft)[2]);
    }

    [Theory]
    [InlineData(null, StepState.ToDo)]
    [InlineData("  ", StepState.ToDo)]
    [InlineData("https://jordan.example", StepState.ToDo)]
    [InlineData("jordan.example", StepState.NeedsAttention)]
    [InlineData("javascript:alert(1)", StepState.NeedsAttention)]
    public void ReviewNeedsAttention_OnlyWhenTheAddressIsFilledButNotAValidAddress(string? url, StepState expected)
    {
        var draft = Complete();
        draft.SiteUrl = url;

        Assert.Equal(expected, States(draft)[3]);
    }

    [Fact]
    public void NothingDependsOnTheStoredStepNumber()
    {
        var empty = new OnboardingDraft { Step = 4 };
        Assert.Equal([StepState.ToDo, StepState.ToDo, StepState.Optional, StepState.ToDo], States(empty));

        var complete = Complete();
        complete.Step = 0;
        Assert.Equal([StepState.Done, StepState.Done, StepState.Optional, StepState.ToDo], States(complete));
    }

    [Fact]
    public void EveryStateIsSaidInWords()
    {
        Assert.Equal("Done", new StepStatus(1, "x", "/x", StepState.Done).Text);
        Assert.Equal("To do", new StepStatus(1, "x", "/x", StepState.ToDo).Text);
        Assert.Equal("Needs attention", new StepStatus(1, "x", "/x", StepState.NeedsAttention).Text);
        Assert.Equal("Optional", new StepStatus(1, "x", "/x", StepState.Optional).Text);
    }

    [Fact]
    public void Resume_IsTheFirstStepThatIsNeitherDoneNorOptional()
    {
        Assert.Equal(1, WizardProgress.Resume(WizardProgress.Compute(null, Catalog)).Number);
        Assert.Equal(2, WizardProgress.Resume(WizardProgress.Compute(new OnboardingDraft { TemplateKey = "software" }, Catalog)).Number);
        Assert.Equal(2, WizardProgress.Resume(WizardProgress.Compute(new OnboardingDraft { TemplateKey = "software", OwnerName = "J" }, Catalog)).Number);

        var basicsDone = Complete();
        basicsDone.SiteUrl = null;
        Assert.Equal(4, WizardProgress.Resume(WizardProgress.Compute(basicsDone, Catalog)).Number); // the photo is optional, so it never holds the owner up
    }

    [Theory]
    [InlineData("1", 1)]
    [InlineData("4", 4)]
    [InlineData("0", 9)]
    [InlineData("5", 9)]
    [InlineData("-1", 9)]
    [InlineData("+2", 9)]
    [InlineData(" 2", 9)]
    [InlineData("2.0", 9)]
    [InlineData("abc", 9)]
    [InlineData("", 9)]
    [InlineData(null, 9)]
    public void ClampStep_AcceptsOnlyTheFourStepNumbers(string? requested, int expected) =>
        Assert.Equal(expected, WizardProgress.ClampStep(requested, 9));

    [Fact]
    public void TheChecklist_IsReady_OnlyWithATemplate_CompleteBasics_AndAValidAddress()
    {
        Assert.True(WizardProgress.Checklist(Complete(), Catalog).Ready);

        foreach (var broken in new Action<OnboardingDraft>[]
                 {
                     d => d.TemplateKey = null,
                     d => d.Email = "nope",
                     d => d.OwnerName = null,
                     d => d.SiteUrl = "jordan.example",
                     d => d.SiteUrl = null,
                 })
        {
            var draft = Complete();
            broken(draft);
            Assert.False(WizardProgress.Checklist(draft, Catalog).Ready);
        }
    }

    [Fact]
    public void ThePhoto_NeverBlocksPublishing()
    {
        var checklist = WizardProgress.Checklist(Complete(), Catalog);
        var photo = checklist.Lines.Single(l => l.Label == "Photo");

        Assert.False(photo.Done);
        Assert.False(photo.Required);
        Assert.True(checklist.Ready);
    }

    [Fact]
    public void TheReason_NamesWhatIsMissing_InOrder_AndIsNullWhenReady()
    {
        Assert.Null(WizardProgress.Checklist(Complete(), Catalog).Reason);

        var checklist = WizardProgress.Checklist(null, Catalog);

        Assert.Equal("Still to do: template, basics, site address.", checklist.Reason);
    }

    [Fact]
    public void EachChecklistLine_LinksToTheStepThatFixesIt()
    {
        var lines = WizardProgress.Checklist(null, Catalog).Lines;

        Assert.Equal(["Template", "Basics", "Photo", "Site address"], lines.Select(l => l.Label));
        Assert.Equal(["/admin/welcome", "/admin/welcome/basics", "/admin/welcome/photo", "/admin/welcome/review"], lines.Select(l => l.Path));
    }

    [Fact]
    public void TheBasicsLine_SaysWhichFieldsAreStillNeeded()
    {
        var draft = new OnboardingDraft { TemplateKey = "software", OwnerName = "Jordan", Email = "nope" };

        var basics = WizardProgress.Checklist(draft, Catalog).Lines.Single(l => l.Label == "Basics");

        Assert.False(basics.Done);
        Assert.Equal("Still needed: professional title, email, short summary.", basics.Detail);
    }

    [Fact]
    public void Clone_IsACopy_ThatCanBeChangedWithoutTouchingTheOriginal()
    {
        var original = Complete();

        var copy = original.Clone();
        copy.SiteUrl = "https://other.example";

        Assert.Equal("https://jordan.example", original.SiteUrl);
        Assert.Equal(original.OwnerName, copy.OwnerName);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Portfolio.Web.Tests --filter "FullyQualifiedName~WizardProgressTests"`
Expected: build FAILS with `The name 'WizardProgress' does not exist in the current context` (and `StepState`, `StepStatus`, `Clone`).

- [ ] **Step 3: Add `Clone` and the new `BasicsForm` members**

In `src/Portfolio.Web/Services/OnboardingDraft.cs`, inside `OnboardingDraft`, after `ToJson()`:

```csharp
    /// <summary>A copy that can be changed without touching the original (the review page checks a draft with the address as shown).</summary>
    public OnboardingDraft Clone() => (OnboardingDraft)MemberwiseClone();
```

Replace the `IsComplete` method at the end of `BasicsForm` (the doc comment `/// <summary>True when the stored values pass the same rules as the form...` through its closing brace) with:

```csharp
    /// <summary>True when every one of the five basics is empty (nothing has been typed on the step yet).</summary>
    public static bool AllEmpty(OnboardingDraft d) =>
        new[] { d.OwnerName, d.JobTitle, d.Email, d.Location, d.Summary }.All(string.IsNullOrWhiteSpace);

    /// <summary>The fields that would stop a publish, by lower-case name and in form order: a required one that is empty, an email that is not an address, or text that breaks a hard rule.
    /// Empty for a complete draft. A draft is data like any other, so this is asked again before publishing.</summary>
    public static IReadOnlyList<string> Missing(OnboardingDraft? d)
    {
        var rules = PublishRules.From(d ?? new OnboardingDraft());
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(rules, new ValidationContext(rules), results, validateAllProperties: true);
        return results.SelectMany(r => r.MemberNames).Distinct().Select(Describe).ToList();
    }

    /// <summary>True when the stored values pass the publish rules.</summary>
    public static bool IsComplete(OnboardingDraft d) => Missing(d).Count == 0;

    private static string Describe(string member) => member switch
    {
        nameof(OwnerName) => "name",
        nameof(JobTitle) => "professional title",
        nameof(Email) => "email",
        nameof(Location) => "location",
        _ => "short summary",
    };

    /// <summary>The completeness rules, applied to what a draft holds. Kept apart from the form so a half-typed step can still be saved.</summary>
    private sealed class PublishRules
    {
        [Required, StringLength(SiteProfile.MaxLength.OwnerName), NoMarkup]
        public string? OwnerName { get; init; }

        [Required, StringLength(SiteProfile.MaxLength.JobTitle), NoMarkup]
        public string? JobTitle { get; init; }

        [Required, StringLength(300), EmailAddress]
        public string? Email { get; init; }

        [StringLength(SiteProfile.MaxLength.Location), NoMarkup]
        public string? Location { get; init; }

        [Required, StringLength(SummaryMax)]
        public string? Summary { get; init; }

        public static PublishRules From(OnboardingDraft d) => new()
        {
            OwnerName = d.OwnerName, JobTitle = d.JobTitle, Email = d.Email, Location = d.Location, Summary = d.Summary,
        };
    }
```

(`BasicsForm` keeps its current attributes for now; Task 4 reduces them to the hard rules.)

- [ ] **Step 4: Create `WizardProgress.cs`**

Create `src/Portfolio.Web/Services/WizardProgress.cs`:

```csharp
using System.Globalization;

namespace Portfolio.Web.Services;

public enum StepState { Done, ToDo, NeedsAttention, Optional }

/// <summary>One wizard step as the indicator shows it. <see cref="Text"/> says the state in words, so colour is never the only signal.</summary>
public sealed record StepStatus(int Number, string Label, string Path, StepState State)
{
    public string Text => State switch
    {
        StepState.Done => "Done",
        StepState.NeedsAttention => "Needs attention",
        StepState.Optional => "Optional",
        _ => "To do",
    };

    /// <summary>A step that still wants something from the owner. Done and Optional steps never hold up resuming.</summary>
    public bool IsOpen => State is StepState.ToDo or StepState.NeedsAttention;
}

/// <summary>One line of the "Ready to publish?" checklist, with the step that fixes it. A line that is not <see cref="Required"/> (the photo) never blocks publishing.</summary>
public sealed record ChecklistLine(string Label, bool Done, string Detail, string Path, bool Required = true);

public sealed record PublishChecklist(IReadOnlyList<ChecklistLine> Lines)
{
    public bool Ready => Lines.All(l => l.Done || !l.Required);

    public IReadOnlyList<ChecklistLine> Missing => Lines.Where(l => !l.Done && l.Required).ToList();

    /// <summary>Why Publish is disabled, for the text beside the button; null when it is ready.</summary>
    public string? Reason => Ready ? null : "Still to do: " + string.Join(", ", Missing.Select(l => l.Label.ToLowerInvariant())) + ".";
}

/// <summary>
/// Where the owner is in the wizard, worked out from what the draft holds and nothing else (the stored <c>OnboardingDraft.Step</c> gates nothing).
/// Pure: no HTML and no I/O beyond the template catalog.
/// </summary>
public static class WizardProgress
{
    public static readonly IReadOnlyList<(int Number, string Label, string Path)> Steps =
    [
        (1, "Template", "/admin/welcome"),
        (2, "Basics", "/admin/welcome/basics"),
        (3, "Photo", "/admin/welcome/photo"),
        (4, "Review and launch", "/admin/welcome/review"),
    ];

    /// <summary>The four steps in order. Template: done once the draft names a catalog template. Basics: to do while all five are empty, done when complete, otherwise needs attention.
    /// Photo: done with a draft photo, else optional. Review: needs attention when an address is filled but is not one, otherwise to do (publishing ends the draft).</summary>
    public static IReadOnlyList<StepStatus> Compute(OnboardingDraft? draft, ITemplateCatalog catalog)
    {
        var template = draft is not null && catalog.TryGet(draft.TemplateKey, out _) ? StepState.Done : StepState.ToDo;
        var basics = draft is null || BasicsForm.AllEmpty(draft) ? StepState.ToDo
            : BasicsForm.IsComplete(draft) ? StepState.Done
            : StepState.NeedsAttention;
        var photo = draft?.PhotoDraft is not null ? StepState.Done : StepState.Optional;
        var review = draft is { SiteUrl: { } url } && !string.IsNullOrWhiteSpace(url) && !OnboardingService.IsValidAddress(url)
            ? StepState.NeedsAttention
            : StepState.ToDo;

        StepStatus At(int index, StepState state) => new(Steps[index].Number, Steps[index].Label, Steps[index].Path, state);
        return [At(0, template), At(1, basics), At(2, photo), At(3, review)];
    }

    /// <summary>The checklist beside the Publish button: template, basics, photo (optional) and site address, each with the step that fixes it.</summary>
    public static PublishChecklist Checklist(OnboardingDraft? draft, ITemplateCatalog catalog)
    {
        var steps = Compute(draft, catalog);
        var templateDone = steps[0].State == StepState.Done;
        var basicsDone = steps[1].State == StepState.Done;
        var photoDone = steps[2].State == StepState.Done;
        var address = draft?.SiteUrl;
        var addressDone = OnboardingService.IsValidAddress(address);

        return new PublishChecklist(
        [
            new("Template", templateDone, templateDone ? "Chosen." : "Choose a template.", steps[0].Path),
            new("Basics", basicsDone, basicsDone ? "Complete." : "Still needed: " + string.Join(", ", BasicsForm.Missing(draft)) + ".", steps[1].Path),
            new("Photo", photoDone, photoDone ? "Added." : "Optional. Your initials are shown without one.", steps[2].Path, Required: false),
            new("Site address", addressDone, addressDone ? address! : string.IsNullOrWhiteSpace(address)
                ? "Enter the address your site will be published at."
                : "Enter a full web address starting with http:// or https://.", steps[3].Path),
        ]);
    }

    /// <summary>Where "Continue setup" goes: the first step that is neither done nor optional. The review step is never done, so there always is one.</summary>
    public static StepStatus Resume(IReadOnlyList<StepStatus> steps) => steps.FirstOrDefault(s => s.IsOpen) ?? steps[^1];

    /// <summary>The step a request asked for (<c>goto</c>). Untrusted: anything that is not exactly 1 to 4 becomes <paramref name="fallback"/>.</summary>
    public static int ClampStep(string? requested, int fallback) =>
        int.TryParse(requested, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n is >= 1 and <= OnboardingService.LastStep ? n : fallback;
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/Portfolio.Web.Tests --filter "FullyQualifiedName~WizardProgressTests"`
Expected: PASS, every test in the class. If `Basics_NeedAttention_WhenTheEmailIsNotAnAddress` fails because `Missing` returns an empty list, check that `Validator.TryValidateObject` is given `validateAllProperties: true` (a `[Required]`/`[EmailAddress]` failure only shows then).

- [ ] **Step 6: Run the existing wizard tests to confirm nothing moved**

Run: `dotnet test tests/Portfolio.Web.Tests --filter "FullyQualifiedName~Onboarding"`
Expected: PASS (only additions so far; `IsComplete` still means the same thing).

- [ ] **Step 7: Commit**

```bash
git add src/Portfolio.Web/Services/WizardProgress.cs src/Portfolio.Web/Services/OnboardingDraft.cs tests/Portfolio.Web.Tests/Services/WizardProgressTests.cs
git commit -m "feat(admin): wizard step status and publish checklist computed from the draft

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 2: `OnboardingService` works with partial drafts (criteria 16, and the service side of 15 and 19)

**Files:**
- Modify: `src/Portfolio.Web/Services/OnboardingService.cs` (`SaveChoicesAsync` at 175-189, `DraftPreviewAsync` at 359-369, `Apply` at 396, plus new members)
- Test: `tests/Portfolio.Web.Tests/Admin/OnboardingPartialDraftTests.cs`

**Interfaces:**
- Consumes: Task 1's `WizardProgress`, `StepStatus`, `PublishChecklist`, `BasicsForm.IsComplete`.
- Produces:
  - `OnboardingService.TemplateFor(OnboardingState state) -> TemplateDefinition` (the draft's template, else the saved site's, else the default on a first run; an unknown key falls back like any catalog lookup)
  - `OnboardingService.ProgressOf(OnboardingDraft? draft) -> IReadOnlyList<StepStatus>`
  - `OnboardingService.ChecklistFor(OnboardingDraft? draft) -> PublishChecklist`
  - `OnboardingService.KeepTemplateAsync(CancellationToken ct = default) -> Task<string?>` (null when a template is in the draft or was saved; otherwise the refusal message from `SaveTemplateAsync`)
  - `SaveChoicesAsync` now saves into a draft that has no template yet (it checks the accent and starting content against `TemplateFor`).

- [ ] **Step 1: Write the failing tests**

Create `tests/Portfolio.Web.Tests/Admin/OnboardingPartialDraftTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Portfolio.Web.Services;
using Portfolio.Web.Tests.Support;
using Xunit;

namespace Portfolio.Web.Tests.Admin;

/// <summary>The service saves whatever has been typed, in any order, and never needs an earlier step first. Completeness is only asked at publishing.</summary>
public class OnboardingPartialDraftTests
{
    private static async Task<T> WithServiceAsync<T>(AdminApp app, Func<OnboardingService, Task<T>> act)
    {
        using var scope = app.Services.CreateScope();
        return await act(scope.ServiceProvider.GetRequiredService<OnboardingService>());
    }

    [Fact]
    public async Task SaveBasics_StoresPartialInput_WithoutATemplate()
    {
        using var app = new AdminApp();
        await app.SignedInClientAsync();

        await WithServiceAsync(app, async s => { await s.SaveBasicsAsync(new BasicsForm { OwnerName = "  Jordan  " }); return 0; });

        var draft = await WizardFlow.DraftOrNullAsync(app);
        Assert.Equal("Jordan", draft!.OwnerName);
        Assert.Null(draft.Email);
        Assert.Null(draft.JobTitle);
        Assert.Null(draft.TemplateKey);
        Assert.Equal(StepState.NeedsAttention, WizardProgress.Compute(draft, app.Services.GetRequiredService<ITemplateCatalog>())[1].State);
    }

    [Fact]
    public async Task SaveChoices_CreatesTheDraft_AndStoresAnAddressThatIsNotYetValid()
    {
        using var app = new AdminApp();
        await app.SignedInClientAsync();

        var saved = await WithServiceAsync(app, s => s.SaveChoicesAsync(new ReviewForm { SiteUrl = "jordan.example", Accent = "", Content = StarterChoice.Blank }));

        Assert.True(saved);
        var draft = await WizardFlow.DraftOrNullAsync(app);
        Assert.Equal("jordan.example", draft!.SiteUrl);
        Assert.Null(draft.TemplateKey);
    }

    [Fact]
    public async Task SaveChoices_StillRefusesAnAccentTheTemplateDoesNotOffer()
    {
        using var app = new AdminApp();
        await app.SignedInClientAsync();

        var saved = await WithServiceAsync(app, s => s.SaveChoicesAsync(new ReviewForm { SiteUrl = "https://jordan.example", Accent = "no-such-accent", Content = StarterChoice.Blank }));

        Assert.False(saved);
        Assert.Null(await WizardFlow.DraftOrNullAsync(app));
    }

    [Fact]
    public async Task TemplateFor_IsTheDraftsTemplate_ElseTheSitesOwn_AndIgnoresAnUnknownKey()
    {
        using var app = new AdminApp();
        await app.SignedInClientAsync();
        var catalog = app.Services.GetRequiredService<ITemplateCatalog>();

        var site = await WithServiceAsync(app, async s => s.TemplateFor(await s.GetStateAsync()));
        Assert.Equal(TemplateCatalog.DefaultKey, site.Key);

        await WithServiceAsync(app, async s => await s.SaveTemplateAsync(catalog.Get("healthcare")));
        var chosen = await WithServiceAsync(app, async s => s.TemplateFor(await s.GetStateAsync()));
        Assert.Equal("healthcare", chosen.Key);

        var unknown = await WithServiceAsync(app, async s =>
        {
            var state = await s.GetStateAsync();
            return s.TemplateFor(new OnboardingState(state.Profile, new OnboardingDraft { TemplateKey = "no-such-template" }));
        });
        Assert.Equal(TemplateCatalog.DefaultKey, unknown.Key);
    }

    [Fact]
    public async Task KeepTemplate_CreatesADraftOnTheCurrentTemplate_AndLeavesAChosenOneAlone()
    {
        using var app = new AdminApp();
        await app.SignedInClientAsync();
        var catalog = app.Services.GetRequiredService<ITemplateCatalog>();

        Assert.Null(await WithServiceAsync(app, s => s.KeepTemplateAsync()));
        Assert.Equal(TemplateCatalog.DefaultKey, (await WizardFlow.DraftOrNullAsync(app))!.TemplateKey);

        await WithServiceAsync(app, async s => await s.SaveTemplateAsync(catalog.Get("healthcare")));
        Assert.Null(await WithServiceAsync(app, s => s.KeepTemplateAsync()));
        Assert.Equal("healthcare", (await WizardFlow.DraftOrNullAsync(app))!.TemplateKey);
    }

    [Fact]
    public async Task ThePreview_AppliesEachFieldThatHasAValue_AndKeepsTheSavedValueForTheRest()
    {
        using var app = new AdminApp();
        await app.SignedInClientAsync();
        var saved = await WizardFlow.ProfileAsync(app);
        await WithServiceAsync(app, async s => { await s.SaveBasicsAsync(new BasicsForm { OwnerName = "Part Person" }); return 0; });

        var preview = await WithServiceAsync(app, s => s.DraftPreviewAsync());

        Assert.NotNull(preview); // a draft without a template previews on the template the site is on
        Assert.Equal("Part Person", preview!.OwnerName);
        Assert.Equal(saved.JobTitle, preview.JobTitle);
    }

    [Fact]
    public async Task AnEmptyLocation_ClearsTheSavedOne_OnlyOnceTheBasicsAreComplete()
    {
        using var app = new AdminApp();
        await app.SignedInClientAsync();
        using (var db = app.CreateDbContext())
        {
            var profile = await db.SiteProfiles.SingleAsync();
            profile.Location = "Leeds, UK";
            await db.SaveChangesAsync();
        }

        await WithServiceAsync(app, async s => { await s.SaveBasicsAsync(new BasicsForm { OwnerName = "Part Person" }); return 0; });
        var partial = await WithServiceAsync(app, s => s.DraftPreviewAsync());
        Assert.Equal("Leeds, UK", partial!.Location);

        await WithServiceAsync(app, async s => { await s.SaveBasicsAsync(new BasicsForm { OwnerName = "Jordan", JobTitle = "Nurse", Email = "j@owner.example", Summary = "Hello." }); return 0; });
        var complete = await WithServiceAsync(app, s => s.DraftPreviewAsync());
        Assert.True(string.IsNullOrEmpty(complete!.Location));

        await WithServiceAsync(app, async s => { await s.SaveBasicsAsync(new BasicsForm { OwnerName = "Jordan", Location = "Bristol" }); return 0; });
        var typed = await WithServiceAsync(app, s => s.DraftPreviewAsync());
        Assert.Equal("Bristol", typed!.Location);
    }

    [Fact]
    public async Task PublishingAnIncompleteDraft_IsStillRefused_ByTheService()
    {
        using var app = new AdminApp();
        await app.SignedInClientAsync();
        var catalog = app.Services.GetRequiredService<ITemplateCatalog>();
        await WithServiceAsync(app, async s => await s.SaveTemplateAsync(catalog.Get("software")));
        await WithServiceAsync(app, async s => { await s.SaveBasicsAsync(new BasicsForm { OwnerName = "Jordan", Email = "nope" }); return 0; });
        await WithServiceAsync(app, s => s.SaveChoicesAsync(new ReviewForm { SiteUrl = "https://jordan.example", Accent = "", Content = StarterChoice.Blank }));
        var before = WizardFlow.PublicFingerprint(await WizardFlow.ProfileAsync(app));

        var result = await WithServiceAsync(app, s => s.PublishAsync());

        Assert.Equal(PublishOutcome.Incomplete, result.Outcome);
        Assert.Equal(2, result.Step);
        Assert.Equal(before, WizardFlow.PublicFingerprint(await WizardFlow.ProfileAsync(app)));
        Assert.NotNull(await WizardFlow.DraftOrNullAsync(app)); // the draft is kept
    }
}
```

- [ ] **Step 2: Add the test helper `DraftOrNullAsync`**

In `tests/Portfolio.Web.Tests/Support/WizardFlow.cs`, after `DraftAsync`:

```csharp
    /// <summary>The stored draft, or null when there is no profile row or no draft (a read that cannot fail on a site nothing has written to yet).</summary>
    public static async Task<OnboardingDraft?> DraftOrNullAsync(AdminApp app)
    {
        using var db = app.CreateDbContext();
        var json = await db.SiteProfiles.AsNoTracking().Select(p => p.OnboardingDraft).SingleOrDefaultAsync();
        return OnboardingDraft.Parse(json);
    }
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/Portfolio.Web.Tests --filter "FullyQualifiedName~OnboardingPartialDraftTests"`
Expected: build FAILS with `'OnboardingService' does not contain a definition for 'TemplateFor'` (and `KeepTemplateAsync`).

- [ ] **Step 4: Add the new members to `OnboardingService`**

In `src/Portfolio.Web/Services/OnboardingService.cs`, after `GetStateAsync` (line 101), add:

```csharp
    /// <summary>The template the wizard shows for this state: the draft's own, else the saved site's, else the default on a first run. An unknown key falls back like any catalog lookup.</summary>
    public TemplateDefinition TemplateFor(OnboardingState state) =>
        _catalog.Get(state.Draft?.TemplateKey ?? (state.FirstRun ? TemplateCatalog.DefaultKey : state.Profile.TemplateKey));

    /// <summary>The four steps' status for the indicator.</summary>
    public IReadOnlyList<StepStatus> ProgressOf(OnboardingDraft? draft) => WizardProgress.Compute(draft, _catalog);

    /// <summary>The "Ready to publish?" lines for a draft (pass a copy with the address as shown to count what the owner can see).</summary>
    public PublishChecklist ChecklistFor(OnboardingDraft? draft) => WizardProgress.Checklist(draft, _catalog);
```

After `SaveTemplateAsync` (ends at line 126), add:

```csharp
    /// <summary>Leaving step 1 without pressing Use keeps the template the owner was looking at: the draft's if it has one, else the one <see cref="TemplateFor"/> shows (the site's, or the default on a first run).
    /// Never changes a template that is already in the draft.</summary>
    /// <returns>Null when the draft has a template now; otherwise the refusal from <see cref="SaveTemplateAsync"/>.</returns>
    public async Task<string?> KeepTemplateAsync(CancellationToken ct = default)
    {
        var state = await GetStateAsync(ct);
        if (state.Draft is { } draft && _catalog.TryGet(draft.TemplateKey, out _)) return null;
        return await SaveTemplateAsync(TemplateFor(state), ct);
    }
```

Replace the doc comment and first lines of `SaveBasicsAsync` so it says what is true now (the body is unchanged: `Cleaned()` already turns blanks into null):

```csharp
    /// <summary>Step 2. Saves whatever was typed, complete or not: only the hard rules apply (the page checks them), and completeness is asked at publishing. The values are cleaned of control
    /// characters here, and a blank one is stored as null.</summary>
```

Replace `SaveChoicesAsync` (lines 175-189) with:

```csharp
    /// <summary>Step 4 choices, saved under the hard rules only: an accent must be one the template offers, content one of the known kinds. The address is stored as typed (cleaned); whether it is a valid
    /// address is asked at publishing. Works before a template is chosen, checking against <see cref="TemplateFor"/>.</summary>
    public async Task<bool> SaveChoicesAsync(ReviewForm form, CancellationToken ct = default)
    {
        var state = await GetStateAsync(ct);
        var draft = state.Draft ?? new OnboardingDraft();
        var template = TemplateFor(state);
        var accent = string.IsNullOrWhiteSpace(form.Accent) ? null : form.Accent.Trim();
        if (accent is not null && !template.Theme.Accents.Any(a => string.Equals(a.Key, accent, StringComparison.Ordinal))) return false;
        if (!StarterChoice.IsKnown(form.Content) || (form.Content == StarterChoice.Sample && !StarterContent.Offers(template))) return false;

        draft.AccentKey = accent;
        draft.Content = form.Content;
        draft.SiteUrl = ProfileForm.Line(form.SiteUrl);
        await SaveDraftAsync(state, draft, ct);
        return true;
    }
```

Replace `DraftPreviewAsync` (lines 359-369) with:

```csharp
    /// <summary>The site as it would look once published (identity, template, accent and draft photo), for the owner's draft preview. Each draft field that has a value is applied and the saved profile's value
    /// stays for any field still empty, so a half-finished draft previews too, on <see cref="TemplateFor"/> until a template is chosen. Null only without a draft. Sample rows are not part of it: they are added at publish.</summary>
    public async Task<SiteOptions?> DraftPreviewAsync(CancellationToken ct = default)
    {
        var state = await GetStateAsync(ct);
        if (state.Draft is not { } draft) return null;

        var copy = SiteProfileMapper.ToEntity(SiteProfileMapper.ToOptions(state.Profile));
        Apply(copy, draft, TemplateFor(state), state.FirstRun, draft.PhotoDraft is null ? null : DraftPhotoPath, _placeholder);
        return SiteProfileMapper.ToOptions(copy);
    }
```

In `Apply`, replace `if (d.Step >= 2) p.Location = d.Location;` with:

```csharp
        // An empty location clears the saved one only once the basics are complete (always so at publishing): a half-typed draft never blanks it. A typed location always applies.
        if (!string.IsNullOrWhiteSpace(d.Location) || BasicsForm.IsComplete(d)) p.Location = d.Location;
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/Portfolio.Web.Tests --filter "FullyQualifiedName~OnboardingPartialDraftTests"`
Expected: PASS.

- [ ] **Step 6: Run the wizard and template suites**

Run: `dotnet test tests/Portfolio.Web.Tests --filter "FullyQualifiedName~Onboarding|FullyQualifiedName~WizardRace|FullyQualifiedName~TemplatePicker"`
Expected: PASS. The pages still validate with the old attributes, so nothing user-visible has changed yet. If a preview test now shows a different location, re-read the `Apply` condition: at publish the basics are complete, so a complete draft behaves exactly as before.

- [ ] **Step 7: Commit**

```bash
git add src/Portfolio.Web/Services/OnboardingService.cs tests/Portfolio.Web.Tests/Admin/OnboardingPartialDraftTests.cs tests/Portfolio.Web.Tests/Support/WizardFlow.cs
git commit -m "feat(admin): onboarding service saves and previews partial drafts

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 3: The card frame, the clickable indicator, and the Template step's jump (criteria 15, 18, 21)

This task builds the shared pieces every step uses and converts step 1. Steps 2 to 4 are converted in Tasks 4 to 6; until then their indicator buttons have no form to submit, which is fine because nothing ships between tasks.

**Files:**
- Modify: `src/Portfolio.Web/Pages/Admin/Welcome/WelcomePageModel.cs`
- Modify: `src/Portfolio.Web/Pages/Admin/Welcome/_WizardHeader.cshtml`
- Modify: `src/Portfolio.Web/Pages/Admin/Welcome/_WizardFooter.cshtml`
- Modify: `src/Portfolio.Web/Pages/Admin/Welcome/Template.cshtml`, `Template.cshtml.cs`
- Modify: `src/Portfolio.Web/wwwroot/css/admin.css` (lines 133-136, plus appended rules)
- Modify: `tests/Portfolio.Web.Tests/Support/WizardFlow.cs`, `tests/Portfolio.Web.Tests/Admin/OnboardingAccessibilityAccentTests.cs`
- Test: `tests/Portfolio.Web.Tests/Admin/WizardFrameTests.cs`

**Interfaces:**
- Consumes: Task 1 and Task 2 members.
- Produces (Tasks 4 to 6 rely on these exactly):
  - `WelcomePageModel.Goto : string?` (`[BindProperty(Name = "goto")]`), `Progress : IReadOnlyList<StepStatus>`, `NextStep : int`, `virtual string? ContinueLabel` (null by default), `protected IActionResult Leave()` (redirects to `WizardProgress.ClampStep(Goto, NextStep)`'s path)
  - Markup contract: every wizard page has exactly one `<form id="wizard-form" ...>` holding the step's fields; the header renders `<button type="submit" form="wizard-form" name="goto" value="N" ...>` in this attribute order (tests match it); the footer renders the card's Continue in the same shape.
  - `WizardFlow.JumpAsync(HttpClient client, string page, int to, Dictionary<string,string>? fields = null)`, `WizardFlow.StepValues(string html)`, `WizardFlow.StepStates(string html)`.

- [ ] **Step 1: Add the test helpers**

In `tests/Portfolio.Web.Tests/Support/WizardFlow.cs`, after `PublishAsync`:

```csharp
    /// <summary>Posts a step's form the way the step indicator does: the step's own fields plus <c>goto</c>. The template step has no fields of its own; its form is the Jump handler.</summary>
    public static Task<HttpResponseMessage> JumpAsync(HttpClient client, string page, int to, Dictionary<string, string>? fields = null)
    {
        var all = new Dictionary<string, string>(fields ?? []) { ["goto"] = to.ToString() };
        return PostAsync(client, page == Template ? Template + "?handler=Jump" : page, all, page);
    }

    /// <summary>The value of every submit button tied to the step's form, in page order: the hidden default first, then the four steps, then the card's Continue when it has one.</summary>
    public static IReadOnlyList<string> StepValues(string html) =>
        Regex.Matches(html, @"<button type=""submit"" form=""wizard-form"" name=""goto"" value=""(\d)""").Select(m => m.Groups[1].Value).ToList();

    /// <summary>The status words in the indicator, in step order.</summary>
    public static IReadOnlyList<string> StepStates(string html) =>
        Regex.Matches(html, @"<span class=""adm-step-state"">([^<]+)</span>").Select(m => m.Groups[1].Value).ToList();
```

- [ ] **Step 2: Write the failing tests**

Create `tests/Portfolio.Web.Tests/Admin/WizardFrameTests.cs`:

```csharp
using System.Net;
using System.Text.RegularExpressions;
using Portfolio.Web.Services;
using Portfolio.Web.Tests.Support;
using Xunit;

namespace Portfolio.Web.Tests.Admin;

/// <summary>The card frame every step shares (indicator, status, saved-at line, bottom bar) and the Template step's jump.</summary>
public class WizardFrameTests
{
    [Fact]
    public async Task EveryPage_OffersAllFourSteps_AsSubmitButtons_AfterAHiddenDefaultButton()
    {
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();

        foreach (var (page, next) in new[] { (WizardFlow.Template, "2"), (WizardFlow.Basics, "3"), (WizardFlow.Photo, "4"), (WizardFlow.Review, "4") })
        {
            var html = await owner.GetStringAsync(page);
            var values = WizardFlow.StepValues(html);

            Assert.Equal(next, values[0]); // Enter in a text field submits through the first submit button, so that must be "the next step", never step 1
            Assert.Equal(["1", "2", "3", "4"], values.Skip(1).Take(4));
            Assert.Matches(@"<button type=""submit"" form=""wizard-form"" name=""goto"" value=""" + next + @""" class=""adm-sr"" tabindex=""-1"" aria-hidden=""true"">", html);
        }
    }

    [Fact]
    public async Task EveryStep_SaysItsStatusInWords_FromTheDraft()
    {
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();

        Assert.Equal(["To do", "To do", "Optional", "To do"], WizardFlow.StepStates(await owner.GetStringAsync(WizardFlow.Template)));

        await WizardFlow.PickTemplateAsync(owner, "healthcare");
        Assert.Equal(["Done", "To do", "Optional", "To do"], WizardFlow.StepStates(await owner.GetStringAsync(WizardFlow.Template)));
    }

    [Fact]
    public async Task TheCurrentStep_IsMarkedByAriaCurrent_NotOnlyByColour()
    {
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();

        var html = await owner.GetStringAsync(WizardFlow.Template);

        Assert.Single(Regex.Matches(html, "aria-current=\"step\""));
        Assert.Matches(@"<li aria-current=""step"" class=""adm-step adm-step-todo"">\s*<button type=""submit"" form=""wizard-form"" name=""goto"" value=""1"" class=""adm-step-btn"">", html);
    }

    [Fact]
    public async Task TheTemplateStep_IsOneCard_WithAFormTheIndicatorSubmits()
    {
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();

        var html = await owner.GetStringAsync(WizardFlow.Template);

        Assert.Single(Regex.Matches(html, @"class=""adm-card adm-wizard"""));
        Assert.Single(Regex.Matches(html, @"<form id=""wizard-form"""));
        Assert.Contains("Template · Step 1 of 4", Regex.Match(html, "<title>(.*?)</title>").Groups[1].Value);
    }

    [Fact]
    public async Task TheSavedAtLine_AppearsOnlyWithADraft_AsATimeWithItsZone()
    {
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();
        Assert.DoesNotContain("Draft saved at", await owner.GetStringAsync(WizardFlow.Template));

        await WizardFlow.PickTemplateAsync(owner, "software");

        Assert.Matches(@"Draft saved at <time datetime=""[^""]+"">\d\d:\d\d UTC</time>", await owner.GetStringAsync(WizardFlow.Template));
    }

    [Fact]
    public async Task TheBottomBar_OffersSkipOnAFirstRun_AndStartOverOnlyWithADraft()
    {
        using var app = new AdminApp();
        var owner = await WizardFlow.FirstRunClientAsync(app);

        var fresh = await owner.GetStringAsync(WizardFlow.Template);
        Assert.Contains(">Skip setup</button>", fresh);
        Assert.DoesNotContain("Start over", fresh);

        await WizardFlow.PickTemplateAsync(owner, "software");
        Assert.Contains(">Start over</button>", await owner.GetStringAsync(WizardFlow.Template));
    }

    [Fact]
    public async Task TheBottomBar_OffersLeaveWithoutPublishing_OnAReRun()
    {
        using var app = new AdminApp();
        var owner = await WizardFlow.FirstRunClientAsync(app);
        await WizardFlow.PickTemplateAsync(owner, "software");
        await WizardFlow.SaveBasicsAsync(owner);
        await WizardFlow.PublishAsync(owner);

        var html = await owner.GetStringAsync(WizardFlow.Template);

        Assert.Contains(">Leave without publishing</button>", html);
        Assert.DoesNotContain(">Skip setup</button>", html);
    }

    [Fact]
    public async Task JumpingFromTheTemplateStep_WithNoDraft_CreatesOneOnTheDefaultTemplate_AndGoesWhereAsked()
    {
        using var app = new AdminApp();
        var owner = await WizardFlow.FirstRunClientAsync(app);

        var jump = await WizardFlow.JumpAsync(owner, WizardFlow.Template, 3);

        Assert.Equal(HttpStatusCode.Redirect, jump.StatusCode);
        Assert.Equal("/admin/welcome/photo", WizardFlow.Where(jump));
        Assert.Equal(TemplateCatalog.DefaultKey, (await WizardFlow.DraftOrNullAsync(app))!.TemplateKey);
    }

    [Fact]
    public async Task JumpingFromTheTemplateStep_KeepsTheDraftsTemplate()
    {
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();
        await WizardFlow.PickTemplateAsync(owner, "healthcare");

        await WizardFlow.JumpAsync(owner, WizardFlow.Template, 4);

        Assert.Equal("healthcare", (await WizardFlow.DraftOrNullAsync(app))!.TemplateKey);
    }

    [Fact]
    public async Task OnAReRun_JumpingFromTheTemplateStep_KeepsTheSitesCurrentTemplate()
    {
        using var app = new AdminApp();
        var owner = await WizardFlow.FirstRunClientAsync(app);
        await WizardFlow.PickTemplateAsync(owner, "healthcare");
        await WizardFlow.SaveBasicsAsync(owner);
        await WizardFlow.PublishAsync(owner);
        Assert.Null(await WizardFlow.DraftOrNullAsync(app)); // published: the draft is gone

        await WizardFlow.JumpAsync(owner, WizardFlow.Template, 2);

        Assert.Equal("healthcare", (await WizardFlow.DraftOrNullAsync(app))!.TemplateKey);
    }

    [Theory]
    [InlineData("99")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("2.0")]
    public async Task AnUnknownGoto_FallsBackToTheNextStep(string requested)
    {
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();
        var token = await AdminApp.GetTokenAsync(owner, WizardFlow.Template);

        var response = await AdminApp.PostFormAsync(owner, WizardFlow.Template + "?handler=Jump", token, new Dictionary<string, string> { ["goto"] = requested });

        Assert.Equal("/admin/welcome/basics", WizardFlow.Where(response));
    }

    [Fact]
    public async Task JumpingSavesNothingPublic()
    {
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();
        var before = WizardFlow.PublicFingerprint(await WizardFlow.ProfileAsync(app));

        await WizardFlow.JumpAsync(owner, WizardFlow.Template, 4);

        Assert.Equal(before, WizardFlow.PublicFingerprint(await WizardFlow.ProfileAsync(app)));
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/Portfolio.Web.Tests --filter "FullyQualifiedName~WizardFrameTests"`
Expected: FAIL (`StepValues` is empty because the header still renders links; the `Jump` handler does not exist, so the jump returns 400 or a re-render).

- [ ] **Step 4: Update `WelcomePageModel`**

Replace the contents of `src/Portfolio.Web/Pages/Admin/Welcome/WelcomePageModel.cs` with:

```csharp
using Microsoft.AspNetCore.Mvc;
using Portfolio.Web.Services;

namespace Portfolio.Web.Pages.Admin.Welcome;

/// <summary>
/// Shared by the four wizard steps: the saved draft, the template the wizard shows, the status of each step for the indicator, where a save goes next
/// (<see cref="Leave"/>), and the two actions every step offers (leave without publishing, start over). Every handler here is a POST; a GET only ever reads.
/// </summary>
public abstract class WelcomePageModel : AdminPageModel
{
    /// <summary>The four steps. Kept so existing callers keep compiling; <see cref="WizardProgress.Steps"/> is the one list.</summary>
    public static IReadOnlyList<(int Number, string Label, string Path)> Steps => WizardProgress.Steps;

    protected WelcomePageModel(OnboardingService onboarding, ITemplateCatalog catalog)
    {
        Onboarding = onboarding;
        Catalog = catalog;
    }

    protected OnboardingService Onboarding { get; }
    protected ITemplateCatalog Catalog { get; }

    public abstract int CurrentStep { get; }

    /// <summary>The step the owner asked to go to: an indicator button, or the card's Continue. Untrusted: it is only ever clamped to one of the four steps (<see cref="WizardProgress.ClampStep"/>).</summary>
    [BindProperty(Name = "goto")]
    public string? Goto { get; set; }

    /// <summary>The label of the card's Continue button; null when the step has its own (the template step's Use button, the review step's Publish).</summary>
    public virtual string? ContinueLabel => null;

    public OnboardingState State { get; private set; } = null!;

    /// <summary>The template the wizard shows: the draft's, else the saved site's, else the default on a first run (<see cref="OnboardingService.TemplateFor"/>).</summary>
    public TemplateDefinition Template { get; private set; } = null!;

    /// <summary>Each step's status, computed from the draft alone.</summary>
    public IReadOnlyList<StepStatus> Progress { get; private set; } = [];

    public bool FirstRun => State.FirstRun;
    public OnboardingDraft? Draft => State.Draft;
    public string StepLabel => WizardProgress.Steps[CurrentStep - 1].Label;

    /// <summary>The step after this one; the last step is its own next.</summary>
    public int NextStep => Math.Min(CurrentStep + 1, OnboardingService.LastStep);

    /// <summary>The furthest step the owner may open: the next unsaved step (the photo is optional, so once the basics are saved the review is open too). Removed with <see cref="RequireStep"/> once every step is converted.</summary>
    public int Reachable => Draft is null ? 1 : Draft.Step >= 2 ? OnboardingService.LastStep : Draft.Step + 1;

    protected async Task LoadAsync(CancellationToken ct)
    {
        State = await Onboarding.GetStateAsync(ct);
        Template = Onboarding.TemplateFor(State);
        Progress = Onboarding.ProgressOf(State.Draft);
    }

    /// <summary>Where a save goes next: the step <see cref="Goto"/> asked for, else the step after this one. A local path from a fixed list, never the posted value.</summary>
    protected IActionResult Leave() => Redirect(WizardProgress.Steps[WizardProgress.ClampStep(Goto, NextStep) - 1].Path);

    /// <summary>Sends the owner back to an earlier step whose answers are missing, so a step is never shown without what it builds on. Removed once every step is converted.</summary>
    protected IActionResult? RequireStep(int saved)
    {
        if (Draft is null || !Catalog.TryGet(Draft.TemplateKey, out _) || Draft.Step < saved)
        {
            FlashError(saved <= 1 ? "Choose a template first." : "Fill in your basics first.");
            return RedirectToPage(saved <= 1 ? "/Admin/Welcome/Template" : "/Admin/Welcome/Basics");
        }
        return null;
    }

    /// <summary>Leaves the wizard without publishing (see <see cref="OnboardingService.SkipAsync"/>).</summary>
    public async Task<IActionResult> OnPostSkipAsync(CancellationToken ct)
    {
        switch (await Onboarding.SkipAsync(ct))
        {
            case SkipOutcome.Skipped:
                Flash("Setup skipped. The site keeps its placeholder profile and demo content; run the wizard again from the dashboard whenever you like. You can add your own content under Content at any time.");
                break;
            case SkipOutcome.DraftDiscarded:
                Flash("Nothing was published. Your draft was discarded and the site is unchanged.");
                break;
            case SkipOutcome.AlreadyFinished:
                Flash("Setup was already finished in another tab, so nothing changed.");
                break;
            default:
                FlashError("Your profile was changed while setup was being skipped, so nothing was recorded. Setup is still open: choose Skip setup again, or continue it.");
                break;
        }
        return Redirect("/admin");
    }

    /// <summary>Throws the draft away and starts again at step 1. The live site and its content are untouched.</summary>
    public async Task<IActionResult> OnPostDiscardAsync(CancellationToken ct)
    {
        await Onboarding.DiscardAsync(ct);
        Flash("Draft discarded. Nothing on the live site changed.");
        return RedirectToPage("/Admin/Welcome/Template");
    }
}
```

- [ ] **Step 5: Replace `_WizardHeader.cshtml`**

Replace the whole file `src/Portfolio.Web/Pages/Admin/Welcome/_WizardHeader.cshtml` with:

```razor
@using System.Globalization
@model WelcomePageModel
@* The step indicator, the polite live region and the step heading. Each step is a full page load, so the heading takes focus on load (autofocus) and the live region
   carries the step and any message for screen readers. When the page has errors the error summary takes the focus instead.
   Every step in the indicator is a submit button of the step's form (form="wizard-form"): choosing one saves what was typed on this step, then opens that step.
   The first button is a hidden default: pressing Enter in a text field submits through the first submit button in the page, and that must mean "continue", not step 1. *@
<button type="submit" form="wizard-form" name="goto" value="@Model.NextStep" class="adm-sr" tabindex="-1" aria-hidden="true">Continue</button>
<nav aria-label="Setup progress">
    <ol class="adm-steps">
        @foreach (var step in Model.Progress)
        {
            var current = step.Number == Model.CurrentStep;
            <li aria-current="@(current ? "step" : null)" class="adm-step adm-step-@(step.State.ToString().ToLowerInvariant())">
                <button type="submit" form="wizard-form" name="goto" value="@step.Number" class="adm-step-btn">
                    <span class="adm-step-num">@step.Number</span> @step.Label
                    <span class="adm-step-state">@step.Text</span>
                </button>
            </li>
        }
    </ol>
</nav>
@if (Model.Draft is { } saved && saved.UpdatedAt != default)
{
    <p class="adm-draft-saved">Draft saved at <time datetime="@saved.UpdatedAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)">@saved.UpdatedAt.UtcDateTime.ToString("HH':'mm", CultureInfo.InvariantCulture) UTC</time></p>
}
<div id="wizard-status" class="adm-sr" role="status" aria-live="polite" aria-atomic="true">Step @Model.CurrentStep of @OnboardingService.LastStep: @Model.StepLabel.</div>
<h1 id="step-heading" tabindex="-1" autofocus="@(ViewData.ModelState.IsValid ? "autofocus" : null)"><span class="adm-step-count">Step @Model.CurrentStep of @OnboardingService.LastStep</span> @Model.StepLabel</h1>
```

- [ ] **Step 6: Replace `_WizardFooter.cshtml`**

Replace the whole file `src/Portfolio.Web/Pages/Admin/Welcome/_WizardFooter.cshtml` with:

```razor
@model WelcomePageModel
@* The card's bottom bar: Skip (or Leave) and Start over on the left, each its own form, and the step's Continue on the right. Continue is a submit button of the step's form
   (form="wizard-form"), so it can sit here, outside that form. A step with its own main button (the template step's Use, the review step's Publish) has no ContinueLabel. *@
<div class="adm-wizard-bar">
    <div class="adm-wizard-exit">
        <form method="post" asp-page-handler="Skip">
            <button type="submit" class="adm-btn adm-btn-quiet">@(Model.FirstRun ? "Skip setup" : "Leave without publishing")</button>
        </form>
        @if (Model.Draft is not null)
        {
            <form method="post" asp-page-handler="Discard">
                <button type="submit" class="adm-btn adm-btn-quiet">Start over</button>
            </form>
        }
    </div>
    @if (Model.ContinueLabel is { } label)
    {
        <button type="submit" form="wizard-form" name="goto" value="@Model.NextStep" class="adm-btn adm-btn-primary">@label</button>
    }
</div>
@if (Model.FirstRun)
{
    <p class="adm-hint">Skipping keeps the placeholder profile and demo content the site started with. You can run this wizard again from the dashboard.</p>
}
```

- [ ] **Step 7: Convert the Template step**

In `src/Portfolio.Web/Pages/Admin/Welcome/Template.cshtml.cs`, after `OnPostAsync` add:

```csharp
    /// <summary>The indicator's buttons on this step. Leaving step 1 keeps the template the draft has, or, with none, the one on screen (the site's, or the default on a first run): only Use changes it.</summary>
    public async Task<IActionResult> OnPostJumpAsync(CancellationToken ct)
    {
        await LoadAsync(ct);
        if (await Onboarding.KeepTemplateAsync(ct) is { } refused)
        {
            ModelState.AddModelError(nameof(Key), refused);
            BuildPicker(null);
            return Page();
        }
        return Leave();
    }
```

Replace the whole file `src/Portfolio.Web/Pages/Admin/Welcome/Template.cshtml` with:

```razor
@page "/admin/welcome"
@model WelcomeTemplateModel
@{
    ViewData["Title"] = "Template · Step 1 of 4";
}
<div class="adm-card adm-wizard">
    <partial name="_WizardHeader" model="Model" />
    <p class="adm-lede">A template sets the layout, the wording and which pages your site has. Pick the one closest to your profession. You can change it later at any time: nothing you add is ever deleted, sections a template does not use are only hidden.</p>

    @* The indicator's buttons submit this form. It has no fields: the picker's own button (Use or Keep) is what changes the template. *@
    <form id="wizard-form" method="post" asp-page-handler="Jump"></form>

    @if (!ViewData.ModelState.IsValid)
    {
        <div id="error-summary" class="adm-flash adm-flash-error" role="alert" tabindex="-1" autofocus>
            <strong>Choose a template to continue.</strong>
            <div asp-validation-summary="All"></div>
        </div>
    }
    <partial name="/Pages/Admin/_TemplatePicker.cshtml" model="Model.Picker" />
    <partial name="_WizardFooter" model="Model" />
</div>
```

- [ ] **Step 8: CSS**

In `src/Portfolio.Web/wwwroot/css/admin.css`, replace the rule on line 133 with (note `padding: 0`, the button carries it now):

old:
```css
.adm-steps li { flex: 1 1 8rem; padding: .5rem .75rem; border: 1px solid var(--adm-border); border-radius: .35rem; background: var(--adm-surface); color: var(--adm-muted); }
```
new:
```css
.adm-steps li { flex: 1 1 8rem; padding: 0; border: 1px solid var(--adm-border); border-radius: .35rem; background: var(--adm-surface); color: var(--adm-muted); }
```

Replace lines 135-136 (`.adm-steps a ...` and `.adm-steps a:hover ...`, which no longer match anything) with:

old:
```css
.adm-steps a { color: inherit; text-decoration: none; display: block; }
.adm-steps a:hover { text-decoration: underline; }
```
new:
```css
.adm-step-btn { display: block; width: 100%; padding: .5rem .75rem; border: 0; border-radius: .35rem; background: transparent; color: inherit; font: inherit; text-align: left; cursor: pointer; }
.adm-step-btn:hover { text-decoration: underline; }
.adm-step-btn:focus-visible { outline: 3px solid var(--adm-accent); outline-offset: 2px; }
.adm-step-state { display: inline-block; margin-left: 2rem; font-size: .75rem; font-weight: 500; color: var(--adm-muted); }
.adm-step-done .adm-step-state { color: var(--adm-ok-text); }
.adm-step-needsattention .adm-step-state { padding: 0 .4rem; border-radius: .25rem; background: var(--adm-warn-bg); color: var(--adm-warn-text); }
```

Append at the end of the file:

```css

/* Setup wizard: one card per step (PR C) */
.adm-wizard { padding: var(--adm-space-5); }
.adm-wizard .adm-picker { padding: 0; border: 0; box-shadow: none; background: transparent; }
.adm-draft-saved { margin: 0 0 var(--adm-space-3); font-size: .85rem; color: var(--adm-muted); }
.adm-wizard-bar { display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: var(--adm-space-3); margin-top: var(--adm-space-5); padding-top: var(--adm-space-4); border-top: 1px solid var(--adm-border); }
.adm-wizard-bar .adm-wizard-exit { margin-top: 0; }
```

(`--adm-space-3`, `-4` and `-5` already exist from PR A. The existing test `TheAdminStylesheet_HasVisibleFocus_...` still passes: no `outline: none` is added, and `.adm-steps li { flex-basis: 100%; }` in the narrow-screen block is untouched.)

- [ ] **Step 9: Update the two existing accessibility tests**

In `tests/Portfolio.Web.Tests/Admin/OnboardingAccessibilityAccentTests.cs`, in `EveryStep_HasAnOrderedProgressListWithOneCurrentStep_AMainLandmarkAndALiveRegion`, replace line 70:

old:
```csharp
            Assert.Matches(new Regex($@"<li aria-current=""step"">\s*<span class=""adm-step-num"">{current[i]}</span>"), list.Value);
```
new:
```csharp
            Assert.Matches(new Regex($@"<li aria-current=""step"" class=""adm-step adm-step-[a-z]+"">\s*<button type=""submit"" form=""wizard-form"" name=""goto"" value=""{current[i]}"" class=""adm-step-btn"">\s*<span class=""adm-step-num"">{current[i]}</span>"), list.Value);
```

Delete the whole test `TheProgressIndicator_OnlyLinksToStepsTheOwnerCanOpen` (the `[Fact]` line through its closing brace, about lines 157-173). Every step is clickable now; `WizardFrameTests` covers it. Also change the `<title>` assertion in the same first test only if it fails: titles now read "Basics · Step 2 of 4" and still contain `Step {n} of 4`, so it should pass as is.

- [ ] **Step 10: Run the tests**

Run: `dotnet test tests/Portfolio.Web.Tests --filter "FullyQualifiedName~WizardFrameTests|FullyQualifiedName~OnboardingAccessibilityAccentTests|FullyQualifiedName~TemplatePickerPageTests"`
Expected: PASS. `EveryPage_OffersAllFourSteps...` also loops over Basics, Photo and Review: it passes because the header is shared, even though those pages have no `wizard-form` yet (the test only checks the header markup).

If `TemplatePickerPageTests` or `OnboardingFlowTests` pin text from the old template-step markup (for example the old `Setup · Step 1 of 4 · Template` title), update the assertion to the new wording and note it in the commit message; the behaviour they guard is unchanged.

- [ ] **Step 11: Commit**

```bash
git add src/Portfolio.Web/Pages/Admin/Welcome/WelcomePageModel.cs src/Portfolio.Web/Pages/Admin/Welcome/_WizardHeader.cshtml src/Portfolio.Web/Pages/Admin/Welcome/_WizardFooter.cshtml src/Portfolio.Web/Pages/Admin/Welcome/Template.cshtml src/Portfolio.Web/Pages/Admin/Welcome/Template.cshtml.cs src/Portfolio.Web/wwwroot/css/admin.css tests/Portfolio.Web.Tests/Support/WizardFlow.cs tests/Portfolio.Web.Tests/Admin/WizardFrameTests.cs tests/Portfolio.Web.Tests/Admin/OnboardingAccessibilityAccentTests.cs
git commit -m "feat(admin): wizard card frame, clickable step indicator with status, template step jump

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 4: Basics step: partial save, hard rules, no gating (criteria 15, 16, 17)

**Files:**
- Modify: `src/Portfolio.Web/Services/OnboardingDraft.cs` (`BasicsForm` attributes)
- Modify: `src/Portfolio.Web/Pages/Admin/Welcome/Basics.cshtml`, `Basics.cshtml.cs`
- Modify: `tests/Portfolio.Web.Tests/Admin/OnboardingFlowTests.cs` (delete one test), `tests/Portfolio.Web.Tests/Admin/OnboardingAccessibilityAccentTests.cs` (one test)
- Test: `tests/Portfolio.Web.Tests/Services/BasicsRulesTests.cs`, `tests/Portfolio.Web.Tests/Admin/WizardBasicsJumpTests.cs`

**Interfaces:**
- Consumes: `WelcomePageModel.Goto`, `Leave()`, `NextStep`, `ContinueLabel`; `WizardFlow.JumpAsync`, `StepValues`.
- Produces: `BasicsForm` binds only the hard rules (`StringLength`, `NoMarkup`); `BasicsForm.IsComplete`/`Missing` (Task 1) remain the completeness check.

- [ ] **Step 1: Write the failing unit tests**

Create `tests/Portfolio.Web.Tests/Services/BasicsRulesTests.cs`:

```csharp
using System.ComponentModel.DataAnnotations;
using Portfolio.Data.Entities;
using Portfolio.Web.Services;
using Xunit;

namespace Portfolio.Web.Tests.Services;

/// <summary>The Basics step's two kinds of rule: hard rules on every save, completeness only at publishing.</summary>
public class BasicsRulesTests
{
    private static bool PassesHardRules(BasicsForm form) =>
        Validator.TryValidateObject(form, new ValidationContext(form), null, validateAllProperties: true);

    [Fact]
    public void AnEmptyForm_PassesTheHardRules_SoAnyStepCanBeSavedHalfDone() =>
        Assert.True(PassesHardRules(new BasicsForm()));

    [Fact]
    public void AnEmailThatIsNotAnAddress_PassesTheHardRules_ButIsNotComplete()
    {
        Assert.True(PassesHardRules(new BasicsForm { Email = "nope" }));
        Assert.False(BasicsForm.IsComplete(new OnboardingDraft { OwnerName = "J", JobTitle = "T", Email = "nope", Summary = "S" }));
    }

    [Theory]
    [InlineData("OwnerName")]
    [InlineData("JobTitle")]
    [InlineData("Location")]
    public void MarkupCharacters_BreakAHardRule(string field)
    {
        var form = new BasicsForm();
        typeof(BasicsForm).GetProperty(field)!.SetValue(form, "<b>x</b>");

        Assert.False(PassesHardRules(form));
    }

    [Fact]
    public void TextThatIsTooLong_BreaksAHardRule()
    {
        Assert.False(PassesHardRules(new BasicsForm { Summary = new string('a', BasicsForm.SummaryMax + 1) }));
        Assert.False(PassesHardRules(new BasicsForm { OwnerName = new string('a', SiteProfile.MaxLength.OwnerName + 1) }));
        Assert.False(PassesHardRules(new BasicsForm { Email = new string('a', 301) }));
        Assert.True(PassesHardRules(new BasicsForm { Summary = new string('a', BasicsForm.SummaryMax) }));
    }

    [Fact]
    public void ARulesBrokenInTheDraft_IsStillCaughtAtPublishing()
    {
        // A draft is data like any other: a tampered one with markup in the name is not complete, whatever the form allowed.
        Assert.False(BasicsForm.IsComplete(new OnboardingDraft { OwnerName = "<b>J</b>", JobTitle = "T", Email = "j@owner.example", Summary = "S" }));
        Assert.True(BasicsForm.IsComplete(new OnboardingDraft { OwnerName = "J", JobTitle = "T", Email = "j@owner.example", Summary = "S" }));
    }
}
```

- [ ] **Step 2: Write the failing integration tests**

Create `tests/Portfolio.Web.Tests/Admin/WizardBasicsJumpTests.cs`:

```csharp
using System.Net;
using System.Text.RegularExpressions;
using Portfolio.Web.Services;
using Portfolio.Web.Tests.Support;
using Xunit;

namespace Portfolio.Web.Tests.Admin;

/// <summary>The Basics step opens without a draft, saves what was typed (even half of it) when the owner jumps, and refuses only what breaks a hard rule.</summary>
public class WizardBasicsJumpTests
{
    [Fact]
    public async Task TheStep_OpensWithNoDraft_AndNoTemplateChosen()
    {
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();

        var response = await owner.GetAsync(WizardFlow.Basics);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(await WizardFlow.DraftOrNullAsync(app)); // reading saves nothing
    }

    [Fact]
    public async Task Jumping_SavesWhatWasTyped_IncludingIncompleteInput_AndGoesToTheRequestedStep()
    {
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();
        await WizardFlow.PickTemplateAsync(owner, "healthcare");

        var jump = await WizardFlow.JumpAsync(owner, WizardFlow.Basics, 4, new() { ["Form.OwnerName"] = "Part Person", ["Form.Email"] = "half@" });

        Assert.Equal("/admin/welcome/review", WizardFlow.Where(jump));
        var draft = await WizardFlow.DraftOrNullAsync(app);
        Assert.Equal("Part Person", draft!.OwnerName);
        Assert.Equal("half@", draft.Email); // not a valid address, and that is fine until publishing
        Assert.Null(draft.JobTitle);
        Assert.Equal("healthcare", draft.TemplateKey);
    }

    [Fact]
    public async Task Jumping_WithNoDraft_CreatesOne_AndSavesTheTypedFields()
    {
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();

        var jump = await WizardFlow.JumpAsync(owner, WizardFlow.Basics, 3, new() { ["Form.OwnerName"] = "First Typed" });

        Assert.Equal("/admin/welcome/photo", WizardFlow.Where(jump));
        Assert.Equal("First Typed", (await WizardFlow.DraftOrNullAsync(app))!.OwnerName);
    }

    [Fact]
    public async Task ContinueWithoutAGoto_GoesToThePhotoStep()
    {
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();

        var response = await WizardFlow.SaveBasicsAsync(owner);

        Assert.Equal("/admin/welcome/photo", WizardFlow.Where(response));
    }

    [Theory]
    [InlineData("99")]
    [InlineData("0")]
    [InlineData("abc")]
    [InlineData("")]
    public async Task AnUnknownGoto_FallsBackToThePhotoStep(string requested)
    {
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();
        var token = await AdminApp.GetTokenAsync(owner, WizardFlow.Basics);

        var response = await AdminApp.PostFormAsync(owner, WizardFlow.Basics, token, new Dictionary<string, string> { ["goto"] = requested, ["Form.OwnerName"] = "X" });

        Assert.Equal("/admin/welcome/photo", WizardFlow.Where(response));
    }

    [Fact]
    public async Task MarkupInAField_BlocksTheJump_ShowsTheError_AndSavesNothing()
    {
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();

        var response = await WizardFlow.JumpAsync(owner, WizardFlow.Basics, 4, new() { ["Form.OwnerName"] = "<b>Jordan</b>" });
        var html = await WizardFlow.BodyAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("id=\"error-summary\"", html);
        Assert.Contains("Use plain text only", html);
        Assert.Null(await WizardFlow.DraftOrNullAsync(app));
    }

    [Fact]
    public async Task ATooLongSummary_BlocksTheJump()
    {
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();

        var response = await WizardFlow.JumpAsync(owner, WizardFlow.Basics, 3, new() { ["Form.Summary"] = new string('a', BasicsForm.SummaryMax + 1) });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(await WizardFlow.DraftOrNullAsync(app));
    }

    [Fact]
    public async Task EmptyFields_AreSaved_AsEmpty_NotAsErrors()
    {
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();
        await WizardFlow.SaveBasicsAsync(owner, WizardFlow.BasicsFields(name: "Keep Me"));

        var jump = await WizardFlow.JumpAsync(owner, WizardFlow.Basics, 1, new() { ["Form.OwnerName"] = "", ["Form.Email"] = "" });

        Assert.Equal("/admin/welcome", WizardFlow.Where(jump));
        var draft = await WizardFlow.DraftOrNullAsync(app);
        Assert.Null(draft!.OwnerName);
        Assert.Null(draft.Email);
    }

    [Fact]
    public async Task TheStatus_ChangesFromToDo_ToNeedsAttention_ToDone_AsTheBasicsFillIn()
    {
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();
        Assert.Equal("To do", WizardFlow.StepStates(await owner.GetStringAsync(WizardFlow.Basics))[1]);

        await WizardFlow.JumpAsync(owner, WizardFlow.Basics, 2, new() { ["Form.OwnerName"] = "Jordan" });
        Assert.Equal("Needs attention", WizardFlow.StepStates(await owner.GetStringAsync(WizardFlow.Basics))[1]);

        await WizardFlow.SaveBasicsAsync(owner);
        Assert.Equal("Done", WizardFlow.StepStates(await owner.GetStringAsync(WizardFlow.Basics))[1]);
    }

    [Fact]
    public async Task EnterInATextField_SubmitsThroughTheHiddenDefaultButton_NotThroughStepOne()
    {
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();

        var html = await owner.GetStringAsync(WizardFlow.Basics);

        Assert.Equal("3", WizardFlow.StepValues(html)[0]);
        Assert.Equal("3", WizardFlow.StepValues(html)[^1]); // and the card's own Continue is the same button
    }

    [Fact]
    public async Task TheStep_IsOneCard_WithItsFormAndAContinueButton_AndNoBackButton()
    {
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();

        var html = await owner.GetStringAsync(WizardFlow.Basics);

        Assert.Single(Regex.Matches(html, @"class=""adm-card adm-wizard"""));
        Assert.Single(Regex.Matches(html, @"<form id=""wizard-form"""));
        Assert.Contains(@"name=""goto"" value=""3"" class=""adm-btn adm-btn-primary"">Continue</button>", html);
        Assert.DoesNotContain(">Back</a>", html);
        Assert.DoesNotContain("Continue to photo", html);
        Assert.Contains("Basics · Step 2 of 4", Regex.Match(html, "<title>(.*?)</title>").Groups[1].Value);
    }

    [Fact]
    public async Task TheFields_AreNotMarkedRequired_BecauseOnlyPublishingNeedsThem()
    {
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();

        var html = await owner.GetStringAsync(WizardFlow.Basics);

        foreach (var tag in WizardFlow.InputPattern.Matches(html).Select(m => m.Value).Where(t => t.Contains("Form_")))
            Assert.DoesNotContain(" required", tag);
        Assert.Contains("needed before you can publish", html);
    }

    [Fact]
    public async Task AFieldThatBreaksARule_KeepsWhatTheOwnerTyped_OnTheRedisplayedPage()
    {
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();

        var html = await WizardFlow.BodyAsync(await WizardFlow.JumpAsync(owner, WizardFlow.Basics, 4,
            new() { ["Form.OwnerName"] = "<b>x</b>", ["Form.Location"] = "Bristol" }));

        Assert.Contains("value=\"Bristol\"", html);
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/Portfolio.Web.Tests --filter "FullyQualifiedName~BasicsRulesTests|FullyQualifiedName~WizardBasicsJumpTests"`
Expected: FAIL (the empty `BasicsForm` fails its `Required` rules; the page still redirects to step 1 without a draft).

- [ ] **Step 4: Reduce `BasicsForm` to the hard rules**

In `src/Portfolio.Web/Services/OnboardingDraft.cs`, replace the doc comment and the five properties of `BasicsForm` (from `/// <summary>The wizard's Basics step. Lengths come from...` through the `Summary` property) with:

```csharp
/// <summary>
/// The wizard's Basics step as typed. Only the hard rules are bound here (length limits and no markup characters), so any half-finished step can be saved.
/// Whether the answers are complete is a separate question asked at publishing: <see cref="Missing"/> and <see cref="IsComplete"/>.
/// Lengths come from the profile columns, so a value that passes here always fits.
/// </summary>
public class BasicsForm
{
    public const int SummaryMax = 600;

    [StringLength(SiteProfile.MaxLength.OwnerName), NoMarkup, Display(Name = "Name")]
    public string? OwnerName { get; set; }

    [StringLength(SiteProfile.MaxLength.JobTitle), NoMarkup, Display(Name = "Professional title")]
    public string? JobTitle { get; set; }

    [StringLength(300), Display(Name = "Email")]
    public string? Email { get; set; }

    [StringLength(SiteProfile.MaxLength.Location), NoMarkup, Display(Name = "Location")]
    public string? Location { get; set; }

    [StringLength(SummaryMax), Display(Name = "Short summary")]
    public string? Summary { get; set; }
```

(`Cleaned()` and the Task 1 members below it stay as they are.)

- [ ] **Step 5: Rewrite the Basics page model**

Replace the body of `WelcomeBasicsModel` in `src/Portfolio.Web/Pages/Admin/Welcome/Basics.cshtml.cs` (from `public override int CurrentStep => 2;` to the end of the class) with:

```csharp
    public override int CurrentStep => 2;

    public override string? ContinueLabel => "Continue";

    [BindProperty]
    public BasicsForm Form { get; set; } = new();

    public TemplateHeroSuggestions Suggestions => Template.HeroSuggestions;

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        await LoadAsync(ct);

        // What the owner typed wins; then, for a site that is already set up, what it says now; the template's suggestion fills what is still empty.
        var profile = State.Profile;
        var existing = !FirstRun;
        var draft = Draft ?? new OnboardingDraft();
        Form = new BasicsForm
        {
            OwnerName = draft.OwnerName ?? (existing ? profile.OwnerName : null),
            JobTitle = draft.JobTitle ?? (existing && !string.IsNullOrWhiteSpace(profile.JobTitle) ? profile.JobTitle : Suggestions.Title),
            Email = draft.Email ?? (existing ? profile.Email : null),
            Location = draft.Location ?? (existing ? profile.Location : null),
            Summary = draft.Summary ?? (existing && !string.IsNullOrWhiteSpace(profile.Summary) ? profile.Summary : Suggestions.Summary),
        };
        return Page();
    }

    /// <summary>Saves whatever was typed, complete or not, then goes where <see cref="WelcomePageModel.Goto"/> says (the photo step by default). Only a hard rule (length, plain text) keeps the owner here.</summary>
    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        await LoadAsync(ct);
        if (!ModelState.IsValid) return Page();

        await Onboarding.SaveBasicsAsync(Form, ct);
        return Leave();
    }
}
```

- [ ] **Step 6: Replace `Basics.cshtml`**

Replace the whole file `src/Portfolio.Web/Pages/Admin/Welcome/Basics.cshtml` with:

```razor
@page "/admin/welcome/basics"
@model WelcomeBasicsModel
@{
    ViewData["Title"] = "Basics · Step 2 of 4";
    ViewData["Crumb"] = "Step 2: Basics";
    bool Bad(string field) => ViewData.ModelState.TryGetValue("Form." + field, out var entry) && entry.Errors.Count > 0;
}
<div class="adm-card adm-wizard">
    <partial name="_WizardHeader" model="Model" />
    <p class="adm-lede">The essentials for your header, home page, contact page and search results. Your title and summary start from a suggestion for the @Model.Template.Name template: keep them or write your own. Your name, title, email and short summary are needed before you can publish, but you can leave any of them for now and come back.</p>

    <form id="wizard-form" method="post" asp-page="/Admin/Welcome/Basics" novalidate>
        @if (!ViewData.ModelState.IsValid)
        {
            <div id="error-summary" class="adm-flash adm-flash-error" role="alert" tabindex="-1" autofocus>
                <strong>Some fields need attention.</strong>
                <div asp-validation-summary="All"></div>
            </div>
        }
        <fieldset class="adm-fieldset">
            <legend>About you</legend>
            <div class="adm-field">
                <label for="Form_OwnerName">Name</label>
                <input asp-for="Form.OwnerName" type="text" maxlength="200" autocomplete="name" aria-describedby="name-hint Form_OwnerName-error" aria-invalid="@(Bad("OwnerName") ? "true" : null)" />
                <span id="name-hint" class="adm-hint">As you want it shown on your site, for example Jordan Alexander.</span>
                <span id="Form_OwnerName-error" asp-validation-for="Form.OwnerName"></span>
            </div>
            <div class="adm-field">
                <label for="Form_JobTitle">Professional title</label>
                <input asp-for="Form.JobTitle" type="text" maxlength="200" autocomplete="organization-title" aria-describedby="title-hint Form_JobTitle-error" aria-invalid="@(Bad("JobTitle") ? "true" : null)" />
                <span id="title-hint" class="adm-hint">@(Model.Form.JobTitle == Model.Suggestions.Title ? "Suggested by the template; change it to match you." : "The template suggests: " + Model.Suggestions.Title)</span>
                <span id="Form_JobTitle-error" asp-validation-for="Form.JobTitle"></span>
            </div>
            <div class="adm-field">
                <label for="Form_Email">Email</label>
                <input asp-for="Form.Email" type="email" maxlength="300" autocomplete="email" inputmode="email" aria-describedby="email-hint Form_Email-error" aria-invalid="@(Bad("Email") ? "true" : null)" />
                <span id="email-hint" class="adm-hint">Shown on your Contact page so people can reach you.</span>
                <span id="Form_Email-error" asp-validation-for="Form.Email"></span>
            </div>
            <div class="adm-field">
                <label for="Form_Location">Location <span class="adm-optional">(optional)</span></label>
                <input asp-for="Form.Location" type="text" maxlength="120" autocomplete="address-level2" aria-describedby="location-hint Form_Location-error" aria-invalid="@(Bad("Location") ? "true" : null)" />
                <span id="location-hint" class="adm-hint">A city or region, for example Leeds, UK.</span>
                <span id="Form_Location-error" asp-validation-for="Form.Location"></span>
            </div>
            <div class="adm-field">
                <label for="Form_Summary">Short summary</label>
                <textarea asp-for="Form.Summary" rows="5" maxlength="600" aria-describedby="summary-hint Form_Summary-error" aria-invalid="@(Bad("Summary") ? "true" : null)"></textarea>
                <span id="summary-hint" class="adm-hint">Up to 600 characters. It leads your home page and is used in search results. @(Model.Form.Summary == Model.Suggestions.Summary ? "This is the template's suggestion: it is published as written, so make it yours." : "")</span>
                <span id="Form_Summary-error" asp-validation-for="Form.Summary"></span>
            </div>
        </fieldset>
    </form>
    <partial name="_WizardFooter" model="Model" />
</div>
```

- [ ] **Step 7: Update the existing tests this task changes**

In `tests/Portfolio.Web.Tests/Admin/OnboardingFlowTests.cs`, delete the whole test `AStepWithoutTheOnesBeforeIt_SendsTheOwnerBack` (the `[Fact]` line and the method, lines 203-216). Its rule is gone by design (spec 6.3: every step opens from every step); `WizardBasicsJumpTests` and, in Tasks 5 and 6, the Photo and Review tests cover what replaces it.

In `tests/Portfolio.Web.Tests/Admin/OnboardingAccessibilityAccentTests.cs`, replace the body of `OnAnError_TheAlertTakesFocusInsteadOfTheHeading_AndTheFieldIsMarkedInvalid` (an invalid email is no longer an error on save; it is checked at publishing) with a hard-rule error:

old:
```csharp
        var html = await (await WizardFlow.SaveBasicsAsync(owner, WizardFlow.BasicsFields(email: "nope"))).Content.ReadAsStringAsync();

        Assert.Matches(new Regex(@"<div id=""error-summary""[^>]*role=""alert""[^>]*tabindex=""-1""[^>]*autofocus"), html);
        Assert.Matches(new Regex(@"<h1 id=""step-heading"" tabindex=""-1"">"), html);
        Assert.Single(Regex.Matches(html, @"\sautofocus\b"));
        var email = Tags(html, "input").Single(t => Attr(t, "id") == "Form_Email");
        Assert.Equal("true", Attr(email, "aria-invalid"));
        Assert.Contains("Enter a valid email address.", html);
        Assert.Matches(new Regex(@"aria-describedby=""email-hint Form_Email-error"""), html);
```
new:
```csharp
        var html = await (await WizardFlow.SaveBasicsAsync(owner, WizardFlow.BasicsFields(name: "<b>Jordan</b>"))).Content.ReadAsStringAsync();

        Assert.Matches(new Regex(@"<div id=""error-summary""[^>]*role=""alert""[^>]*tabindex=""-1""[^>]*autofocus"), html);
        Assert.Matches(new Regex(@"<h1 id=""step-heading"" tabindex=""-1"">"), html);
        Assert.Single(Regex.Matches(html, @"\sautofocus\b"));
        var name = Tags(html, "input").Single(t => Attr(t, "id") == "Form_OwnerName");
        Assert.Equal("true", Attr(name, "aria-invalid"));
        Assert.Contains("Use plain text only", html);
        Assert.Matches(new Regex(@"aria-describedby=""name-hint Form_OwnerName-error"""), html);
```

- [ ] **Step 8: Run the tests**

Run: `dotnet test tests/Portfolio.Web.Tests --filter "FullyQualifiedName~BasicsRulesTests|FullyQualifiedName~WizardBasicsJumpTests|FullyQualifiedName~OnboardingFlowTests|FullyQualifiedName~OnboardingAccessibilityAccentTests|FullyQualifiedName~OnboardingSecurityTests"`
Expected: the new tests PASS. Any other failure is a test that pinned old Basics behaviour (an error for an empty field, the old button text, the old title). For each one decide: if it guarded an intent that still holds (for example "the field is validated"), rewrite it against the hard rule; if it only pinned the removed rule, delete it. Do not loosen an assertion to make it pass. Record each change in the commit message.

- [ ] **Step 9: Commit**

```bash
git add src/Portfolio.Web/Services/OnboardingDraft.cs src/Portfolio.Web/Pages/Admin/Welcome/Basics.cshtml src/Portfolio.Web/Pages/Admin/Welcome/Basics.cshtml.cs tests/Portfolio.Web.Tests
git commit -m "feat(admin): basics step saves partial input under the hard rules and opens without a draft

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 5: Photo step: one card, upload on jump (criteria 15, 16, 17)

**Files:**
- Modify: `src/Portfolio.Web/Pages/Admin/Welcome/Photo.cshtml`, `Photo.cshtml.cs`
- Modify: `src/Portfolio.Web/wwwroot/css/admin.css` (append)
- Modify: `tests/Portfolio.Web.Tests/Support/WizardFlow.cs`
- Test: `tests/Portfolio.Web.Tests/Admin/WizardPhotoJumpTests.cs`

**Interfaces:**
- Consumes: `Leave()`, `ContinueLabel`; `OnboardingService.SaveDraftPhotoAsync`.
- Produces: `WizardFlow.JumpFromPhotoAsync(HttpClient client, int to, byte[]? file = null, string fileName = "me.jpg")`. The handlers `Upload`, `Remove`, `Skip` and `Discard` keep their names and behaviour (the security tests post to them); the new default POST (`OnPostAsync`) is the jump.

- [ ] **Step 1: Add the helper**

In `tests/Portfolio.Web.Tests/Support/WizardFlow.cs`, after `UploadPhotoAsync`:

```csharp
    /// <summary>Posts the photo step's form the way the indicator does: no handler, a <c>goto</c> and, optionally, a chosen file.</summary>
    public static async Task<HttpResponseMessage> JumpFromPhotoAsync(HttpClient client, int to, byte[]? file = null, string fileName = "me.jpg")
    {
        var token = await AdminApp.GetTokenAsync(client, Photo);
        var content = new MultipartFormDataContent { { new StringContent(token), "__RequestVerificationToken" }, { new StringContent(to.ToString()), "goto" } };
        if (file is not null)
        {
            var part = new ByteArrayContent(file);
            part.Headers.ContentType = MediaTypeHeaderValue.Parse("image/jpeg");
            content.Add(part, "Photo", fileName);
        }
        return await client.PostAsync(Photo, content);
    }
```

- [ ] **Step 2: Write the failing tests**

Create `tests/Portfolio.Web.Tests/Admin/WizardPhotoJumpTests.cs`:

```csharp
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using SkiaSharp;
using Portfolio.Web.Tests.Support;
using Xunit;

namespace Portfolio.Web.Tests.Admin;

/// <summary>The Photo step opens without a draft, and a file that was chosen but not uploaded is uploaded as part of the jump.</summary>
public class WizardPhotoJumpTests
{
    private static byte[] Jpeg() => TestImages.Split(SKEncodedImageFormat.Jpeg, 64, 64);

    [Fact]
    public async Task TheStep_OpensWithNoDraft()
    {
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();

        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync(WizardFlow.Photo)).StatusCode);
        Assert.Null(await WizardFlow.DraftOrNullAsync(app));
    }

    [Fact]
    public async Task Jumping_WithAChosenFile_UploadsItFirst_ThenGoesWhereAsked()
    {
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();

        var jump = await WizardFlow.JumpFromPhotoAsync(owner, 4, Jpeg());

        Assert.Equal("/admin/welcome/review", WizardFlow.Where(jump));
        var draft = await WizardFlow.DraftOrNullAsync(app);
        Assert.NotNull(draft!.PhotoDraft);
        Assert.Equal("Done", WizardFlow.StepStates(await owner.GetStringAsync(WizardFlow.Photo))[2]);
    }

    [Fact]
    public async Task Jumping_WithNoFile_JustMovesOn_AndAddsNoPhoto()
    {
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();

        var jump = await WizardFlow.JumpFromPhotoAsync(owner, 2);

        Assert.Equal("/admin/welcome/basics", WizardFlow.Where(jump));
        Assert.Null((await WizardFlow.DraftOrNullAsync(app))?.PhotoDraft);
    }

    [Fact]
    public async Task AFileThatIsNotAnImage_KeepsTheOwnerOnTheStep_WithTheError_AndSavesNothing()
    {
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();

        var jump = await WizardFlow.JumpFromPhotoAsync(owner, 4, Encoding.UTF8.GetBytes("this is not an image"));
        var html = await WizardFlow.BodyAsync(jump);

        Assert.Equal(HttpStatusCode.OK, jump.StatusCode);
        Assert.Contains("id=\"error-summary\"", html);
        Assert.Null((await WizardFlow.DraftOrNullAsync(app))?.PhotoDraft);
    }

    [Fact]
    public async Task ContinueWithNoGoto_GoesToTheReviewStep()
    {
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();
        var token = await AdminApp.GetTokenAsync(owner, WizardFlow.Photo);
        var content = new MultipartFormDataContent { { new StringContent(token), "__RequestVerificationToken" } };

        var response = await owner.PostAsync(WizardFlow.Photo, content);

        Assert.Equal("/admin/welcome/review", WizardFlow.Where(response));
    }

    [Fact]
    public async Task TheUploadHandler_StillAddsThePhoto_AndStaysOnTheStep()
    {
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();

        var upload = await WizardFlow.UploadPhotoAsync(owner, Jpeg());

        Assert.Equal("/admin/welcome/photo", WizardFlow.Where(upload));
        Assert.NotNull((await WizardFlow.DraftOrNullAsync(app))!.PhotoDraft);
    }

    [Fact]
    public async Task TheStep_IsOneCard_WithTheCurrentPhotoBesideTheUpload_AndNoBackButton()
    {
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();

        var html = await owner.GetStringAsync(WizardFlow.Photo);

        Assert.Single(Regex.Matches(html, @"class=""adm-card adm-wizard"""));
        Assert.Contains("class=\"adm-photo-step\"", html);
        Assert.Single(Regex.Matches(html, @"<form id=""wizard-form"""));
        Assert.Contains("enctype=\"multipart/form-data\"", html);
        Assert.Contains("name=\"goto\" value=\"4\" class=\"adm-btn adm-btn-primary\">Continue</button>", html);
        Assert.DoesNotContain(">Back</a>", html);
        Assert.DoesNotContain("Skip photo and continue", html);
        Assert.DoesNotContain(" required", WizardFlow.InputPattern.Matches(html).Select(m => m.Value).Single(t => t.Contains("type=\"file\"")));
        Assert.Contains("Photo · Step 3 of 4", Regex.Match(html, "<title>(.*?)</title>").Groups[1].Value);
    }

    [Fact]
    public async Task TheRemoveButton_IsItsOwnForm_NotNestedInTheStepsForm()
    {
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();
        await WizardFlow.UploadPhotoAsync(owner, Jpeg());

        var html = await owner.GetStringAsync(WizardFlow.Photo);

        var wizardForm = Regex.Match(html, @"<form id=""wizard-form"".*?</form>", RegexOptions.Singleline).Value;
        Assert.DoesNotContain("Remove photo", wizardForm);
        Assert.Contains(">Remove photo</button>", html);
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/Portfolio.Web.Tests --filter "FullyQualifiedName~WizardPhotoJumpTests"`
Expected: FAIL (the step still redirects to Basics without a draft, and has no default POST handler).

- [ ] **Step 4: Rewrite the Photo page model**

Replace everything in `src/Portfolio.Web/Pages/Admin/Welcome/Photo.cshtml.cs` from `public override int CurrentStep => 3;` to the end of the file with:

```csharp
    public override int CurrentStep => 3;

    public override string? ContinueLabel => "Continue";

    [BindProperty]
    public IFormFile? Photo { get; set; }

    public string MaxSize => UploadForm.Label(_store.MaxBytes);

    public bool HasDraftPhoto => Draft?.PhotoDraft is not null && _store.TryResolveDraft(Draft.PhotoDraft, out _, out _);

    /// <summary>Changes whenever the draft photo does, so the browser never shows a replaced picture from its cache.</summary>
    public long PhotoVersion => Draft?.UpdatedAt.UtcTicks ?? 0;

    /// <summary>What the monogram fallback shows, from the name entered so far.</summary>
    public string Initials => new SiteOptions { OwnerName = Draft?.OwnerName ?? "" }.Initials;

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        await LoadAsync(ct);
        return Page();
    }

    /// <summary>Continue, or one of the indicator's buttons. A file that was chosen but not uploaded is uploaded first (a file that is not a valid image keeps the owner here with the reason);
    /// with no file chosen the owner simply moves on. The photo is optional.</summary>
    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        await LoadAsync(ct);
        if (Photo is { Length: > 0 })
        {
            if (await TryUploadAsync(ct) is { } error) return Reject(error);
            Flash("Photo added to your draft. It is not public until you publish.");
        }
        return Leave();
    }

    /// <summary>The Upload button: adds the chosen photo and stays on this step.</summary>
    public async Task<IActionResult> OnPostUploadAsync(CancellationToken ct)
    {
        await LoadAsync(ct);
        if (Photo is null || Photo.Length == 0)
            return Reject("Choose an image to upload.");
        if (await TryUploadAsync(ct) is { } error)
            return Reject(error);

        Flash("Photo added to your draft. It is not public until you publish.");
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRemoveAsync(CancellationToken ct)
    {
        await LoadAsync(ct);
        await Onboarding.RemoveDraftPhotoAsync(ct);
        Flash("Photo removed from your draft. Your site will use a monogram of your initials.");
        return RedirectToPage();
    }

    /// <summary>Stores the chosen photo in the hidden draft area. Null when it was stored; otherwise why it was not (too large, or not an image the processor accepts).</summary>
    private async Task<string?> TryUploadAsync(CancellationToken ct)
    {
        if (Photo!.Length > _store.MaxBytes) return $"That file is larger than {MaxSize}.";
        try
        {
            await Onboarding.SaveDraftPhotoAsync(await UploadForm.ReadAsync(Photo, _store.MaxBytes, MaxSize, ct), ct);
            return null;
        }
        catch (ImageRejectedException ex)
        {
            return ex.Message;
        }
    }

    private IActionResult Reject(string message)
    {
        ModelState.AddModelError(nameof(Photo), message);
        return Page();
    }
}
```

- [ ] **Step 5: Replace `Photo.cshtml`**

Replace the whole file `src/Portfolio.Web/Pages/Admin/Welcome/Photo.cshtml` with:

```razor
@page "/admin/welcome/photo"
@model WelcomePhotoModel
@{
    ViewData["Title"] = "Photo · Step 3 of 4";
    ViewData["Crumb"] = "Step 3: Photo";
}
<div class="adm-card adm-wizard">
    <partial name="_WizardHeader" model="Model" />
    <p class="adm-lede">Optional. A photo is shown on your home and About pages. Without one, your site shows a monogram of your initials. The photo stays private to you until you publish.</p>

    <div class="adm-photo-step">
        <section aria-labelledby="photo-current">
            <h2 id="photo-current">Your photo</h2>
            @if (Model.HasDraftPhoto)
            {
                <img class="adm-photo" src="/admin/welcome/draft-photo?v=@Model.PhotoVersion" alt="Your draft profile photo" width="160" height="160" />
                <form method="post" asp-page="/Admin/Welcome/Photo" asp-page-handler="Remove">
                    <button type="submit" class="adm-btn adm-btn-danger">Remove photo</button>
                </form>
            }
            else
            {
                <div class="adm-monogram" role="img" aria-label="Monogram of your initials, @Model.Initials">@Model.Initials</div>
                <p class="adm-hint">No photo yet. This monogram is what visitors see without one.</p>
            }
        </section>

        <section aria-labelledby="photo-upload">
            <h2 id="photo-upload">@(Model.HasDraftPhoto ? "Choose a different photo" : "Upload a photo")</h2>
            <form id="wizard-form" method="post" asp-page="/Admin/Welcome/Photo" enctype="multipart/form-data" novalidate>
                @if (!ViewData.ModelState.IsValid)
                {
                    <div id="error-summary" class="adm-flash adm-flash-error" role="alert" tabindex="-1" autofocus>
                        <div asp-validation-summary="All"></div>
                    </div>
                }
                <div class="adm-field">
                    <label for="Photo">Image file</label>
                    <input asp-for="Photo" type="file" accept="image/jpeg,image/png,image/webp" aria-describedby="photo-hint" />
                    <span id="photo-hint" class="adm-hint">JPEG, PNG or WebP, up to @Model.MaxSize. It is re-encoded on upload (location and camera details are removed) and shown as a circle centred on the picture, so choose one with your face in the middle. There is no cropping step. A file you choose is also uploaded when you press Continue or another step.</span>
                </div>
                <button type="submit" class="adm-btn" asp-page-handler="Upload">@(Model.HasDraftPhoto ? "Replace photo" : "Upload photo")</button>
            </form>
        </section>
    </div>
    <partial name="_WizardFooter" model="Model" />
</div>
```

- [ ] **Step 6: CSS**

Append to `src/Portfolio.Web/wwwroot/css/admin.css`:

```css
.adm-photo-step { display: grid; grid-template-columns: minmax(0, 12rem) minmax(0, 1fr); gap: var(--adm-space-5); align-items: start; }
.adm-photo-step h2 { margin-top: 0; }
@media (max-width: 40rem) { .adm-photo-step { grid-template-columns: 1fr; } }
```

- [ ] **Step 7: Run the tests**

Run: `dotnet test tests/Portfolio.Web.Tests --filter "FullyQualifiedName~WizardPhotoJumpTests|FullyQualifiedName~OnboardingPhotoPreviewTests|FullyQualifiedName~OnboardingSecurityTests|FullyQualifiedName~PhotoUploadTests"`
Expected: PASS. A failure in `OnboardingPhotoPreviewTests` or `OnboardingSecurityTests` that mentions a redirect to `/admin/welcome/basics` is a test that relied on the removed gating; rewrite it to post the fields it needs instead, and say so in the commit message.

- [ ] **Step 8: Commit**

```bash
git add src/Portfolio.Web/Pages/Admin/Welcome/Photo.cshtml src/Portfolio.Web/Pages/Admin/Welcome/Photo.cshtml.cs src/Portfolio.Web/wwwroot/css/admin.css tests/Portfolio.Web.Tests
git commit -m "feat(admin): photo step in one card, uploading a chosen file when the owner moves on

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 6: Review step: checklist, split layout, disabled Publish; remove the gating (criteria 15, 16, 17, 19, 20)

**Files:**
- Modify: `src/Portfolio.Web/Services/OnboardingDraft.cs` (`ReviewForm`)
- Replace: `src/Portfolio.Web/Pages/Admin/Welcome/Review.cshtml`, `Review.cshtml.cs`
- Modify: `src/Portfolio.Web/Pages/Admin/Welcome/WelcomePageModel.cs` (remove `RequireStep`, `Reachable`)
- Modify: `src/Portfolio.Web/wwwroot/css/admin.css` (append)
- Test: `tests/Portfolio.Web.Tests/Admin/WizardReviewTests.cs`

**Interfaces:**
- Consumes: `OnboardingService.ChecklistFor`, `TemplatePickerBuilder.PagesOf`, `OnboardingDraft.Clone`, `ProfileForm.Line`, `Leave()`.
- Produces: `WelcomeReviewModel.Checklist : PublishChecklist`, `ShowSearch : bool`, `ActiveTab : string`, `const string SearchTab = "search"`. Handlers: `OnPostAsync` (jump), `OnPostSaveAsync`, `OnPostPublishAsync`. `ReviewForm.SiteUrl` keeps only `StringLength`.

- [ ] **Step 1: Write the failing tests**

Create `tests/Portfolio.Web.Tests/Admin/WizardReviewTests.cs`:

```csharp
using System.Net;
using System.Text.RegularExpressions;
using Portfolio.Web.Services;
using Portfolio.Web.Tests.Support;
using Xunit;

namespace Portfolio.Web.Tests.Admin;

/// <summary>The Review step: a checklist beside the preview, Publish disabled with its reason until the checklist is complete, and a server that refuses an incomplete publish whatever the button says.</summary>
public class WizardReviewTests
{
    private static readonly Regex PublishButton = new(@"<button[^>]*>Publish my site</button>", RegexOptions.Compiled);

    private static async Task ReadyAsync(AdminApp app, HttpClient owner, string template = "software")
    {
        await WizardFlow.PickTemplateAsync(owner, template);
        await WizardFlow.SaveBasicsAsync(owner);
        await WizardFlow.SaveChoicesAsync(owner);
    }

    [Fact]
    public async Task TheStep_OpensWithNoDraft_AndSaysWhatIsStillToDo()
    {
        using var app = new AdminApp();
        var owner = await WizardFlow.FirstRunClientAsync(app);

        var response = await owner.GetAsync(WizardFlow.Review);
        var html = await WizardFlow.BodyAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Ready to publish?", html);
        Assert.Contains("Still to do: template, basics.", html); // the address is prefilled from the request, so it counts
        Assert.Null(await WizardFlow.DraftOrNullAsync(app));
    }

    [Fact]
    public async Task Publish_IsDisabled_WithItsReasonBesideIt_UntilTheChecklistIsComplete()
    {
        using var app = new AdminApp();
        var owner = await WizardFlow.FirstRunClientAsync(app);
        await WizardFlow.PickTemplateAsync(owner, "software");

        var html = await owner.GetStringAsync(WizardFlow.Review);

        var button = PublishButton.Match(html).Value;
        Assert.Contains("disabled", button);
        Assert.Contains("aria-describedby=\"publish-reason\"", button);
        Assert.Matches(@"<span id=""publish-reason"" class=""adm-hint"">Still to do: basics\.</span>", html);
    }

    [Fact]
    public async Task Publish_IsEnabled_OnceTheTemplate_TheBasicsAndTheAddressAreIn()
    {
        using var app = new AdminApp();
        var owner = await WizardFlow.FirstRunClientAsync(app);
        await ReadyAsync(app, owner);

        var html = await owner.GetStringAsync(WizardFlow.Review);

        Assert.DoesNotContain("disabled", PublishButton.Match(html).Value);
        Assert.DoesNotContain("publish-reason", html);
    }

    [Fact]
    public async Task TheChecklist_ListsFourLines_EachLinkingToTheStepThatFixesIt_AndThePhotoIsOptional()
    {
        using var app = new AdminApp();
        var owner = await WizardFlow.FirstRunClientAsync(app);

        var html = await owner.GetStringAsync(WizardFlow.Review);
        var list = Regex.Match(html, @"<ul class=""adm-readiness"">.*?</ul>", RegexOptions.Singleline).Value;

        Assert.Equal(4, Regex.Matches(list, "<li\\b").Count);
        foreach (var path in new[] { "/admin/welcome", "/admin/welcome/basics", "/admin/welcome/photo", "/admin/welcome/review" })
            Assert.Contains($"href=\"{path}\"", list);
        Assert.Contains(">Optional</span>", list);
        Assert.Contains("To do", list);
    }

    [Fact]
    public async Task AnIncompletePublish_IsRefusedByTheServer_EvenWhenTheButtonIsBypassed_AndReturnsToReview()
    {
        using var app = new AdminApp();
        var owner = await WizardFlow.FirstRunClientAsync(app);
        await WizardFlow.PickTemplateAsync(owner, "software"); // no basics
        var before = WizardFlow.PublicFingerprint(await WizardFlow.ProfileAsync(app));

        var publish = await WizardFlow.PublishAsync(owner);

        Assert.Equal(HttpStatusCode.Redirect, publish.StatusCode);
        Assert.Equal("/admin/welcome/review", WizardFlow.Where(publish));
        var profile = await WizardFlow.ProfileAsync(app);
        Assert.Null(profile.OnboardingCompletedAt);
        Assert.Equal(before, WizardFlow.PublicFingerprint(profile));
        Assert.NotNull(await WizardFlow.DraftOrNullAsync(app)); // the draft is kept
        Assert.Contains("Still to do: basics.", await owner.GetStringAsync(WizardFlow.Review));
    }

    [Fact]
    public async Task AnInvalidEmail_IsSaved_ButRefusedAtPublishing()
    {
        using var app = new AdminApp();
        var owner = await WizardFlow.FirstRunClientAsync(app);
        await WizardFlow.PickTemplateAsync(owner, "software");
        await WizardFlow.SaveBasicsAsync(owner, WizardFlow.BasicsFields(email: "nope"));

        var publish = await WizardFlow.PublishAsync(owner);

        Assert.Equal("/admin/welcome/review", WizardFlow.Where(publish));
        Assert.Null((await WizardFlow.ProfileAsync(app)).OnboardingCompletedAt);
        Assert.Contains("Still needed: email.", await owner.GetStringAsync(WizardFlow.Review));
    }

    [Fact]
    public async Task AnAddressThatIsNotAnAddress_IsSaved_ButRefusedAtPublishing()
    {
        using var app = new AdminApp();
        var owner = await WizardFlow.FirstRunClientAsync(app);
        await WizardFlow.PickTemplateAsync(owner, "software");
        await WizardFlow.SaveBasicsAsync(owner);

        var publish = await WizardFlow.PublishAsync(owner, WizardFlow.ReviewFields("jordan.example"));

        Assert.Equal("/admin/welcome/review", WizardFlow.Where(publish));
        Assert.Equal("jordan.example", (await WizardFlow.DraftOrNullAsync(app))!.SiteUrl);
        Assert.Null((await WizardFlow.ProfileAsync(app)).OnboardingCompletedAt);
    }

    [Fact]
    public async Task ACompletePublish_StillWorks_FromAFirstRun()
    {
        using var app = new AdminApp();
        var owner = await WizardFlow.FirstRunClientAsync(app);
        await ReadyAsync(app, owner, "healthcare");

        var publish = await WizardFlow.PublishAsync(owner, WizardFlow.ReviewFields("https://jordan.example"));

        Assert.Equal("/admin", WizardFlow.Where(publish));
        var profile = await WizardFlow.ProfileAsync(app);
        Assert.NotNull(profile.OnboardingCompletedAt);
        Assert.Equal("healthcare", profile.TemplateKey);
    }

    [Fact]
    public async Task Jumping_SavesTheChoicesUnderTheHardRulesOnly()
    {
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();

        var jump = await WizardFlow.JumpAsync(owner, WizardFlow.Review, 2, WizardFlow.ReviewFields("not a url"));

        Assert.Equal("/admin/welcome/basics", WizardFlow.Where(jump));
        Assert.Equal("not a url", (await WizardFlow.DraftOrNullAsync(app))!.SiteUrl);
    }

    [Fact]
    public async Task ATooLongAddress_BlocksTheJump()
    {
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();

        var jump = await WizardFlow.JumpAsync(owner, WizardFlow.Review, 2, WizardFlow.ReviewFields("https://" + new string('a', 400) + ".example"));

        Assert.Equal(HttpStatusCode.OK, jump.StatusCode);
        Assert.Contains("id=\"error-summary\"", await WizardFlow.BodyAsync(jump));
        Assert.Null(await WizardFlow.DraftOrNullAsync(app));
    }

    [Fact]
    public async Task AnAccentTheTemplateDoesNotOffer_IsStillRefused_OnAJump()
    {
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();

        var jump = await WizardFlow.JumpAsync(owner, WizardFlow.Review, 2, WizardFlow.ReviewFields(accent: "no-such-accent"));

        Assert.Equal(HttpStatusCode.OK, jump.StatusCode);
        Assert.Contains("That choice is not available for this template.", await WizardFlow.BodyAsync(jump));
    }

    [Fact]
    public async Task TheSearchTab_ShowsTheSearchResultCard_InPlaceOfThePreviewFrame()
    {
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();
        await ReadyAsync(app, owner);

        var page = await owner.GetStringAsync(WizardFlow.Review);
        var search = await owner.GetStringAsync(WizardFlow.Review + "?tab=search");

        Assert.Contains("<iframe", page);
        Assert.DoesNotContain("adm-serp-title", page);
        Assert.DoesNotContain("<iframe", search);
        Assert.Contains("adm-serp-title", search);
        Assert.Contains("How it may appear in search results", search);
        Assert.Contains("tab=search", page); // the tab is offered next to the page tabs
        Assert.Matches(@"<a href=""[^""]*tab=search[^""]*"" aria-current=""page"">Search result</a>", search);
    }

    [Theory]
    [InlineData("%3Cscript%3Ealert(1)%3C/script%3E")]
    [InlineData("/no-such-page")]
    [InlineData("SEARCH")]
    public async Task AnUnknownTab_FallsBackToTheHomePage_AndIsNeverReflected(string tab)
    {
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();
        await ReadyAsync(app, owner);

        var html = await owner.GetStringAsync(WizardFlow.Review + "?tab=" + tab);

        Assert.Contains("<iframe", html);
        Assert.DoesNotContain("<script>alert", html);
        Assert.DoesNotContain("adm-serp-title", html);
    }

    [Fact]
    public async Task TheStep_IsASplitLayout_InOneCard_WithTheFormBesideThePreview()
    {
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();

        var html = await owner.GetStringAsync(WizardFlow.Review);

        Assert.Single(Regex.Matches(html, @"class=""adm-card adm-wizard"""));
        Assert.Contains("class=\"adm-review\"", html);
        Assert.Single(Regex.Matches(html, @"<form id=""wizard-form"""));
        Assert.Contains("Review and launch · Step 4 of 4", Regex.Match(html, "<title>(.*?)</title>").Groups[1].Value);
        Assert.Contains(">Save and update preview</button>", html);
        Assert.DoesNotContain(">Back</a>", html);
    }

    [Fact]
    public async Task TheIndicator_StillShowsTheReviewStepAsCurrent_AndNeverDone()
    {
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();
        await ReadyAsync(app, owner);

        var states = WizardFlow.StepStates(await owner.GetStringAsync(WizardFlow.Review));

        Assert.Equal(["Done", "Done", "Optional", "To do"], states);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Portfolio.Web.Tests --filter "FullyQualifiedName~WizardReviewTests"`
Expected: FAIL (the step still redirects to Basics without a draft, has no checklist, no `adm-review`, no default POST handler).

- [ ] **Step 3: Reduce `ReviewForm` to the hard rules**

In `src/Portfolio.Web/Services/OnboardingDraft.cs`, replace the `SiteUrl` property of `ReviewForm` and its attributes:

old:
```csharp
    [Required(ErrorMessage = "Enter the address your site will be published at."), StringLength(SiteProfile.MaxLength.Url), HttpUrl, Display(Name = "Site address")]
    public string? SiteUrl { get; set; }
```
new:
```csharp
    /// <summary>Only the length is a hard rule. Whether it is a valid address is asked at publishing (<see cref="OnboardingService.IsValidAddress"/>).</summary>
    [StringLength(SiteProfile.MaxLength.Url), Display(Name = "Site address")]
    public string? SiteUrl { get; set; }
```

- [ ] **Step 4: Replace the Review page model**

Replace the whole file `src/Portfolio.Web/Pages/Admin/Welcome/Review.cshtml.cs` with:

```csharp
using Microsoft.AspNetCore.Mvc;
using Portfolio.Web.Configuration;
using Portfolio.Web.Security;
using Portfolio.Web.Services;

namespace Portfolio.Web.Pages.Admin.Welcome;

// A few short choices; anything much larger is not from this page.
[BodyLimit(16 * 1024)]
public class WelcomeReviewModel : WelcomePageModel
{
    /// <summary>The preview tab that shows the search-result card in place of the page.</summary>
    public const string SearchTab = "search";

    private readonly TemplateResolver _resolver;
    private readonly StarterContent _starter;
    private readonly Portfolio.Data.PortfolioDbContext _db;

    public WelcomeReviewModel(OnboardingService onboarding, ITemplateCatalog catalog, TemplateResolver resolver, StarterContent starter,
        Portfolio.Data.PortfolioDbContext db) : base(onboarding, catalog)
    {
        _resolver = resolver;
        _starter = starter;
        _db = db;
    }

    public override int CurrentStep => 4;

    [BindProperty]
    public ReviewForm Form { get; set; } = new();

    /// <summary>Which preview tab is open. Untrusted: it only ever selects one of <see cref="PreviewPages"/>, or <see cref="SearchTab"/>.</summary>
    [BindProperty(SupportsGet = true, Name = "tab")]
    public string? Tab { get; set; }

    [BindProperty(SupportsGet = true, Name = "size")]
    public string? Size { get; set; }

    public bool Mobile => string.Equals(Size, "mobile", StringComparison.Ordinal);

    /// <summary>The site as it would be published (identity, template, accent, photo), without the sample rows.</summary>
    public SiteOptions? DraftSite { get; private set; }

    public IReadOnlyList<(string Path, string Label)> PreviewPages { get; private set; } = [];
    public string PreviewPath { get; private set; } = "/";

    /// <summary>True when the search-result tab is open: the card replaces the preview frame.</summary>
    public bool ShowSearch { get; private set; }

    /// <summary>The value the width links keep in <c>tab</c>: the search tab, or the page being previewed.</summary>
    public string ActiveTab => ShowSearch ? SearchTab : PreviewPath;

    public IReadOnlyList<StarterTable> Sample { get; private set; } = [];
    public string SearchTitle { get; private set; } = "";
    public string SearchDescription { get; private set; } = "";

    /// <summary>The "Ready to publish?" lines, counting the saved draft with the address as it is shown in the form.</summary>
    public PublishChecklist Checklist { get; private set; } = null!;

    public string PreviewUrl =>
        $"{PreviewPath}?{RequestTemplate.PreviewParameter}={Uri.EscapeDataString(Template.Key)}&{RequestTemplate.DraftParameter}=1";

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        await LoadAsync(ct);

        Form = new ReviewForm
        {
            // The address is the one thing the owner cannot be expected to know is wrong: a new install still carries the placeholder from the
            // configuration, so a first run starts from the address this request came to.
            SiteUrl = Draft?.SiteUrl ?? (FirstRun ? $"{Request.Scheme}://{Request.Host}" : State.Profile.Url),
            Accent = Draft?.AccentKey,
            Content = Draft?.Content ?? StarterChoice.Blank,
        };
        await PrepareAsync(ct);
        return Page();
    }

    /// <summary>Continue, or one of the indicator's buttons: saves the choices under the hard rules, then opens the step asked for (this one by default).</summary>
    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        await LoadAsync(ct);
        if (await TrySaveAsync(ct) is { } again) return again;
        return Leave();
    }

    /// <summary>Saves the choices and shows the preview again.</summary>
    public async Task<IActionResult> OnPostSaveAsync(CancellationToken ct)
    {
        await LoadAsync(ct);
        if (await TrySaveAsync(ct) is { } again) return again;

        Flash("Choices saved. The preview shows your draft; nothing is public yet.");
        return RedirectToPage();
    }

    /// <summary>Saves the choices, then publishes the draft in one step (see <see cref="OnboardingService.PublishAsync"/>). The service checks everything again, so the disabled button is only a convenience.</summary>
    public async Task<IActionResult> OnPostPublishAsync(CancellationToken ct)
    {
        await LoadAsync(ct);
        if (Draft is null && !FirstRun)
        {
            // A second click or a replayed request after a publish: the draft is gone and the wizard is done. Nothing to apply.
            Flash("This setup was already published, so nothing changed.");
            return Redirect("/admin");
        }
        if (await TrySaveAsync(ct) is { } again) return again;

        var result = await Onboarding.PublishAsync(ct);
        switch (result.Outcome)
        {
            case PublishOutcome.Published:
                var added = result.Sample?.Where(t => t.WouldAdd > 0).Select(t => $"{t.WouldAdd} {t.Name.ToLowerInvariant()}").ToList();
                Flash("Your site is live." + (added is { Count: > 0 } ? $" Sample content added: {string.Join(", ", added)}." : ""));
                return Redirect("/admin");
            case PublishOutcome.Incomplete:
                // Back here, where the checklist says what is missing and links to the step that fixes it.
                FlashError("Nothing was published: something is missing or no longer valid. The checklist below shows what to fix.");
                return Redirect(WizardProgress.Steps[3].Path);
            case PublishOutcome.NoMenu:
                FlashError(result.Detail ?? "That template would leave your site without a menu section, so nothing was published.");
                return Redirect(WizardProgress.Steps[0].Path);
            case PublishOutcome.Changed:
                // Not "already published": the wizard is still open and the draft is kept, so the only truthful advice is to publish again.
                FlashError("Your profile was changed while publishing, so nothing was published. Your answers are kept: check them and press Publish again.");
                return Redirect(WizardProgress.Steps[3].Path);
            default:
                // A second click, a replayed request, or another tab got there first: nothing was applied by this request.
                Flash("This setup was already published, so nothing changed.");
                return Redirect("/admin");
        }
    }

    /// <summary>Saves the form's choices under the hard rules. Null when saved; otherwise the page to show again with the reason.</summary>
    private async Task<IActionResult?> TrySaveAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid) { await PrepareAsync(ct); return Page(); }
        if (!await Onboarding.SaveChoicesAsync(Form, ct))
        {
            ModelState.AddModelError(string.Empty, "That choice is not available for this template.");
            await PrepareAsync(ct);
            return Page();
        }
        return null;
    }

    private async Task PrepareAsync(CancellationToken ct)
    {
        DraftSite = await Onboarding.DraftPreviewAsync(ct);
        var site = DraftSite ?? SiteProfileMapper.ToOptions(State.Profile);
        var view = _resolver.Resolve(site, Template.Key);
        PreviewPages = TemplatePickerBuilder.PagesOf(view);
        ShowSearch = string.Equals(Tab, SearchTab, StringComparison.Ordinal);
        PreviewPath = PreviewPages.Select(p => p.Path).FirstOrDefault(p => p == Tab) ?? "/";
        SearchTitle = TemplateSeo.Apply(Template.Seo.Title, site);
        SearchDescription = TemplateSeo.ApplyDescription(Template.Seo.Description, site);
        Sample = StarterContent.Offers(Template) ? await _starter.DescribeAsync(_db, Template, ct) : [];

        // The address in the form is what the owner can see (on a first run it is prefilled and not yet saved), so the checklist counts it.
        var shown = (Draft ?? new OnboardingDraft()).Clone();
        shown.SiteUrl = ProfileForm.Line(Form.SiteUrl);
        Checklist = Onboarding.ChecklistFor(shown);
    }
}
```

- [ ] **Step 5: Replace `Review.cshtml`**

Replace the whole file `src/Portfolio.Web/Pages/Admin/Welcome/Review.cshtml` with:

```razor
@page "/admin/welcome/review"
@model WelcomeReviewModel
@{
    ViewData["Title"] = "Review and launch · Step 4 of 4";
    ViewData["Crumb"] = "Step 4: Review and launch";
    var checklist = Model.Checklist;
    var offersSample = StarterContent.Offers(Model.Template);
    var willAdd = Model.Sample.Where(t => t.WouldAdd > 0).ToList();
    var skipped = Model.Sample.Where(t => t.SkippedBecauseNotEmpty).ToList();
    var size = Model.Mobile ? "mobile" : "desktop";
}
<div class="adm-card adm-wizard">
    <partial name="_WizardHeader" model="Model" />
    <p class="adm-lede">Check the list, choose an accent colour and what to start with, then publish. Until you publish, nothing here is visible to visitors, search engines, the sitemap, the CV or the share image.</p>

    <div class="adm-review">
        <div class="adm-review-form">
            <section aria-labelledby="ready-heading">
                <h2 id="ready-heading">Ready to publish?</h2>
                <ul class="adm-readiness">
                    @foreach (var line in checklist.Lines)
                    {
                        var state = line.Done ? "Done" : line.Required ? "To do" : "Optional";
                        <li class="adm-ready-@(state.Replace(" ", "").ToLowerInvariant())">
                            <strong>@line.Label</strong>
                            <span class="adm-state">@state</span>
                            <span>@line.Detail</span>
                            <a href="@line.Path">@(line.Done ? "Change" : "Fix")<span class="adm-sr"> @line.Label.ToLowerInvariant()</span></a>
                        </li>
                    }
                </ul>
                @if (!string.IsNullOrWhiteSpace(Model.Template.Guidance))
                {
                    <p class="adm-callout"><strong>Before you publish.</strong> @Model.Template.Guidance</p>
                }
            </section>

            <form id="wizard-form" method="post" asp-page="/Admin/Welcome/Review" novalidate>
                @if (!ViewData.ModelState.IsValid)
                {
                    <div id="error-summary" class="adm-flash adm-flash-error" role="alert" tabindex="-1" autofocus>
                        <strong>Some choices need attention.</strong>
                        <div asp-validation-summary="All"></div>
                    </div>
                }

                <fieldset class="adm-fieldset">
                    <legend>Where it will live</legend>
                    <div class="adm-field">
                        <label for="Form_SiteUrl">Site address</label>
                        <input asp-for="Form.SiteUrl" type="url" maxlength="300" inputmode="url" aria-describedby="url-hint Form_SiteUrl-error" />
                        <span id="url-hint" class="adm-hint">The public address, for example https://example.com. Needed to publish. Used for canonical links, the sitemap and sharing.</span>
                        <span id="Form_SiteUrl-error" asp-validation-for="Form.SiteUrl"></span>
                    </div>
                </fieldset>

                <fieldset class="adm-fieldset">
                    <legend>Accent colour</legend>
                    <p class="adm-hint">The highlight colour for links, buttons and headings. It only changes colour variables; the template's own look is otherwise unchanged.</p>
                    <div class="adm-choices">
                        <div class="adm-choice">
                            <input type="radio" name="Form.Accent" id="accent-default" value="" checked="@string.IsNullOrEmpty(Model.Form.Accent)" />
                            <label for="accent-default">Template default</label>
                        </div>
                        @foreach (var accent in Model.Template.Theme.Accents)
                        {
                            <div class="adm-choice">
                                <input type="radio" name="Form.Accent" id="accent-@(accent.Key)" value="@accent.Key" checked="@(accent.Key == Model.Form.Accent)" />
                                <label for="accent-@(accent.Key)">
                                    <svg class="adm-swatch" width="28" height="28" viewBox="0 0 28 28" aria-hidden="true" focusable="false">
                                        <path d="M14 2a12 12 0 0 0 0 24Z" fill="@accent.Dark.Accent" />
                                        <path d="M14 2a12 12 0 0 1 0 24Z" fill="@accent.Light.Accent" />
                                    </svg>
                                    @accent.Label
                                </label>
                            </div>
                        }
                    </div>
                </fieldset>

                <fieldset class="adm-fieldset">
                    <legend>Starting content</legend>
                    <div class="adm-choices adm-choices-stacked">
                        <div class="adm-choice">
                            <input type="radio" name="Form.Content" id="content-blank" value="@StarterChoice.Blank" checked="@(Model.Form.Content != StarterChoice.Sample)" aria-describedby="content-blank-hint" />
                            <label for="content-blank">Start blank</label>
                            <span id="content-blank-hint" class="adm-hint">Only your details. Pages with no content show an empty state until you add some: after publishing, add your own under Content in the admin menu.</span>
                        </div>
                        @if (offersSample)
                        {
                            <div class="adm-choice">
                                <input type="radio" name="Form.Content" id="content-sample" value="@StarterChoice.Sample" checked="@(Model.Form.Content == StarterChoice.Sample)" aria-describedby="content-sample-hint" />
                                <label for="content-sample">Add sample content for @Model.Template.Profession.ToLowerInvariant()</label>
                                <span id="content-sample-hint" class="adm-hint">
                                    Fictional example entries so you can see a full site. Replace them with your own before sharing the site.
                                    @if (willAdd.Count > 0)
                                    {
                                        <text>This would add: @string.Join(", ", willAdd.Select(t => $"{t.WouldAdd} {t.Name.ToLowerInvariant()}")).</text>
                                    }
                                    @if (skipped.Count > 0)
                                    {
                                        <text> Left alone because they already have content (nothing is replaced or deleted): @string.Join(", ", skipped.Select(t => $"{t.Name.ToLowerInvariant()} ({t.Existing})")).</text>
                                    }
                                    @if (willAdd.Count == 0 && skipped.Count > 0)
                                    {
                                        <text> Choosing this will not add anything.</text>
                                    }
                                </span>
                            </div>
                        }
                    </div>
                </fieldset>

                <div class="adm-actions">
                    <button type="submit" class="adm-btn" asp-page-handler="Save">Save and update preview</button>
                    <button type="submit" class="adm-btn adm-btn-primary" asp-page-handler="Publish" disabled="@(!checklist.Ready)" aria-describedby="@(checklist.Ready ? null : "publish-reason")">Publish my site</button>
                    @if (checklist.Reason is { } why)
                    {
                        <span id="publish-reason" class="adm-hint">@why</span>
                    }
                </div>
            </form>
        </div>

        <section class="adm-review-preview" aria-labelledby="preview-heading">
            <h2 id="preview-heading">Preview of your draft</h2>
            <p class="adm-hint">Only you can see this. It shows the template, your details, photo and accent colour as saved (use "Save and update preview" after changing a choice). Sample content is added when you publish.</p>
            <nav class="adm-tabs" aria-label="Preview page">
                @foreach (var (path, label) in Model.PreviewPages)
                {
                    <a href="/admin/welcome/review?tab=@Uri.EscapeDataString(path)&size=@size"
                       aria-current="@(!Model.ShowSearch && path == Model.PreviewPath ? "page" : null)">@label</a>
                }
                <a href="/admin/welcome/review?tab=@WelcomeReviewModel.SearchTab&size=@size" aria-current="@(Model.ShowSearch ? "page" : null)">Search result</a>
            </nav>
            @if (Model.ShowSearch)
            {
                <div class="adm-serp" role="group" aria-labelledby="search-heading">
                    <h3 id="search-heading">How it may appear in search results</h3>
                    <p class="adm-serp-title">@Model.SearchTitle</p>
                    <p class="adm-serp-description">@Model.SearchDescription</p>
                </div>
            }
            else
            {
                <p class="adm-hint">
                    Width:
                    <a href="/admin/welcome/review?tab=@Uri.EscapeDataString(Model.ActiveTab)&size=desktop">desktop</a> ·
                    <a href="/admin/welcome/review?tab=@Uri.EscapeDataString(Model.ActiveTab)&size=mobile">mobile</a>
                </p>
                <iframe class="adm-preview@(Model.Mobile ? " adm-preview-mobile" : "")" src="@Model.PreviewUrl"
                        title="Preview of your draft site in the @Model.Template.Name template" referrerpolicy="same-origin"></iframe>
            }
        </section>
    </div>
    <partial name="_WizardFooter" model="Model" />
</div>
```

- [ ] **Step 6: Remove the old gating**

In `src/Portfolio.Web/Pages/Admin/Welcome/WelcomePageModel.cs`, delete the `Reachable` property (with its doc comment) and the `RequireStep` method (with its doc comment). Nothing calls either now: `Basics`, `Photo` and `Review` no longer do, and the header no longer reads `Reachable`.

Run `grep -rn "RequireStep\|Reachable" src` and confirm no match remains.

- [ ] **Step 7: CSS**

Append to `src/Portfolio.Web/wwwroot/css/admin.css`:

```css
.adm-review { display: grid; grid-template-columns: minmax(0, 1fr) minmax(0, 1.15fr); gap: var(--adm-space-5); align-items: start; }
.adm-review h2 { margin-top: 0; }
.adm-review-preview .adm-preview { height: clamp(18rem, calc(100vh - 22rem), 44rem); min-height: 0; }
.adm-serp { padding: var(--adm-space-4); border: 1px solid var(--adm-border); border-radius: var(--adm-radius-sm); background: var(--adm-bg); }
.adm-serp h3 { margin: 0 0 var(--adm-space-2); font-size: .95rem; color: var(--adm-muted); }
.adm-readiness { list-style: none; margin: 0 0 var(--adm-space-4); padding: 0; }
.adm-readiness li { display: flex; flex-wrap: wrap; align-items: baseline; gap: .25rem .6rem; padding: .4rem 0; border-bottom: 1px solid var(--adm-border); }
.adm-state { display: inline-block; padding: 0 .45rem; border-radius: .25rem; font-size: .75rem; font-weight: 600; border: 1px solid transparent; }
.adm-ready-done .adm-state { background: var(--adm-ok-bg); color: var(--adm-ok-text); }
.adm-ready-todo .adm-state { background: var(--adm-warn-bg); color: var(--adm-warn-text); }
.adm-ready-optional .adm-state { background: var(--adm-bg); color: var(--adm-muted); border-color: var(--adm-border); }
@media (max-width: 40rem) { .adm-review { grid-template-columns: 1fr; } }
```

- [ ] **Step 8: Run the tests**

Run: `dotnet test tests/Portfolio.Web.Tests --filter "FullyQualifiedName~WizardReviewTests|FullyQualifiedName~OnboardingFlowTests|FullyQualifiedName~OnboardingRestartTests|FullyQualifiedName~WizardRaceTests|FullyQualifiedName~OnboardingSecurityTests|FullyQualifiedName~ArchetypeTemplateFlowTests|FullyQualifiedName~OnboardingAccessibilityAccentTests"`
Expected: the new tests PASS. For anything else that fails, apply the rule from Task 4 Step 8. Known likely candidates: a test that expected a form error for an invalid site address on Save or Publish (now saved, and refused at publish with a redirect to Review); a test that matched the old review headings ("Your answers", "How it may appear in search results" as an always-visible card, "Preview of your draft" inside `adm-card`); the label test `EveryVisibleFormControl_HasARealLabelBoundByForAndId` if it counts the Review page's controls (the checklist adds links only, no controls).

- [ ] **Step 9: Commit**

```bash
git add src/Portfolio.Web/Services/OnboardingDraft.cs src/Portfolio.Web/Pages/Admin/Welcome src/Portfolio.Web/wwwroot/css/admin.css tests/Portfolio.Web.Tests
git commit -m "feat(admin): review step with a publish checklist beside the preview; remove step gating

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 7: The dashboard resumes at the first open step; docs and the spec sentence (criterion 20)

**Files:**
- Modify: `src/Portfolio.Web/Pages/Admin/Index.cshtml.cs` (lines 26-27 and 44-48)
- Modify: `src/Portfolio.Web/Pages/Admin/Welcome/WelcomePageModel.cs` (remove the `Steps` alias)
- Modify: `tests/Portfolio.Web.Tests/Admin/OnboardingFlowTests.cs` (line 200)
- Modify: `docs/new-user-guide.md`, `docs/superpowers/specs/2026-11-15-admin-redesign-design.md`
- Test: `tests/Portfolio.Web.Tests/Admin/WizardFrameTests.cs` (add three tests)

**Interfaces:**
- Consumes: `WizardProgress.Resume`, `OnboardingService.ProgressOf`.

- [ ] **Step 1: Write the failing tests**

Add to `tests/Portfolio.Web.Tests/Admin/WizardFrameTests.cs` (inside the class):

```csharp
    private static string ContinueSetupHref(string dashboard) =>
        Regex.Match(dashboard, @"<a class=""adm-btn adm-btn-primary"" href=""([^""]+)"">Continue setup</a>").Groups[1].Value;

    [Fact]
    public async Task ContinueSetup_OpensTheFirstStep_WithNoDraft()
    {
        using var app = new AdminApp();
        var owner = await WizardFlow.FirstRunClientAsync(app);

        Assert.Equal("/admin/welcome", ContinueSetupHref(await owner.GetStringAsync("/admin")));
    }

    [Fact]
    public async Task ContinueSetup_OpensTheBasics_AfterOnlyATemplate_AndWhileTheyAreIncomplete()
    {
        using var app = new AdminApp();
        var owner = await WizardFlow.FirstRunClientAsync(app);
        await WizardFlow.PickTemplateAsync(owner, "software");
        Assert.Equal("/admin/welcome/basics", ContinueSetupHref(await owner.GetStringAsync("/admin")));

        await WizardFlow.JumpAsync(owner, WizardFlow.Basics, 1, new() { ["Form.OwnerName"] = "Half Done" });
        Assert.Equal("/admin/welcome/basics", ContinueSetupHref(await owner.GetStringAsync("/admin")));
    }

    [Fact]
    public async Task ContinueSetup_SkipsTheOptionalPhoto_AndOpensTheReview_OnceTheBasicsAreDone()
    {
        using var app = new AdminApp();
        var owner = await WizardFlow.FirstRunClientAsync(app);
        await WizardFlow.PickTemplateAsync(owner, "software");
        await WizardFlow.SaveBasicsAsync(owner);

        Assert.Equal("/admin/welcome/review", ContinueSetupHref(await owner.GetStringAsync("/admin")));
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Portfolio.Web.Tests --filter "FullyQualifiedName~WizardFrameTests.ContinueSetup"`
Expected: FAIL on the third test (the dashboard still resumes at the photo) and on the second's second half (a half-filled Basics resumes at the photo too, because `Step` was written as 2).

- [ ] **Step 3: Use the status for the resume path**

In `src/Portfolio.Web/Pages/Admin/Index.cshtml.cs`, replace the doc comment on `ResumePath`:

old:
```csharp
    /// <summary>Where "Continue setup" goes: the first step that is not saved yet.</summary>
```
new:
```csharp
    /// <summary>Where "Continue setup" goes: the first step that is neither done nor optional (see <see cref="WizardProgress.Resume"/>).</summary>
```

and the body lines:

old:
```csharp
            if (state.Draft is { } draft)
                ResumePath = Welcome.WelcomePageModel.Steps[Math.Clamp(draft.Step + 1, 1, 4) - 1].Path;
```
new:
```csharp
            ResumePath = WizardProgress.Resume(_onboarding.ProgressOf(state.Draft)).Path;
```

In `src/Portfolio.Web/Pages/Admin/Welcome/WelcomePageModel.cs`, delete the `Steps` alias property and its doc comment (Task 6 left no other user: `Review.cshtml.cs` now says `WizardProgress.Steps`). Run `grep -rn "WelcomePageModel.Steps\|[^.]Steps\[" src/Portfolio.Web/Pages` and fix any remaining reference to `WizardProgress.Steps`.

- [ ] **Step 4: Update the one existing assertion that pinned the old resume**

In `tests/Portfolio.Web.Tests/Admin/OnboardingFlowTests.cs`, in `TheDraft_SurvivesARefreshAndANewSession_AndEachStepRestoresItsAnswers`, replace the last assertion and its comment:

old:
```csharp
        Assert.Contains("href=\"/admin/welcome/photo\"", dashboard); // resumes at the first step that is not saved yet... after basics that is the photo
```
new:
```csharp
        Assert.Contains("href=\"/admin/welcome/review\"", dashboard); // resumes at the first step that is neither done nor optional: the photo is optional, so after the basics that is the review
```

- [ ] **Step 5: Docs**

In `docs/new-user-guide.md` (keep the file's existing line endings: check with `git ls-files --eol docs/new-user-guide.md`; if it reports `w/crlf`, write the new line breaks as CRLF), make three single-line edits with the Edit tool:

1. Line 75, old: `   from the template's suggestion; they are published as written, so edit them.`
   new: `   from the template's suggestion; they are published as written, so edit them. You may leave a field blank or half typed while you move on; only publishing needs the name, title, email and summary complete and the email valid.`

2. Line 81, old: `   content** (fictional entries so you can see a full site). Press **Publish my site**.`
   new: `   content** (fictional entries so you can see a full site). A **Ready to publish?** checklist lists the template, your basics, the photo (optional) and the site address, each with a link to the step that fixes it, and **Publish my site** stays greyed out, with the reason beside it, until the list is complete. The preview has a **Search result** tab that shows how the page may appear in search results.`

3. Line 83, old: `Publishing applies everything in one transaction and refreshes the sitemap, CV and share image at once. Pressing it`
   new (a new paragraph, then the original sentence start):
   `**Jumping between steps.** The four steps at the top of each page are buttons: click any of them, from any step, to go there. What you typed on the current step is saved first, even if it is unfinished, and each step says in words whether it is Done, To do, Needs attention or Optional. The only things checked on every save are that text is plain (no \`<\` or \`>\`) and not too long; if a value breaks that, you stay on the step and see why. Everything else is checked when you publish, and the server checks it again whatever the button says.` followed by a blank line, then `Publishing applies everything in one transaction and refreshes the sitemap, CV and share image at once. Pressing it`

In `docs/superpowers/specs/2026-11-15-admin-redesign-design.md`, line 149, old: `- Bottom: "Start over" (when a draft exists) and "Skip setup" or "Leave without publishing" on the left; one "Continue" button on the right. The Back button is removed.`
new: `- Bottom: "Start over" (when a draft exists) and "Skip setup" or "Leave without publishing" on the left; one "Continue" button on the right. The Back button is removed. On the Template step the picker's own "Use [name] and continue" is the Continue button, and on the Review step "Publish my site" is.`

- [ ] **Step 6: Run the tests**

Run: `dotnet test tests/Portfolio.Web.Tests --filter "FullyQualifiedName~WizardFrameTests|FullyQualifiedName~AdminDashboardTests|FullyQualifiedName~OnboardingFlowTests|FullyQualifiedName~WizardRaceTests"`
Expected: PASS. `AdminDashboardTests` and `WizardRaceTests` assert only that "Continue setup" is present, not where it points.

- [ ] **Step 7: Commit**

```bash
git add src/Portfolio.Web/Pages/Admin/Index.cshtml.cs src/Portfolio.Web/Pages/Admin/Welcome/WelcomePageModel.cs tests/Portfolio.Web.Tests docs/new-user-guide.md docs/superpowers/specs/2026-11-15-admin-redesign-design.md
git commit -m "feat(admin): Continue setup opens the first open step; document jumping and the checklist

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 8: Verification, browser pass and review (criteria 15 to 21, manual 18)

**Files:** none planned. Fix what the suite and the browser find, each fix with its own test.

- [ ] **Step 1: Build**

Run: `dotnet build Portfolio.slnx`
Expected: succeeds with no new warnings; only the 16 baseline NU1902/NU1903 advisories. A warning in a file this PR touched is ours: fix it.

- [ ] **Step 2: Whole suite**

Run: `dotnet test Portfolio.slnx`
Expected: every test passes except the baseline failures recorded before Task 1 (the line-ending ones). Report the exact counts: passed, failed, skipped. A pass on zero executed tests is "not executed", not a pass.

For every failure that is not in the baseline list, decide whether it pinned removed behaviour (rewrite it against the new rule and record why in the commit message) or found a real regression (fix the code, with a test). Do not loosen an assertion to make it pass.

- [ ] **Step 3: Criteria map**

Confirm each criterion has a test that ran, or a manual check below:

| Criterion | Where |
|---|---|
| 15 Every step clickable from every step, with no draft | `WizardFrameTests.EveryPage_OffersAllFourSteps...`; each step's "opens with no draft" test |
| 16 Jumping saves what was typed, including incomplete input | `WizardBasicsJumpTests.Jumping_Saves...`; `WizardReviewTests.Jumping_Saves...`; `WizardPhotoJumpTests.Jumping_WithAChosenFile...` |
| 17 A hard-rule value blocks the jump and shows the error | `WizardBasicsJumpTests.MarkupInAField...`, `ATooLongSummary...`; `WizardReviewTests.ATooLongAddress...`; `WizardPhotoJumpTests.AFileThatIsNotAnImage...` |
| 18 Each step shows its status in text | `WizardFrameTests.EveryStep_SaysItsStatusInWords...`; `WizardBasicsJumpTests.TheStatus_Changes...`; manual: colour and text in light and dark |
| 19 Publish disabled with a reason; server refuses an incomplete publish | `WizardReviewTests.Publish_IsDisabled...`, `AnIncompletePublish_IsRefused...`, `AnInvalidEmail...`, `AnAddressThatIsNotAnAddress...` |
| 20 Full first-run pass, and a re-run on a finished site | `OnboardingFlowTests.FromSetupToALiveSite_InTheChosenTemplate`; `WizardFrameTests.OnAReRun_...`, `TheBottomBar_OffersLeaveWithoutPublishing_OnAReRun`; `OnboardingRestartTests` |
| 21 Start over, Skip setup and Leave without publishing behave as before | `WizardFrameTests.TheBottomBar_...`; `OnboardingFlowTests` (skip, discard); `OnboardingSecurityTests` (handlers on every step) |

- [ ] **Step 4: Browser pass**

Start the app per `CONTRIBUTING.md` (or the `run` skill), sign in, and check at 1280 by 800 and 390 by 844, in light and dark (`prefers-color-scheme`):

1. Menu, Tools, Setup wizard opens step 1 inside one card: indicator on top with a status under each step, picker in the middle, Skip or Leave (and Start over with a draft) bottom left.
2. Click step 4, then step 2, then step 1 with no draft: each opens, and a draft now exists (the "Draft saved at HH:mm UTC" line appears).
3. On Basics, type a name only and press Enter: the page goes to step 3 (not step 1) and the name is kept. Go back: Basics says "Needs attention".
4. Type `<b>` in the name and click step 4: you stay on Basics, the alert takes the focus, the field is marked invalid.
5. On Photo, choose a file and click Review without pressing Upload: the photo is saved and Review opens; step 3 shows "Done".
6. On Review with the basics missing: Publish is greyed out with "Still to do: basics." beside it; the checklist link takes you to step 2. Complete everything: Publish is enabled. Click the Search result tab: the card replaces the frame; click Home: the frame returns.
7. Keyboard only: Tab reaches the four step buttons in order, then the page content, then the bottom bar; focus rings are visible on the step buttons.
8. At 390 wide: the indicator stacks, the Review form is above the preview, nothing scrolls sideways.
9. Publish. Re-open the wizard from the menu: the bottom bar says "Leave without publishing", and step 1 opens on the site's template.

Record what was seen and what was not run in the final report. If the browser pass cannot run in this environment, say so plainly; do not claim it.

- [ ] **Step 5: Independent review**

Run the `quality-reviewer` agent on the range `6f5dc32..HEAD` with the spec section 6 and criteria 15 to 21 as the brief. Fix what it confirms, with tests; list what it raised and what was declined, with the reason.

- [ ] **Step 6: Final commit and hand-off**

Commit any fixes (explicit `git add` paths, never `app.css`). Report: the branch and tip, test counts and baseline failures, the browser results (or "not run"), the decisions in this plan the owner should confirm, and the note that this branch must be rebased onto `develop` after PR B merges (`git rebase --onto develop 6f5dc32 feature/admin-redesign-pr-c`). Do not push and do not open a PR.

---

## Self-review

**Spec coverage (section 6 and criteria 15 to 21):**

- 6.1 Frame: indicator and saved-at on top, content in the middle, Skip or Leave and Start over bottom left, one Continue bottom right, Back removed, live region and heading focus kept: Task 3 (header, footer, Template), Tasks 4 to 6 (each step). The Template and Review steps' own main buttons are decision 1.
- 6.2 Step status: Task 1 (`WizardProgress.Compute`), shown in text in Task 3's header; `aria-current="step"` on the current one.
- 6.3 Jumping: `goto` buttons tied to the form, clamped, saved then redirected (Task 3 plumbing, one test each in Tasks 3 to 6); hard rules versus completeness (Task 4 `BasicsForm`, Task 6 `ReviewForm`); template step keeps the draft's template and creates one on the current template (Tasks 2 and 3); photo uploaded on jump, invalid file stays (Task 5); Review saves address, accent and content under the hard rules (Task 6).
- 6.4 Steps: Template is the PR B picker; Basics keeps its five fields and the suggestions beside them (Task 4); Photo is one card, current photo left, upload right (Task 5); Review is a split with checklist, address, accent and content on the left and the preview with page tabs, width switch and a Search result tab on the right, and "Save and update preview" stays (Task 6).
- 6.5 Publishing: button disabled with the reason beside it, the service re-validates unchanged, a refused publish returns to Review (Task 6).
- 6.6 Service changes: partial saves (Tasks 2 and 4), `StepStatus` exposed to pages and dashboard (Tasks 1, 3, 7), `RequireStep` and `Reachable` removed (Task 6), `DraftPreviewAsync` applies each field with a value (Task 2), dashboard resume (Task 7).
- Criteria 15 to 21: mapped in Task 8 Step 3.

**Placeholder scan:** no "TBD", "TODO" or "add validation" steps; every code step shows the code. Where a step says "if X fails, then Y" (Task 4 Step 8, Task 5 Step 7, Task 6 Step 8) it names the rule for deciding, because the exact set of old tests that pinned removed behaviour is only known by running the suite.

**Type consistency:** `WizardProgress.Steps` is `IReadOnlyList<(int Number, string Label, string Path)>` everywhere; `StepStatus` fields are `Number`, `Label`, `Path`, `State`; `PublishChecklist.Lines` elements are `ChecklistLine(Label, Done, Detail, Path, Required)`; `Leave()`, `Goto`, `NextStep`, `ContinueLabel` and `Progress` are defined in Task 3 and used with the same names in Tasks 4 to 6; `OnboardingService.TemplateFor`, `ProgressOf`, `ChecklistFor` and `KeepTemplateAsync` are defined in Task 2 and used in Tasks 3, 6 and 7; the markup attribute order `type="submit" form="wizard-form" name="goto" value="N"` is fixed in the header and footer (Task 3) and matched by `WizardFlow.StepValues`.

**Known gaps (state them in the hand-off, do not hide them):**

- The visual fit of the Review split and the indicator at 1280 by 800 and 390 by 844 cannot be asserted in a test; Task 8 Step 4 is the only check, and CSS values (`.adm-review-preview .adm-preview` height, the indicator button padding) may need one tuning pass after it.
- The hidden default button relies on browsers submitting through the first submit button in tree order, regardless of `display`; the plan uses the visually-hidden class rather than `display: none` for that reason, but it is only tested at the markup level, so keep it in the manual pass (step 3).
- Existing tests that pinned old wizard markup or error text are fixed as they surface (Tasks 4 to 6 name the likely ones); the full list is only known after Task 8 Step 2.
