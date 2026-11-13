using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Portfolio.Web.Transfer;

namespace Portfolio.Web.Tests.Support;

/// <summary>Drives the export and import over HTTP the way a browser would.</summary>
public static class TransferFlow
{
    public const string Page = "/admin/transfer";
    public const string Export = "/admin/transfer/export";

    /// <summary>The export file as the owner downloads it.</summary>
    public static async Task<string> ExportTextAsync(HttpClient client)
    {
        var response = await client.GetAsync(Export);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    public static async Task<JsonDocument> ExportJsonAsync(HttpClient client) => JsonDocument.Parse(await ExportTextAsync(client));

    public static byte[] Utf8(string text) => new UTF8Encoding(false).GetBytes(text);
}

/// <summary>Builds import files and runs the planner directly (no HTTP), so rules can be checked row by row.</summary>
public static class ImportHarness
{
    private static readonly JsonSerializerOptions Camel = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never };

    /// <summary>An export file with the given parts (anonymous objects or dictionaries), schema version 1.</summary>
    public static string File(object? profile = null, object? content = null, int? version = 1, IDictionary<string, object?>? extra = null)
    {
        var root = new Dictionary<string, object?>();
        if (version is not null) root["schemaVersion"] = version;
        root["format"] = "living-cv-export";
        if (profile is not null) root["profile"] = profile;
        if (content is not null) root["content"] = content;
        if (extra is not null) foreach (var (k, v) in extra) root[k] = v;
        return JsonSerializer.Serialize(root, Camel);
    }

    public static string Json(object value) => JsonSerializer.Serialize(value, Camel);

    /// <summary>A file with one table: <c>Table("skills", new { name = "x", group = "y" })</c>.</summary>
    public static string Table(string table, params object[] rows) => File(content: new Dictionary<string, object> { [table] = rows });

    public static async Task<PlanOutcome> PlanAsync(AdminApp app, string json, bool applyProfile = false, string session = "test-session")
    {
        using var scope = app.Services.CreateScope();
        using var read = ImportFileReader.Parse(TransferFlow.Utf8(json));
        Assert.True(read.Refusal is null, read.Refusal);
        return await scope.ServiceProvider.GetRequiredService<ImportPlanner>().PlanAsync(read.Document!.RootElement, "test.json", applyProfile, session, CancellationToken.None);
    }

    /// <summary>The plan of a file that is expected to be read.</summary>
    public static async Task<ImportPlan> PlanOkAsync(AdminApp app, string json, bool applyProfile = false)
    {
        var outcome = await PlanAsync(app, json, applyProfile);
        Assert.True(outcome.Plan is not null, outcome.Refusal);
        return outcome.Plan!;
    }

    public static SectionReport Section(ImportPlan plan, string section) => plan.Reports.Single(r => r.Section == section);

    /// <summary>The one row of a table in a plan of a one-row file.</summary>
    public static RowReport OneRow(ImportPlan plan, string section) => Assert.Single(Section(plan, section).Rows);

    public static string Why(RowReport row) => string.Join(" | ", row.Reasons);
}
