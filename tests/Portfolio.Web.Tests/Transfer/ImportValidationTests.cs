using Portfolio.Web.Tests.Support;
using Portfolio.Web.Transfer;
using Xunit;

namespace Portfolio.Web.Tests.Transfer;

/// <summary>Imported text is untrusted: every field is held to the rules of the editor for the same field, whatever the file says.</summary>
[Collection(PlanningCollection.Name)]
public class ImportValidationTests
{
    private readonly PlanningSite _site;

    public ImportValidationTests(PlanningSite site) => _site = site;

    private AdminApp App => _site.App;

    /// <summary>The smallest row of a table that is accepted as new.</summary>
    private static Dictionary<string, object?> Minimal(string table) => table switch
    {
        "experience" => new() { ["company"] = "Probe Co", ["title"] = "Probe Role", ["start"] = "2020", ["end"] = "2021", ["summary"] = "Did probe things." },
        "skills" => new() { ["name"] = "Probe skill", ["group"] = "Probe group" },
        "projects" => new() { ["title"] = "Probe project", ["summary"] = "A probe." },
        "certifications" => new() { ["name"] = "Probe cert", ["issuer"] = "Probe board", ["status"] = "Earned" },
        "talks" => new() { ["title"] = "Probe talk" },
        "testimonials" => new() { ["quote"] = "A probe quote, long enough.", ["author"] = "Probe Person" },
        "blog" => new() { ["title"] = "Probe post", ["date"] = "2025-01-01", ["body"] = "Probe text." },
        _ => throw new ArgumentOutOfRangeException(nameof(table)),
    };

    private async Task<RowReport> PlanRowAsync(string table, Action<Dictionary<string, object?>> change)
    {
        var row = Minimal(table);
        change(row);
        return ImportHarness.OneRow(await ImportHarness.PlanOkAsync(App, ImportHarness.Table(table, row)), table);
    }

    [Theory]
    [InlineData("experience")]
    [InlineData("skills")]
    [InlineData("projects")]
    [InlineData("certifications")]
    [InlineData("talks")]
    [InlineData("testimonials")]
    [InlineData("blog")]
    public async Task TheMinimalRowOfEachTable_IsAccepted_SoTheRefusalsBelowAreCausedByTheValueUnderTest(string table)
    {
        var row = await PlanRowAsync(table, _ => { });

        Assert.True(row.Status == RowStatus.Create, ImportHarness.Why(row));
    }

    // (table, field, kind, longest accepted length). Kind: line = single-line plain text, para = multi-line plain text, list = entries of single-line plain text.
    public static IEnumerable<object[]> TextFields() =>
    [
        ["experience", "company", "line", 200], ["experience", "title", "line", 200], ["experience", "start", "line", 30], ["experience", "summary", "para", 1500],
        ["experience", "highlights", "list", 300], ["experience", "tech", "list", 60],
        ["skills", "name", "line", 100], ["skills", "group", "line", 60],
        ["projects", "title", "line", 200], ["projects", "summary", "para", 600], ["projects", "role", "line", 200], ["projects", "org", "line", 200], ["projects", "period", "line", 60],
        ["projects", "category", "line", 80], ["projects", "problem", "para", 3000], ["projects", "approach", "para", 3000], ["projects", "impact", "list", 200], ["projects", "tech", "list", 60],
        ["projects", "heroImageAlt", "line", 300],
        ["certifications", "name", "line", 200], ["certifications", "issuer", "line", 160], ["certifications", "category", "line", 80],
        ["talks", "title", "line", 200], ["talks", "event", "line", 200],
        ["testimonials", "quote", "para", 2000], ["testimonials", "author", "line", 120], ["testimonials", "authorTitle", "line", 200], ["testimonials", "relation", "line", 200],
        ["blog", "title", "line", 200], ["blog", "description", "line", 300], ["blog", "coverAlt", "line", 300],
    ];

    private static object Value(string kind, string text) => kind == "list" ? new[] { text } : text;

    private static string Fill(int length) => new string('a', length);

    public static IEnumerable<object[]> Markup() =>
        from field in TextFields()
        from text in new[] { "<b>bold</b>", "<!-- comment -->", "<script>alert(1)</script>", "<?php echo 1 ?>", "<!DOCTYPE html>", "a </div> b", "<img src=x onerror=alert(1)>" }
        select new[] { field[0], field[1], field[2], text };

