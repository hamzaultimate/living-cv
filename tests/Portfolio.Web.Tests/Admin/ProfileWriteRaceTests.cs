using System.Net;
using System.Net.Http.Headers;
using Microsoft.EntityFrameworkCore;
using Portfolio.Web.Tests.Support;
using SkiaSharp;
using Xunit;

namespace Portfolio.Web.Tests.Admin;

/// <summary>A photo or template write that keeps losing to other profile writes, and a first write that races the row's creation, end as a friendly refusal, not a 500, and leave nothing behind.</summary>
public class ProfileWriteRaceTests
{
    private static int UploadedFiles(AdminApp app) => Directory.GetFiles(app.UploadsPath).Length;

    [Fact]
    public async Task APhotoUpload_ThatLosesThreeRaces_SaysSo_ChangesNothing_AndDeletesTheFileItStored()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        await owner.GetAsync("/admin");
        var files = UploadedFiles(app);
        var token = await AdminApp.GetTokenAsync(owner, "/admin/photo");
        var form = new MultipartFormDataContent { { new StringContent(token), "__RequestVerificationToken" } };
        var file = new ByteArrayContent(TestImages.Split(SKEncodedImageFormat.Png, 32, 32));
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "Photo", "me.png");

        var response = await app.OverlapAsync(() => owner.PostAsync("/admin/photo?handler=Upload", form), WizardRaceTests.Race(app, 3));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("the photo was not changed", await owner.GetStringAsync("/admin/photo"));
        Assert.Null((await ProfileFlow.ProfileAsync(app)).PhotoUrl);
        Assert.Equal(files, UploadedFiles(app)); // the stored file did not stay behind
    }

    [Fact]
    public async Task APhotoRemoval_ThatLosesThreeRaces_SaysSo_AndKeepsThePhoto()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        using (var db = app.CreateDbContext())
        {
            (await db.SiteProfiles.SingleAsync()).PhotoUrl = "/uploads/" + new string('a', 32) + ".jpg";
            await db.SaveChangesAsync();
        }
        var token = await AdminApp.GetTokenAsync(owner, "/admin/photo");

        var response = await app.OverlapAsync(() => AdminApp.PostFormAsync(owner, "/admin/photo?handler=Remove", token, new Dictionary<string, string>()), WizardRaceTests.Race(app, 3));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("the photo was not changed", await owner.GetStringAsync("/admin/photo"));
        Assert.NotNull((await ProfileFlow.ProfileAsync(app)).PhotoUrl);
    }

    [Fact]
    public async Task ATemplateSwitch_ThatLosesThreeRaces_SaysSo_AndKeepsTheTemplate()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);

        var response = await app.OverlapAsync(() => ContentFlow.PostAsync(owner, "/admin/template", new Dictionary<string, string> { ["Key"] = "healthcare" }), WizardRaceTests.Race(app, 3));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("the template was not switched", await owner.GetStringAsync("/admin/template"));
        Assert.Equal("software", (await ProfileFlow.ProfileAsync(app)).TemplateKey);
    }

    [Fact]
    public async Task AnEdit_ThatRacesTheCreationOfTheProfileRow_IsRefusedLikeAStaleForm_NotAServerError()
    {
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();
        await app.NewClient().GetAsync("/"); // the provider creates the row and keeps the profile in memory ...
        using (var db = app.CreateDbContext())
        {
            db.SiteProfiles.RemoveRange(await db.SiteProfiles.ToListAsync()); // ... so a second instance that has not seen the row yet looks just like this
            await db.SaveChangesAsync();
        }
        var stamp = await ProfileFlow.StampAsync(owner, ProfileFlow.Hero);

        // The row is created by someone else between this request's read (no row) and its insert.
        var response = await app.OverlapAsync(
            () => ProfileFlow.PostAsync(owner, ProfileFlow.Hero, ProfileFlow.HeroFields(), stamp: stamp),
            async () =>
            {
                using var db = app.CreateDbContext();
                db.SiteProfiles.Add(new Portfolio.Data.Entities.SiteProfile { OwnerName = "Created elsewhere", BrandName = "Elsewhere", Url = "https://elsewhere.example" });
                await db.SaveChangesAsync();
            });

        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{(int)response.StatusCode}");
        Assert.Contains("Nothing was saved.", await response.Content.ReadAsStringAsync());
        Assert.Equal("Created elsewhere", (await ProfileFlow.ProfileAsync(app)).OwnerName);
    }
}
