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
