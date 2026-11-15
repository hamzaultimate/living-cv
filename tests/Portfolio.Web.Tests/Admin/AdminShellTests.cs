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
            Assert.Matches(@"<form method=""post""[^>]*action=""/admin/logout""|<form[^>]*action=""/admin/logout""[^>]*method=""post""", menu);
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
            Assert.DoesNotMatch(@"\shidden[\s=>/]", menu);    // no element a missing script would have to reveal (an anti-forgery type=""hidden"" input is not one)
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
        Assert.Matches(@"<button[^>]*data-adm-open[^>]*aria-controls=""adm-menu""[^>]*aria-expanded=""false""|<button[^>]*data-adm-open[^>]*aria-expanded=""false""[^>]*aria-controls=""adm-menu""|<button[^>]*aria-controls=""adm-menu""[^>]*aria-expanded=""false""[^>]*data-adm-open", html);
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

        var src = Regex.Match(page, "<script src=\"(/js/admin-shell[^\"]*\\.js[^\"]*)\"");
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

        // Pages that read nothing from the dropped table themselves (the template page and the content hub count their own rows, so they fail on their own account).
        foreach (var path in new[] { "/admin", ProfileFlow.Identity, ProfileFlow.Hero, "/admin/photo", SectionsFlow.Path, TransferFlow.Page, "/admin/welcome", ContentFlow.Skills })
        {
            var response = await owner.GetAsync(path);
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{path} returned {(int)response.StatusCode}");
            var menu = MenuOf(await response.Content.ReadAsStringAsync());
            Assert.DoesNotContain("adm-menu-count", menu);
            Assert.Contains("href=\"/admin/content/experience\"", menu);
        }
    }
}
