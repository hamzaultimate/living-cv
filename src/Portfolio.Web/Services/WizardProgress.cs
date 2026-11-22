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
