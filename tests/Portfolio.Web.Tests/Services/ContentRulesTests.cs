using Portfolio.Web.Services;
using Xunit;

namespace Portfolio.Web.Tests.Services;

public class ContentRulesTests
{
    [Theory]
    [InlineData("<b>bold</b>")]
    [InlineData("hello <script>alert(1)</script>")]
    [InlineData("<!-- comment -->")]
    [InlineData("<?php echo 1 ?>")]
    [InlineData("text </div>")]
    [InlineData("<img src=x onerror=alert(1)>")]
    public void Markup_IsDetected(string value) => Assert.True(ContentRules.HasMarkup(value));

    [Theory]
    [InlineData("Cut latency to <50ms")]
    [InlineData("5 < 6 and 7 > 3")]
    [InlineData("C# & .NET")]
    [InlineData("")]
    [InlineData(null)]
    public void OrdinaryText_IsNotMarkup(string? value) => Assert.False(ContentRules.HasMarkup(value));

    [Theory]
    [InlineData("https://example.com", true)]
    [InlineData("http://example.com/a?b=c#d", true)]
    [InlineData("HTTPS://EXAMPLE.COM", true)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("JaVaScRiPt:alert(1)", false)]
    [InlineData("data:text/html;base64,PHNjcmlwdD4=", false)]
    [InlineData("vbscript:msgbox(1)", false)]
    [InlineData("file:///etc/passwd", false)]
    [InlineData("ftp://example.com", false)]
    [InlineData("//example.com", false)]
    [InlineData("/relative/path", false)]
    [InlineData("https://user:pass@example.com", false)]
    [InlineData("https://", false)]
    [InlineData("https://exa\u0000mple.com", false)]
    [InlineData("", false)]
    public void OnlyHttpAndHttpsAddresses_AreAccepted(string value, bool expected) => Assert.Equal(expected, ContentRules.IsHttpUrl(value));

    [Theory]
    [InlineData("/img/badges/badge.svg", true)]
    [InlineData("/proofs/sample-report.svg", true)]
    [InlineData("//evil.example/x", false)]
    [InlineData("/a/../b", false)]
    [InlineData("/a\\b", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("/x y", false)]
    public void LocalPaths_AreStrict(string value, bool expected) => Assert.Equal(expected, ContentRules.IsLocalPath(value));

    [Fact]
    public void ARelativeAddress_IsOnlyKeptWhenItIsExactlyTheStoredOne()
    {
        Assert.True(ContentRules.IsAcceptableUrl("/img/badges/badge.svg", "/img/badges/badge.svg"));
        Assert.False(ContentRules.IsAcceptableUrl("/img/badges/other.svg", "/img/badges/badge.svg"));
        Assert.False(ContentRules.IsAcceptableUrl("/img/badges/badge.svg", null));
        // A stored value that is itself unsafe never unlocks itself.
        Assert.False(ContentRules.IsAcceptableUrl("javascript:alert(1)", "javascript:alert(1)"));
        Assert.True(ContentRules.IsAcceptableUrl("https://example.com", null));
    }

    [Theory]
    [InlineData("2021", 2021, 1, 1, 2021, 12, 31)]
    [InlineData("Mar 2021", 2021, 3, 1, 2021, 3, 31)]
    [InlineData("March 2021", 2021, 3, 1, 2021, 3, 31)]
    [InlineData("2021-03", 2021, 3, 1, 2021, 3, 31)]
    [InlineData("2020-02", 2020, 2, 1, 2020, 2, 29)]
    [InlineData("2021-03-15", 2021, 3, 15, 2021, 3, 15)]
    [InlineData("  2019  ", 2019, 1, 1, 2019, 12, 31)]
    public void PartialDates_CoverTheirWholePeriod(string text, int fy, int fm, int fd, int ly, int lm, int ld)
    {
        Assert.True(PartialDate.TryParse(text, out var date));
        Assert.Equal(new DateOnly(fy, fm, fd), date.First);
        Assert.Equal(new DateOnly(ly, lm, ld), date.Last);
    }

    [Theory]
    [InlineData("")]
    [InlineData("soon")]
    [InlineData("2021-13")]
    [InlineData("1800")]
    [InlineData("3000")]
    [InlineData("12")]
    [InlineData("Mar 21")]
    [InlineData("2021-02-30")]
    [InlineData("<b>2021</b>")]
    public void NonsenseDates_AreRejected(string text) => Assert.False(PartialDate.TryParse(text, out _));

    [Theory]
    [InlineData("My First Project", "my-first-project")]
    [InlineData("  Spaces   and --- dashes  ", "spaces-and-dashes")]
    [InlineData("Café déjà vu", "cafe-deja-vu")]
    [InlineData("../../etc/passwd", "etc-passwd")]
    [InlineData("a/b\\c?d#e%2f", "a-b-c-d-e-2f")]
    [InlineData("<script>alert(1)</script>", "script-alert-1-script")]
    [InlineData("日本語", "project")]
    [InlineData("", "project")]
    [InlineData("new", "new-1")]
    [InlineData("Admin", "admin-1")]
    public void GeneratedSlugs_AreUrlSafe(string title, string expected)
    {
        var slug = Slugs.From(title);
        Assert.Equal(expected, slug);
        Assert.True(Slugs.IsValid(slug));
        Assert.False(Slugs.IsReserved(slug));
    }

    [Fact]
    public void ALongTitle_GivesASlugThatFitsTheColumnWithRoomForASuffix()
    {
        var slug = Slugs.From(new string('a', 500));
        Assert.True(slug.Length <= 100);
        Assert.True(Slugs.IsValid(Slugs.Unique(slug, s => s == slug)));
        Assert.True(Slugs.Unique(slug, s => s == slug).Length <= Slugs.MaxLength);
    }

    [Fact]
    public void Collisions_GetTheNextFreeNumber()
    {
        var taken = new HashSet<string> { "work", "work-2", "work-3" };
        Assert.Equal("work-4", Slugs.Unique("work", taken.Contains));
        Assert.Equal("other", Slugs.Unique("other", taken.Contains));
    }

    [Theory]
    [InlineData("good-slug", true)]
    [InlineData("a", true)]
    [InlineData("a1-b2", true)]
    [InlineData("Bad", false)]
    [InlineData("-lead", false)]
    [InlineData("trail-", false)]
    [InlineData("dou--ble", false)]
    [InlineData("a/b", false)]
    [InlineData("a.b", false)]
    [InlineData("a%2fb", false)]
    [InlineData("a b", false)]
    [InlineData("..", false)]
    [InlineData("", false)]
    public void TypedSlugs_AreCheckedStrictly(string slug, bool expected) => Assert.Equal(expected, Slugs.IsValid(slug));
}