    [Theory]
    [MemberData(nameof(Markup))]
    public async Task TextThatLooksLikeMarkup_IsRefused_InEveryTextField(string table, string field, string kind, string text)
    {
        var row = await PlanRowAsync(table, r => r[field] = Value(kind, text));

        Assert.Equal(RowStatus.Refused, row.Status);
        Assert.Contains("plain text", ImportHarness.Why(row));
    }

    public static IEnumerable<object[]> ControlCharacters() =>
        from field in TextFields()
        from text in new[] { "a\u0000b", "a\u0007b", "a\u001Fb", "a\u007Fb", "a\u0085b" }
        select new[] { field[0], field[1], field[2], text };

    [Theory]
    [MemberData(nameof(ControlCharacters))]
    public async Task ControlCharacters_AreRefused_InEveryTextField(string table, string field, string kind, string text)
    {
        var row = await PlanRowAsync(table, r => r[field] = Value(kind, text));

        Assert.Equal(RowStatus.Refused, row.Status);
        Assert.Contains("characters that are not allowed", ImportHarness.Why(row));
    }

    [Theory]
    [MemberData(nameof(TextFields))]
    public async Task TheLimitOfEachField_IsTheEditors_OneOverIsRefused_TheLimitItselfIsAccepted(string table, string field, string kind, int max)
    {
        var atLimit = await PlanRowAsync(table, r => r[field] = Value(kind, Fill(max)));
        var over = await PlanRowAsync(table, r => r[field] = Value(kind, Fill(max + 1)));

        // A date-like field also has to parse, so a run of letters is refused for its format rather than accepted: for those the limit is checked by the message instead.
        if (field == "start") Assert.Contains("year", ImportHarness.Why(atLimit));
        else Assert.True(atLimit.Status == RowStatus.Create, $"{table}.{field} at {max}: {ImportHarness.Why(atLimit)}");
        Assert.Equal(RowStatus.Refused, over.Status);
        Assert.Contains($"at most {max}", ImportHarness.Why(over));
    }

    [Theory]
    [InlineData("experience", "company", "line")]
    [InlineData("skills", "name", "line")]
    [InlineData("projects", "title", "line")]
    [InlineData("testimonials", "author", "line")]
    [InlineData("blog", "title", "line")]
    [InlineData("projects", "impact", "list")]
    public async Task ALineBreak_IsRefusedInASingleLineField_ButMultiLineFieldsKeepTheirs(string table, string field, string kind)
    {
        var refused = await PlanRowAsync(table, r => r[field] = Value(kind, "first line\nsecond line"));
        var allowed = await PlanRowAsync("projects", r => r["problem"] = "first line\n\nsecond paragraph");

        Assert.Equal(RowStatus.Refused, refused.Status);
        Assert.Equal(RowStatus.Create, allowed.Status);
    }

    [Fact]
    public async Task APlainBareLessThan_AndScriptLookingText_AreOrdinaryText_NotRefused()
    {
        // The same as the editors: a "<" that does not start a tag is fine, and "javascript:" in plain text is only text (it is refused where an address is expected).
        foreach (var text in new[] { "latency <50ms & 5 < 6", "javascript:alert(1) is just words here", "{{ template }} ${x} %s", "'; DROP TABLE Skills; --" })
        {
            var row = await PlanRowAsync("projects", r => r["period"] = text);

            Assert.True(row.Status == RowStatus.Create, $"{text}: {ImportHarness.Why(row)}");
        }
    }

    [Fact]
    public async Task WrongTypes_AreRefused_WithAnAnswerThatNamesTheProblem()
    {
        foreach (var bad in new object?[] { 5, true, new { a = 1 }, new[] { "x" } })
        {
            var row = await PlanRowAsync("skills", r => r["name"] = bad);
            Assert.Equal(RowStatus.Refused, row.Status);
            Assert.Contains("must be text", ImportHarness.Why(row));
        }
        var flag = await PlanRowAsync("projects", r => r["featured"] = "yes");
        Assert.Contains("true or false", ImportHarness.Why(flag));
        var list = await PlanRowAsync("projects", r => r["tech"] = "one, two");
        Assert.Contains("must be a list", ImportHarness.Why(list));
        var listOfNumbers = await PlanRowAsync("projects", r => r["tech"] = new[] { 1, 2 });
        Assert.Contains("text entries", ImportHarness.Why(listOfNumbers));
        var links = await PlanRowAsync("projects", r => r["links"] = new[] { "https://example.com" });
        Assert.Contains("each with its own fields", ImportHarness.Why(links));
    }

