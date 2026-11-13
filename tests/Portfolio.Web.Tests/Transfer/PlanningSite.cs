using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Portfolio.Data.Entities;
using Portfolio.Web.Services;
using Portfolio.Web.Tests.Support;
using Xunit;

namespace Portfolio.Web.Tests.Transfer;

/// <summary>
/// One software site shared by the tests that only plan (a plan writes nothing, so they cannot disturb each other): a finished blank wizard, two rows in every table, text in
/// the hero, metrics and About, two stored section settings, and one uploaded image that really exists.
/// </summary>
public sealed class PlanningSite : IAsyncLifetime
{
    public const string RealUpload = "/uploads/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.png";
    public const string MissingUpload = "/uploads/bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb.png";

    public AdminApp App { get; } = new();
    public HttpClient Owner { get; private set; } = null!;
    public ContentFlow.Seeded Rows { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Owner = await ContentFlow.BlankSiteAsync(App);
        Rows = await ContentFlow.SeedRowsAsync(App, pairs: true);
        await ProfileFlow.SeedTextAsync(App);
        await File.WriteAllBytesAsync(Path.Combine(App.UploadsPath, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.png"), TestImages.Split(SkiaSharp.SKEncodedImageFormat.Png, 8, 8));
        using var db = App.CreateDbContext();
        var p = await db.SiteProfiles.SingleAsync();
        p.Sections = [new SectionSetting { Key = "blog", Label = "Journal" }, new SectionSetting { Key = "talks", Enabled = false, Shown = false }];
        await db.SaveChangesAsync();
        App.Services.GetRequiredService<ISiteProfileProvider>().Invalidate();
    }

    public Task DisposeAsync()
    {
        App.Dispose();
        return Task.CompletedTask;
    }
}

[CollectionDefinition(Name)]
public sealed class PlanningCollection : ICollectionFixture<PlanningSite>
{
    public const string Name = "planning";
}
