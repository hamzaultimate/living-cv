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