    // ---- addresses ----

    private static readonly string[] BadAddresses =
    [
        "javascript:alert(1)", "JaVaScRiPt:alert(1)", "java\tscript:alert(1)", "data:text/html,<script>alert(1)</script>", "vbscript:x", "//evil.example/x", "ftp://example.com/x",
        "file:///etc/passwd", "http://user:secret@example.com/", "https:///nohost", "/etc/passwd", "/admin", "/img/../admin", "/img/x y.png", "\\\\server\\share", "mailto:a@b.example", "example.com/page", "",
    ];

    public static IEnumerable<object[]> AddressFields() =>
    [
        ["certifications", "credentialUrl"], ["certifications", "certificateUrl"], ["certifications", "badgeImageUrl"], ["talks", "url"], ["testimonials", "sourceUrl"],
    ];

    [Theory]
    [MemberData(nameof(AddressFields))]
    public async Task OnlyWebAddresses_AndTheSitesOwnBundledFiles_AreAcceptedInAnAddressField(string table, string field)
    {
        foreach (var bad in BadAddresses.Where(a => a.Length > 0))
        {
            var row = await PlanRowAsync(table, r => r[field] = bad);
            Assert.True(row.Status == RowStatus.Refused, $"{table}.{field} accepted {bad}");
        }
        foreach (var good in new[] { "https://example.com/x?y=1#z", "http://example.com", "/img/badges/badge.svg", "/proofs/report.svg" })
        {
            var row = await PlanRowAsync(table, r => r[field] = good);
            Assert.True(row.Status == RowStatus.Create, $"{table}.{field} refused {good}: {ImportHarness.Why(row)}");
        }
        var empty = await PlanRowAsync(table, r => r[field] = "");
        Assert.Equal(RowStatus.Create, empty.Status); // empty means no address
    }

    [Fact]
    public async Task LinkAndProofAddresses_FollowTheSameRules_AndEveryBadEntryIsReported()
    {
        foreach (var bad in BadAddresses.Where(a => a.Length > 0))
        {
            var link = await PlanRowAsync("projects", r => r["links"] = new[] { new { label = "L", url = bad } });
            var proof = await PlanRowAsync("projects", r => r["proofs"] = new[] { new { label = "P", url = bad, kind = "Link" } });
            var certProof = await PlanRowAsync("certifications", r => r["proofs"] = new[] { new { label = "P", url = bad } });

            Assert.True(link.Status == RowStatus.Refused, $"link {bad}");
            Assert.True(proof.Status == RowStatus.Refused, $"proof {bad}");
            Assert.True(certProof.Status == RowStatus.Refused, $"certificate proof {bad}");
        }
        var good = await PlanRowAsync("projects", r =>
        {
            r["links"] = new[] { new { label = "Code", url = "https://example.com/code" } };
            r["proofs"] = new[] { new { label = "Report", url = "/proofs/report.svg", kind = "Pdf" } };
        });
        Assert.True(good.Status == RowStatus.Create, ImportHarness.Why(good));

        var noLabel = await PlanRowAsync("projects", r => r["links"] = new[] { new { label = "", url = "https://example.com" } });
        Assert.Contains("Give a label for link 1", ImportHarness.Why(noLabel));
        var tooMany = await PlanRowAsync("projects", r => r["links"] = Enumerable.Range(0, 21).Select(i => new { label = "L" + i, url = "https://example.com/" + i }).ToArray());
        Assert.Contains("at most 20", ImportHarness.Why(tooMany));
        var badKind = await PlanRowAsync("projects", r => r["proofs"] = new[] { new { label = "P", url = "https://example.com", kind = "Video" } });
        Assert.Contains("choose one of", ImportHarness.Why(badKind));
    }

    // ---- images ----

    public static IEnumerable<object[]> ImageFields() => [["projects", "heroImage"], ["blog", "cover"]];

