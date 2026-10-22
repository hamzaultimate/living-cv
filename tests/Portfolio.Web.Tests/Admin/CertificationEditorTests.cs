using System.Net;
using Microsoft.EntityFrameworkCore;
using Portfolio.Data.Entities;
using Portfolio.Web.Tests.Support;
using Xunit;

namespace Portfolio.Web.Tests.Admin;

public class CertificationEditorTests
{
    private const string Edit = ContentFlow.Certifications + "/edit";

    private static Dictionary<string, string> Cert(string name = "Registered Nurse", string issuer = "Nursing Council", string status = "Earned", string date = "2018",
        string category = "", string credential = "", string certificate = "", string badge = "") =>
        ContentFlow.Fields(("Name", name), ("Issuer", issuer), ("Status", status), ("Date", date), ("Category", category), ("CredentialUrl", credential),
            ("CertificateUrl", certificate), ("BadgeImageUrl", badge));

    private static async Task<List<Certification>> RowsAsync(AdminApp app)
    {
        using var db = app.CreateDbContext();
        return await db.Certifications.AsNoTracking().OrderBy(x => x.SortOrder).ThenBy(x => x.Id).ToListAsync();
    }

    [Fact]
    public async Task CreatingACertification_Persists_AndShowsOnThePublicPage()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);

        var fields = Cert("Azure Fundamentals", "Microsoft", "Earned", "Mar 2024", "Cloud", "https://learn.microsoft.com/credentials/x", "https://example.com/cert.pdf", "https://example.com/badge.png");
        var create = await ContentFlow.PostAsync(owner, Edit, fields);

        Assert.Equal(HttpStatusCode.Redirect, create.StatusCode);
        var row = Assert.Single(await RowsAsync(app));
        Assert.Equal(("Azure Fundamentals", "Microsoft", CertificationStatus.Earned, "Mar 2024", "Cloud"), (row.Name, row.Issuer, row.Status, row.Date, row.Category));
        Assert.Equal("https://learn.microsoft.com/credentials/x", row.CredentialUrl);
        var page = await app.NewClient().GetStringAsync("/certifications");
        Assert.Contains("Azure Fundamentals", page);
        Assert.Contains("https://learn.microsoft.com/credentials/x", page);
    }

    [Fact]
    public async Task TheStatusWords_AndTheEntryName_ComeFromTheTemplate()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app, "healthcare");

        var html = await owner.GetStringAsync(Edit);

        Assert.Contains("<option value=\"Earned\" selected=\"selected\">", html);
        Assert.Contains("<option value=\"InProgress\">", html);
        Assert.Contains("Registration or qualification", html);
        Assert.Contains("Issuing body", html);
        Assert.Contains("Register entry link", html);
        Assert.Contains("Add licence or registration", await owner.GetStringAsync(ContentFlow.Certifications));
        Assert.Contains("Licences &amp; registrations", await owner.GetStringAsync(ContentFlow.Certifications));
    }

    [Fact]
    public async Task OnlyEarnedEntries_BecomeCredentialsInTheHomePageMarkup()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app, "healthcare");

        await ContentFlow.PostAsync(owner, Edit, Cert("Held Registration", "The Council", "Earned", "2019"));
        await ContentFlow.PostAsync(owner, Edit, Cert("Studying Towards", "The College", "InProgress", "2027"));
        await ContentFlow.PostAsync(owner, Edit, Cert("Planned Exam", "The Society", "Planned", ""));

        var home = await app.NewClient().GetStringAsync("/");
        Assert.Contains("hasCredential", home);
        Assert.Contains("Held Registration", home);
        Assert.DoesNotContain("Studying Towards\"", home.Substring(home.IndexOf("hasCredential", StringComparison.Ordinal)));
        Assert.DoesNotContain("Planned Exam\"", home.Substring(home.IndexOf("hasCredential", StringComparison.Ordinal)));
        Assert.Contains("registrations", await app.NewClient().GetStringAsync("/certifications"));
    }

    [Fact]
    public async Task AnEarnedEntry_CannotBeDatedInTheFuture_ButAPlannedOneCan()
    {
        using var app = new AdminApp(); // the test clock stands at 1 January 2026
        var owner = await ContentFlow.BlankSiteAsync(app);

        foreach (var future in new[] { "2027", "Feb 2026", "2026-02", "2026-01-02" })
        {
            var refused = await ContentFlow.PostAsync(owner, Edit, Cert(date: future));
            Assert.Contains("is in the future", await refused.Content.ReadAsStringAsync());
        }
        Assert.Empty(await RowsAsync(app));

        Assert.Equal(HttpStatusCode.Redirect, (await ContentFlow.PostAsync(owner, Edit, Cert("Held", date: "2026"))).StatusCode);       // this year, already started
        Assert.Equal(HttpStatusCode.Redirect, (await ContentFlow.PostAsync(owner, Edit, Cert("Later", status: "Planned", date: "2027"))).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await ContentFlow.PostAsync(owner, Edit, Cert("Doing", status: "InProgress", date: "Sep 2026"))).StatusCode);
        Assert.Equal(3, (await RowsAsync(app)).Count);
    }

    [Theory]
    [InlineData("soon")]
    [InlineData("13/2020")]
    [InlineData("1999-99")]
    [InlineData("<b>2020</b>")]
    public async Task ADateThatIsNotADate_IsRefused(string date)
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);

        var response = await ContentFlow.PostAsync(owner, Edit, Cert(date: date));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(await RowsAsync(app));
    }

    [Theory]
    [InlineData("CredentialUrl", "javascript:alert(1)")]
    [InlineData("CredentialUrl", "JAVASCRIPT:alert(1)")]
    [InlineData("CertificateUrl", "data:text/html;base64,PHNjcmlwdD5hbGVydCgxKTwvc2NyaXB0Pg==")]
    [InlineData("BadgeImageUrl", "vbscript:msgbox(1)")]
    [InlineData("CredentialUrl", "//evil.example/x")]
    [InlineData("CertificateUrl", "/img/certs/anything.svg")]
    [InlineData("BadgeImageUrl", "ftp://example.com/x.png")]
    [InlineData("CredentialUrl", "https://user:pw@example.com")]
    public async Task OnlyHttpAndHttpsAddresses_AreAccepted(string field, string value)
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        var fields = Cert();
        fields["Form." + field] = value;

        var response = await ContentFlow.PostAsync(owner, Edit, fields);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("must be a full web address starting with http:// or https://", await response.Content.ReadAsStringAsync());
        Assert.Empty(await RowsAsync(app));
    }

    [Fact]
    public async Task UnknownStatuses_AreRefused_WhateverTheBrowserSent()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);

        foreach (var status in new[] { "Pending", "earned", "0", "1", "Earned; DROP TABLE Certifications", "" })
        {
            var response = await ContentFlow.PostAsync(owner, Edit, Cert(status: status));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        Assert.Empty(await RowsAsync(app));
    }

    [Fact]
    public async Task AnOlderEntry_KeepsItsSiteRelativeLinks_ButCannotGainNewOnes()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        using (var db = app.CreateDbContext())
        {
            db.Certifications.Add(new Certification { Name = "Old", Issuer = "Board", Status = CertificationStatus.Earned, Date = "2020", BadgeImageUrl = "/img/badges/badge.svg", CertificateUrl = "/img/certs/certificate.svg", SortOrder = 1 });
            await db.SaveChangesAsync();
        }
        var id = (await RowsAsync(app)).Single().Id;
        var path = $"{Edit}/{id}";

        var unchanged = Cert("Old", "Board", "Earned", "2020", "", "", "/img/certs/certificate.svg", "/img/badges/badge.svg");
        unchanged["Form.Stamp"] = await ContentFlow.StampAsync(owner, path);
        Assert.Equal(HttpStatusCode.Redirect, (await ContentFlow.PostAsync(owner, path, unchanged)).StatusCode);

        var changed = Cert("Old", "Board", "Earned", "2020", "", "", "/img/certs/other.svg", "/img/badges/badge.svg");
        changed["Form.Stamp"] = await ContentFlow.StampAsync(owner, path);
        var refused = await ContentFlow.PostAsync(owner, path, changed);
        Assert.Equal(HttpStatusCode.OK, refused.StatusCode);
        Assert.Equal("/img/certs/certificate.svg", (await RowsAsync(app)).Single().CertificateUrl);
    }

    [Fact]
    public async Task DuplicateNames_AreRefused()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        await ContentFlow.PostAsync(owner, Edit, Cert("Same Name", "Board A"));

        var again = await ContentFlow.PostAsync(owner, Edit, Cert("same name", "Board B"));

        Assert.Contains("already have a certification called", await again.Content.ReadAsStringAsync());
        Assert.Single(await RowsAsync(app));
    }

    [Fact]
    public async Task Reordering_PersistsAndTheIssuerGroupsFollowThePublicOrder()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        await ContentFlow.PostAsync(owner, Edit, Cert("One", "Issuer One"));
        await ContentFlow.PostAsync(owner, Edit, Cert("Two", "Issuer Two"));
        var ids = (await RowsAsync(app)).Select(r => r.Id).ToList();

        await ContentFlow.PostAsync(owner, $"{ContentFlow.Certifications}?handler=Move&id={ids[1]}", new Dictionary<string, string> { ["direction"] = "up" }, ContentFlow.Certifications);

        Assert.Equal(["Two", "One"], (await RowsAsync(app)).Select(r => r.Name));
        var page = await app.NewClient().GetStringAsync("/certifications");
        Assert.True(page.IndexOf("data-issuer=\"issuer two\"", StringComparison.Ordinal) < page.IndexOf("data-issuer=\"issuer one\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Removing_NeedsAConfirmation_AndTheEntryLeavesThePublicSite()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        await ContentFlow.PostAsync(owner, Edit, Cert("Doomed", "Board"));
        var id = (await RowsAsync(app)).Single().Id;

        var confirm = await owner.GetStringAsync($"{ContentFlow.Certifications}/delete/{id}");
        Assert.Contains("Doomed", confirm);
        Assert.Contains("Board", confirm);
        Assert.Single(await RowsAsync(app));
        await ContentFlow.PostAsync(owner, $"{ContentFlow.Certifications}/delete/{id}", new Dictionary<string, string> { ["Stamp"] = await ContentFlow.StampAsync(owner, $"{ContentFlow.Certifications}/delete/{id}") });

        Assert.Empty(await RowsAsync(app));
        Assert.DoesNotContain("Doomed", await app.NewClient().GetStringAsync("/certifications"));
    }

    [Fact]
    public async Task AStaleCertificationForm_IsRefused()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        await ContentFlow.PostAsync(owner, Edit, Cert("Original", "Board"));
        var id = (await RowsAsync(app)).Single().Id;
        var tabOne = await ContentFlow.StampAsync(owner, $"{Edit}/{id}");
        var tabTwo = await ContentFlow.StampAsync(owner, $"{Edit}/{id}");
        var one = Cert("One", "Board");
        one["Form.Stamp"] = tabOne;
        await ContentFlow.PostAsync(owner, $"{Edit}/{id}", one);
        var two = Cert("Two", "Board");
        two["Form.Stamp"] = tabTwo;

        var refused = await ContentFlow.PostAsync(owner, $"{Edit}/{id}", two);

        Assert.Contains("changed or removed somewhere else", await refused.Content.ReadAsStringAsync());
        Assert.Equal("One", (await RowsAsync(app)).Single().Name);
    }
}
