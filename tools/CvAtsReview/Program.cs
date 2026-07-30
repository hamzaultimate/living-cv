using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Anthropic;
using Anthropic.Models.Messages;
using Microsoft.Extensions.Configuration;
using Portfolio.Data.Entities;
using Portfolio.Web.Configuration;
using Portfolio.Web.Services;

var toolDir = AppContext.BaseDirectory; // bin/Debug/net10.0 under tools/CvAtsReview
var repoRoot = Path.GetFullPath(Path.Combine(toolDir, "..", "..", "..", "..", ".."));
var seedDir = Path.Combine(repoRoot, "src", "Portfolio.Data", "Seed");
var appSettingsPath = Path.Combine(repoRoot, "src", "Portfolio.Web", "appsettings.json");

var seedJsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
    Converters = { new JsonStringEnumConverter() },
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
};

var config = new ConfigurationBuilder().AddJsonFile(appSettingsPath).Build();
var site = config.GetSection("Site").Get<SiteOptions>()
    ?? throw new InvalidOperationException($"Could not read 'Site' section from {appSettingsPath}");

var experiences = (await LoadSeedAsync<Experience>(seedDir, "experiences.json")).OrderBy(e => e.SortOrder).ToList();
var skills = (await LoadSeedAsync<Skill>(seedDir, "skills.json")).OrderBy(s => s.Group).ThenBy(s => s.SortOrder).ToList();
var projects = (await LoadSeedAsync<Project>(seedDir, "projects.json")).Where(p => p.Featured).OrderBy(p => p.SortOrder).ToList();
var certifications = (await LoadSeedAsync<Certification>(seedDir, "certifications.json")).OrderBy(c => c.SortOrder).ToList();

var skillGroups = skills.GroupBy(s => s.Group)
    .OrderBy(g => { var i = Array.IndexOf(CvSkillGroupOrder.Order, g.Key); return i < 0 ? int.MaxValue : i; })
    .ToList();

var contacts = new[] { site.HasEmail ? site.Email : null, site.HasPhone ? site.Phone : null, site.HasLinkedIn ? site.LinkedIn : null, site.HasGitHub ? site.GitHub : null }
    .Where(x => !string.IsNullOrWhiteSpace(x))
    .Select(x => x!)
    .ToList();

var cvData = new CvData(site.OwnerName, site.Role, site.Summary, contacts, experiences, skillGroups, projects, certifications, site.Url);

var ruleReport = CvAtsRuleScorer.Score(cvData);
Console.WriteLine($"Rule score: {ruleReport.Score}/100");

AiReviewResult? aiResult = null;
var apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
if (string.IsNullOrWhiteSpace(apiKey))
{
    Console.WriteLine("No ANTHROPIC_API_KEY set — writing a rule-based-only report (no AI review).");
}
else
{
    try
    {
        AnthropicClient client = new();

        var schema = new Dictionary<string, JsonElement>
        {
            ["type"] = JsonSerializer.SerializeToElement("object"),
            ["properties"] = JsonSerializer.SerializeToElement(new
            {
                score = new { type = "integer" },
                strengths = new { type = "array", items = new { type = "string" } },
                critiques = new { type = "array", items = new { type = "string" } },
            }),
            ["required"] = JsonSerializer.SerializeToElement(new[] { "score", "strengths", "critiques" }),
            ["additionalProperties"] = JsonSerializer.SerializeToElement(false),
        };

        var response = await client.Messages.Create(new MessageCreateParams
        {
            Model = "claude-opus-5",
            MaxTokens = 2048,
            System = "You are an expert resume reviewer assessing impact and clarity for a software engineering CV — not ATS keyword matching, which is scored separately. Be specific and concrete; ground every critique in the actual text provided.",
            OutputConfig = new OutputConfig { Format = new JsonOutputFormat { Schema = schema } },
            Messages = [ new() { Role = Role.User, Content = BuildReviewPrompt(cvData) } ],
        });

        var textBlock = response.Content.Select(b => b.Value).OfType<TextBlock>().First();
        aiResult = JsonSerializer.Deserialize<AiReviewResult>(textBlock.Text, seedJsonOptions)
            ?? throw new InvalidOperationException("Claude returned an empty or unparsable review.");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Claude review failed: {ex.Message}");
        return 1;
    }

    Console.WriteLine($"AI score: {aiResult.Score}/100");
}

var combinedScore = aiResult is null
    ? ruleReport.Score
    : (int)Math.Round(0.5 * ruleReport.Score + 0.5 * aiResult.Score);

var report = new CvAtsReport(
    ruleReport.Score,
    aiResult?.Score,
    combinedScore,
    ruleReport.Findings,
    aiResult?.Strengths ?? new List<string>(),
    aiResult?.Critiques ?? new List<string>(),
    DateTimeOffset.UtcNow);

var outputPath = Path.Combine(seedDir, "ats-report.json");
await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(report, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));

if (aiResult is null)
{
    Console.WriteLine($"Rule-only combined score: {combinedScore}/100");
}
else
{
    Console.WriteLine($"Combined score: {combinedScore}/100");
}
Console.WriteLine($"Wrote {outputPath}");
return 0;

static async Task<List<T>> LoadSeedAsync<T>(string seedDir, string fileName)
{
    var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };
    var path = Path.Combine(seedDir, fileName);
    await using var stream = File.OpenRead(path);
    return await JsonSerializer.DeserializeAsync<List<T>>(stream, jsonOptions) ?? new List<T>();
}

static string BuildReviewPrompt(CvData data)
{
    var sb = new StringBuilder();
    sb.AppendLine($"Name: {data.Name}");
    sb.AppendLine($"Title: {data.Title}");
    sb.AppendLine($"Summary: {data.Summary}");
    sb.AppendLine();
    sb.AppendLine("Experience:");
    foreach (var e in data.Experience)
    {
        sb.AppendLine($"- {e.Company} — {e.Title} ({e.Start} to {(e.Current ? "present" : e.End)})");
        foreach (var h in e.Highlights)
            sb.AppendLine($"  * {h}");
    }
    sb.AppendLine();
    sb.AppendLine("Projects:");
    foreach (var p in data.Projects)
    {
        sb.AppendLine($"- {p.Title}: {p.Summary}");
        foreach (var i in p.Impact)
            sb.AppendLine($"  * {i}");
    }
    sb.AppendLine();
    sb.AppendLine("Give a 0-100 score for impact and clarity, up to 3 strengths, and up to 5 concrete critiques.");
    return sb.ToString();
}

record AiReviewResult(int Score, List<string> Strengths, List<string> Critiques);