    [Theory]
    [MemberData(nameof(ImageFields))]
    public async Task AnUploadedFile_IsKeptOnlyIfThatExactFileIsHere_OtherwiseTheReferenceIsClearedWithAWarning(string table, string field)
    {
        var kept = await PlanRowAsync(table, r => r[field] = PlanningSite.RealUpload);
        Assert.Equal(RowStatus.Create, kept.Status);
        Assert.Empty(kept.Warnings);

        foreach (var missing in new[] { PlanningSite.MissingUpload, "/uploads/../secret.png", "/uploads/not-a-generated-name.png", "/uploads/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.gif", "/uploads/.drafts/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.png", "/uploads/" })
        {
            var row = await PlanRowAsync(table, r => r[field] = missing);

            Assert.True(row.Status == RowStatus.Create, $"{missing}: {ImportHarness.Why(row)}"); // the row still imports
            var warning = Assert.Single(row.Warnings);
            Assert.Contains("not on this site", warning);
            Assert.DoesNotContain("secret", warning);
        }
    }

    [Theory]
    [MemberData(nameof(ImageFields))]
    public async Task ImageAddresses_AreWebAddressesBundledFilesOrHereUploads_NothingElse(string table, string field)
    {
        foreach (var bad in BadAddresses.Where(a => a.Length > 0))
        {
            var row = await PlanRowAsync(table, r => r[field] = bad);
            Assert.True(row.Status == RowStatus.Refused, $"{table}.{field} accepted {bad}");
        }
        foreach (var good in new[] { "https://example.com/p.png", "/img/projects/telemetry.svg" })
        {
            var row = await PlanRowAsync(table, r => r[field] = good);
            Assert.True(row.Status == RowStatus.Create && row.Warnings.Count == 0, $"{good}: {ImportHarness.Why(row)}");
        }
    }

    [Fact]
    public async Task GalleryImages_NeedADescription_AreCapped_AndAMissingUploadIsDroppedAlone()
    {
        var ok = await PlanRowAsync("projects", r => r["screenshots"] = new[] { new { url = PlanningSite.RealUpload, caption = "A real picture" }, new { url = "/img/projects/telemetry-1.svg", caption = "Bundled" } });
        Assert.Equal(RowStatus.Create, ok.Status);

        var noCaption = await PlanRowAsync("projects", r => r["screenshots"] = new[] { new { url = "/img/projects/telemetry-1.svg", caption = "" } });
        Assert.Contains("needs a description", ImportHarness.Why(noCaption));
        var markup = await PlanRowAsync("projects", r => r["screenshots"] = new[] { new { url = "/img/projects/telemetry-1.svg", caption = "<b>x</b>" } });
        Assert.Contains("plain text", ImportHarness.Why(markup));
        var tooMany = await PlanRowAsync("projects", r => r["screenshots"] = Enumerable.Range(0, 13).Select(i => new { url = "/img/projects/telemetry-1.svg", caption = "Image " + i }).ToArray());
        Assert.Contains("at most 12", ImportHarness.Why(tooMany));
        var gone = await PlanRowAsync("projects", r => r["screenshots"] = new[] { new { url = PlanningSite.MissingUpload, caption = "Gone" }, new { url = PlanningSite.RealUpload, caption = "Here" } });
        Assert.Equal(RowStatus.Create, gone.Status);
        Assert.Single(gone.Warnings);
    }

    // ---- the rules of a single table that the editors have ----

    [Fact]
    public async Task Roles_FollowTheEditorsDateAndPipeRules()
    {
        Assert.Contains("year or a month and year", ImportHarness.Why(await PlanRowAsync("experience", r => r["start"] = "last spring")));
        Assert.Contains("cannot be earlier", ImportHarness.Why(await PlanRowAsync("experience", r => { r["start"] = "2022"; r["end"] = "2021"; })));
        Assert.Contains("year or a month and year", ImportHarness.Why(await PlanRowAsync("experience", r => r["end"] = "soon")));
        Assert.Contains("cannot contain the |", ImportHarness.Why(await PlanRowAsync("experience", r => r["company"] = "A|B")));
        Assert.Contains("cannot contain the |", ImportHarness.Why(await PlanRowAsync("experience", r => r["title"] = "A|B")));
        Assert.Contains("Give an", ImportHarness.Why(await PlanRowAsync("experience", r => r["end"] = null)));
        Assert.Equal(RowStatus.Create, (await PlanRowAsync("experience", r => { r["end"] = null; r["current"] = true; })).Status);
        Assert.Equal(RowStatus.Create, (await PlanRowAsync("experience", r => { r["start"] = "Mar 2021"; r["end"] = "2021-03"; })).Status);
        Assert.Contains("is required", ImportHarness.Why(await PlanRowAsync("experience", r => r.Remove("summary"))));
        Assert.Contains("at most 12", ImportHarness.Why(await PlanRowAsync("experience", r => r["highlights"] = Enumerable.Range(0, 13).Select(i => "h" + i).ToArray())));
        Assert.Contains("at most 20", ImportHarness.Why(await PlanRowAsync("experience", r => r["tech"] = Enumerable.Range(0, 21).Select(i => "t" + i).ToArray())));
    }

