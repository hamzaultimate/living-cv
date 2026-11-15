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

    [Theory]
    [InlineData(".adm-split")]
    [InlineData(".adm-attention")]
    [InlineData(".adm-pagehead")]
    [InlineData(".adm-menu")]
    public void TheNewShellPieces_AreStyled(string selector) => Assert.Contains(selector + " {", Css());

    [Fact]
    public void TheRailLink_IsHiddenWhileTheMenuIsExpanded_WithEnoughSpecificityToBeatTheMenuLinkRule()
    {
        // ".adm .adm-menu-link" sets display:flex; a bare ".adm-menu-rail" would lose to it and every parent would show twice.
        var css = Css();

        Assert.Contains(".adm .adm-menu-rail { display: none; }", css);
        Assert.Contains(".adm-shell[data-menu=\"collapsed\"] .adm-menu-rail { display: flex; }", css);
    }

    [Fact]
    public void OnNarrowScreens_TheCollapseButtonIsHidden_WithEnoughSpecificityToBeatTheMenuLinkRule() =>
        Assert.Contains(".adm .adm-menu-toggle { display: none; }", Css()); // a bare ".adm-menu-toggle" loses to ".adm .adm-menu-link { display: flex }"

    [Fact]
    public void TheNarrowOverlay_StretchesTopToBottom_InsteadOfShrinkingToItsContent() =>
        // The wide menu is "align-self: start"; a fixed overlay inherits that and would stop at its last link with the page showing beneath it.
        Assert.Matches(@"\.adm-shell\.adm-js \.adm-menu \{[^}]*align-self: stretch;", Css());

    [Fact]
    public void TheMenuFooter_StaysInView_WhenTheMenuItselfScrolls() =>
        Assert.Matches(@"\.adm-menu-foot \{[^}]*position: sticky;[^}]*bottom: 0;", Css());

    [Fact]
    public void TheRail_OnlyStopsScrolling_WhereThereIsRoomForItsTooltips()
    {
        // A rail that cannot scroll cuts off its lower icons on a short screen (a landscape phone). Its tooltips need overflow to be visible, so only taller screens get that.
        var css = Css();

        Assert.Matches(@"@media \(min-width: 40\.0625rem\) and \(min-height: 34rem\) \{\s*\.adm-shell\[data-menu=""collapsed""\] \.adm-menu \{ overflow: visible; \}", css);
        Assert.DoesNotMatch(@"\.adm-shell\[data-menu=""collapsed""\] \.adm-menu \{ overflow: visible; padding-inline", css);
    }

    [Fact]
    public void TheOldDashboardTileStyles_AreGone() => Assert.DoesNotContain(".adm-tile", Css());

    [Fact]
    public void TheMotionAndColourSchemeHandling_IsKept()
    {
        var css = Css();

        Assert.Contains("prefers-reduced-motion: reduce", css);
        Assert.Contains("prefers-color-scheme: dark", css);
    }

    [Fact]
    public void NoRuleReachesForAnInlineOnlyFeature_TheAdminCspForbids()
    {
        // style-src 'self' means a stylesheet is the only source of style; an @import of another origin would be refused, so none may be added.
        Assert.DoesNotMatch(@"@import\s+url\(\s*['""]?https?:", Css());
    }
}