    [Fact]
    public async Task Certifications_FollowTheEditorsStatusAndDateRules()
    {
        Assert.Contains("Choose one of", ImportHarness.Why(await PlanRowAsync("certifications", r => r["status"] = "Pending")));
        Assert.Contains("Choose one of", ImportHarness.Why(await PlanRowAsync("certifications", r => r["status"] = "1")));
        Assert.Contains("is required", ImportHarness.Why(await PlanRowAsync("certifications", r => r.Remove("status"))));
        Assert.Equal(RowStatus.Create, (await PlanRowAsync("certifications", r => r["status"] = "inprogress")).Status); // any casing of a real name
        Assert.Contains("in the future", ImportHarness.Why(await PlanRowAsync("certifications", r => r["date"] = "2099")));
        Assert.Equal(RowStatus.Create, (await PlanRowAsync("certifications", r => { r["date"] = "2099"; r["status"] = "Planned"; })).Status);
        Assert.Contains("year or a month and year", ImportHarness.Why(await PlanRowAsync("certifications", r => r["date"] = "someday")));
    }

    [Fact]
    public async Task Talks_FollowTheEditorsTypeDateAndVideoRules()
    {
        Assert.Contains("Choose one of", ImportHarness.Why(await PlanRowAsync("talks", r => r["type"] = "Podcast")));
        Assert.Contains("must be a date", ImportHarness.Why(await PlanRowAsync("talks", r => r["date"] = "yesterday")));
        Assert.Equal(RowStatus.Create, (await PlanRowAsync("talks", r => { r["date"] = "2025"; r["type"] = "talk"; })).Status);
        Assert.Contains("YouTube link", ImportHarness.Why(await PlanRowAsync("talks", r => r["youtubeId"] = "javascript:alert(1)")));
        Assert.Contains("YouTube link", ImportHarness.Why(await PlanRowAsync("talks", r => r["youtubeId"] = "abc\"onload=x")));
        var id = await PlanRowAsync("talks", r => r["youtubeId"] = "https://www.youtube.com/watch?v=dQw4w9WgXcQ");
        Assert.Equal(RowStatus.Create, id.Status);
        Assert.Equal(RowStatus.Create, (await PlanRowAsync("talks", r => r["youtubeId"] = "dQw4w9WgXcQ")).Status);
    }

    [Fact]
    public async Task Projects_FollowTheEditorsAddressNameRules()
    {
        Assert.Contains("lower-case letters", ImportHarness.Why(await PlanRowAsync("projects", r => r["slug"] = "Not A Slug!")));
        Assert.Contains("lower-case letters", ImportHarness.Why(await PlanRowAsync("projects", r => r["slug"] = "a--b")));
        Assert.Contains("lower-case letters", ImportHarness.Why(await PlanRowAsync("projects", r => r["slug"] = "../admin")));
        Assert.Contains("reserved", ImportHarness.Why(await PlanRowAsync("projects", r => r["slug"] = "new")));
        Assert.Contains("reserved", ImportHarness.Why(await PlanRowAsync("projects", r => r["slug"] = "ADMIN")));
        Assert.Contains("at most 120", ImportHarness.Why(await PlanRowAsync("projects", r => r["slug"] = new string('a', 121))));
        Assert.Equal(RowStatus.Create, (await PlanRowAsync("projects", r => r["slug"] = "My-Project-2")).Status); // lower-cased like the editor
        Assert.Contains("at most 8", ImportHarness.Why(await PlanRowAsync("projects", r => r["impact"] = Enumerable.Range(0, 9).Select(i => "i" + i).ToArray())));
    }

    [Fact]
    public async Task Testimonials_AndPosts_FollowTheirEditorsToo()
    {
        Assert.Contains("is required", ImportHarness.Why(await PlanRowAsync("testimonials", r => r.Remove("author"))));
        Assert.Contains("must be a date written as year-month-day", ImportHarness.Why(await PlanRowAsync("blog", r => r["date"] = "2025-13-45")));
        Assert.Contains("must be a date written as year-month-day", ImportHarness.Why(await PlanRowAsync("blog", r => r["date"] = "1980-01-01")));
        Assert.Contains("must be a date written as year-month-day", ImportHarness.Why(await PlanRowAsync("blog", r => r["date"] = "15/09/2025")));
        Assert.Contains("at most 8", ImportHarness.Why(await PlanRowAsync("blog", r => r["tags"] = Enumerable.Range(0, 9).Select(i => "tag" + i).ToArray())));
        Assert.Contains("each can be at most 30", ImportHarness.Why(await PlanRowAsync("blog", r => r["tags"] = new[] { new string('t', 31) })));
        Assert.Contains("has something else", ImportHarness.Why(await PlanRowAsync("blog", r => r["tags"] = new[] { "ok", "bad;tag" })));
        Assert.Equal(RowStatus.Create, (await PlanRowAsync("blog", r => r["tags"] = new[] { "C#", "dot.net", "Tag", "tag" })).Status); // the duplicate is dropped
        Assert.Contains("reserved", ImportHarness.Why(await PlanRowAsync("blog", r => r["slug"] = "edit")));
    }

    // ---- Markdown posts ----

    [Fact]
    public async Task PostText_IsHeldToTheSafeRendererLimits_AndMarkupInItIsStillOnlyText()
    {
        Assert.Equal(RowStatus.Create, (await PlanRowAsync("blog", r => r["body"] = new string('b', 60_000))).Status);
        Assert.Contains("at most 60,000", ImportHarness.Why(await PlanRowAsync("blog", r => r["body"] = new string('b', 60_001))));
        Assert.Contains("more than 200 headings", ImportHarness.Why(await PlanRowAsync("blog", r => r["body"] = string.Concat(Enumerable.Repeat("# Heading\n\n", 201)))));
        Assert.Equal(RowStatus.Create, (await PlanRowAsync("blog", r => r["body"] = string.Concat(Enumerable.Repeat("# Heading\n\n", 200)))).Status);
        Assert.Contains("more than 1,000 opening square brackets", ImportHarness.Why(await PlanRowAsync("blog", r => r["body"] = string.Concat(Enumerable.Repeat("[a] ", 1001)))));
        Assert.Equal(RowStatus.Create, (await PlanRowAsync("blog", r => r["body"] = string.Concat(Enumerable.Repeat("[a] ", 1000)))).Status);
        Assert.Contains("characters that are not allowed", ImportHarness.Why(await PlanRowAsync("blog", r => r["body"] = "text\u0000more")));
        Assert.Contains("is required", ImportHarness.Why(await PlanRowAsync("blog", r => r["body"] = "   \n  ")));
        Assert.Contains("is required", ImportHarness.Why(await PlanRowAsync("blog", r => r.Remove("body"))));
        Assert.Contains("must be text", ImportHarness.Why(await PlanRowAsync("blog", r => r["body"] = new[] { "a" })));
        // Tags in Markdown are legal: the renderer shows them as text. They are neither refused nor run.
        var withTags = await PlanRowAsync("blog", r => r["body"] = "<script>alert(1)</script> [x](javascript:alert(1)) <img src=x onerror=alert(1)>");
        Assert.Equal(RowStatus.Create, withTags.Status);
    }

    [Fact]
    public async Task AHugeString_IsRefusedByTheFieldLimit_NotAcceptedBecauseTheFileIsSmallEnough()
    {
        var row = await PlanRowAsync("skills", r => r["name"] = new string('x', 1_500_000));

        Assert.Equal(RowStatus.Refused, row.Status);
        Assert.Contains("at most 100", ImportHarness.Why(row));
    }

    [Fact]
    public async Task ARefusedRow_ListsEveryReason_NotJustTheFirst()
    {
        var row = await PlanRowAsync("projects", r =>
        {
            r["title"] = "<b>x</b>";
            r["summary"] = "";
            r["links"] = new[] { new { label = "", url = "javascript:alert(1)" } };
            r["heroImage"] = "data:image/png;base64,AAAA";
        });

        Assert.Equal(RowStatus.Refused, row.Status);
        Assert.True(row.Reasons.Count >= 4, ImportHarness.Why(row));
    }
}
