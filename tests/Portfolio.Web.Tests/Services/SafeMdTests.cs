using System.Text.Json;
using System.Text.RegularExpressions;
using Markdig;
using Portfolio.Web.Services;
using Xunit;

namespace Portfolio.Web.Tests.Services;

/// <summary>The renderer for editor-written Markdown: identical to the trusted renderer for the shipped posts, and unable to emit script or an unsafe address.</summary>
public class SafeMdTests
{
    private static IEnumerable<string> SeedBodies(string file)
    {
        var text = Regex.Replace(File.ReadAllText(file), @"^\s*//.*$", "", RegexOptions.Multiline);
        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.EnumerateArray().Select(p => p.GetProperty("bodyMarkdown").GetString()!).ToList();
    }

    // The renderer the site used before posts could be edited: Markdig's advanced extensions with raw HTML allowed. Only the shipped, trusted posts are compared with it.
    private static readonly Markdig.MarkdownPipeline Trusted = new Markdig.MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

    [Fact]
    public void EveryShippedPostBody_RendersByteForByte_AsItDidUnderTheTrustedRenderer()
    {
        var bodies = SeedBodies(Path.Combine(AppContext.BaseDirectory, "Seed", "blog.json"));
        Assert.True(bodies.Count() >= 3);
        foreach (var body in bodies) Assert.Equal(Markdig.Markdown.ToHtml(body, Trusted), SafeMd.ToHtml(body));
    }

    [Fact]
    public void ThePlainFormatting_TheEditorOffers_Renders()
    {
        var html = SafeMd.ToHtml("## Heading\n\nSome **bold**, *italic*, `code` and ~~gone~~.\n\n- one\n- two\n\n1. a\n2. b\n\n> quote\n\n```csharp\nvar x = 1 < 2;\n```\n\n| a | b |\n|---|---|\n| 1 | 2 |\n\n[site](https://example.org/a?b=1&c=2) and [mail](mailto:me@example.org) and [here](/blog/x) and [top](#top)");
        Assert.Contains("<h2 id=\"heading\">Heading</h2>", html);
        Assert.Contains("<strong>bold</strong>", html);
        Assert.Contains("<del>gone</del>", html);
        Assert.Contains("<table>", html);
        Assert.Contains("language-csharp", html);
        Assert.Contains("var x = 1 &lt; 2;", html);
        Assert.Contains("href=\"https://example.org/a?b=1&amp;c=2\"", html);
        Assert.Contains("rel=\"noopener nofollow ugc\"", html);
        Assert.Contains("href=\"mailto:me@example.org\"", html);
        Assert.Contains("href=\"/blog/x\"", html);
        Assert.Contains("href=\"#top\"", html);
    }

    public static IEnumerable<object[]> Attacks() => new[]
    {
        "<script>alert(1)</script>",
        "<img src=x onerror=alert(1)>",
        "<IMG SRC=x OnError=alert(1)>",
        "<svg onload=alert(1)>",
        "<iframe src=\"https://evil.example\"></iframe>",
        "<style>body{display:none}</style>",
        "<a href=\"javascript:alert(1)\">x</a>",
        "<div onclick=\"alert(1)\">x</div>",
        "<!-- <script>alert(1)</script> -->",
        "<details open ontoggle=alert(1)>",
        "[x](javascript:alert(1))",
        "[x](JaVaScRiPt:alert(1))",
        "[x](java\tscript:alert(1))",
        "[x](&#106;avascript:alert(1))",
        "[x](&#x6A;&#x61;vascript:alert(1))",
        "[x](jav&#x09;ascript:alert(1))",
        "[x](&#0000106avascript:alert(1))",
        "[x](  javascript:alert(1))",
        "[x](<javascript:alert(1)>)",
        "[x](vbscript:msgbox(1))",
        "[x](data:text/html;base64,PHNjcmlwdD5hbGVydCgxKTwvc2NyaXB0Pg==)",
        "[x](data:text/html,<script>alert(1)</script>)",
        "[x](file:///etc/passwd)",
        "[x][ref]\n\n[ref]: javascript:alert(1)",
        "[x][ref]\n\n[ref]: JAVASCRIPT:alert(1) \"t\"",
        "<javascript:alert(1)>",
        "<JAVASCRIPT:alert(1)>",
        "![x](javascript:alert(1))",
        "![x](data:image/svg+xml;base64,PHN2ZyBvbmxvYWQ9YWxlcnQoMSk+)",
        "![x](data:text/html,x)",
        "![x](mailto:a@b.c)",
        "![x](x\" onerror=\"alert(1))",
        "![x](https://example.org/a.png \"t\" onerror=\"alert(1)\")",
        "[**bold <script>alert(1)</script>**](javascript:alert(1))",
        "[`code`](javascript:alert(1))",
        "| a |\n|---|\n| <img src=x onerror=alert(1)> |",
        "| a |\n|---|\n| [x](javascript:alert(1)) |",
        "text[^1]\n\n[^1]: <script>alert(1)</script>",
        "# <script>alert(1)</script>",
        "[x]{onclick=alert(1)}",
        "**bold**{onclick=alert(1)}",
        "# Heading {onclick=alert(1)}",
        "[x](https://example.org){onmouseover=alert(1)}",
        "![x](https://example.org/a.png){onerror=alert(1)}",
        "::: warning {onclick=alert(1)}\nbody\n:::",
        "```\"><script>alert(1)</script>\ncode\n```",
        "```js onclick=alert(1)\ncode\n```",
        "&lt;script&gt;alert(1)&lt;/script&gt;",
        "<<script>script>alert(1)</script>",
        "<scr<script>ipt>alert(1)</script>",
        "[a](https://example.org \"x\" onclick=\"alert(1)\")",
        "https://example.org/\"onmouseover=\"alert(1)",
        "www.example.org/\"onmouseover=\"alert(1)",
        "<https://example.org/\"onmouseover=\"alert(1)>",
        "$$\\href{javascript:alert(1)}{x}$$",
        "```mermaid\ngraph TD; A-->B\n```",
        "<video src=x onerror=alert(1)>",
        "<math><mtext></mtext></math>",
    }.Select(a => new object[] { a });

    [Theory]
    [MemberData(nameof(Attacks))]
    public void NothingTheEditorTypes_CanBecomeAnElement_AnEventHandler_OrAnUnsafeAddress(string markdown)
    {
        var html = SafeMd.ToHtml(markdown);

        Assert.DoesNotMatch(@"<(script|iframe|style|svg|img|video|audio|object|embed|math|details|form|input|link|meta|base)\b", html.Replace("<img src=\"https://example.org/a.png\"", "").Replace("<img src=\"/", ""));
        // Every address that survived is one we would allow.
        foreach (Match m in Regex.Matches(html, @"\s(?:href|src|data|action|formaction|xlink:href)=""([^""]*)"""))
        {
            var decoded = System.Net.WebUtility.HtmlDecode(m.Groups[1].Value);
            Assert.True(SafeMd.IsAllowed(decoded, isImage: false), $"unsafe address {decoded} in {html}");
            Assert.DoesNotMatch(@"(?i)^\s*(javascript|vbscript|data|file):", decoded);
        }
        // Only attributes a plain Markdown renderer produces.
        foreach (Match m in Regex.Matches(html, @"<[a-z0-9]+\s+([^>]*)>"))
            foreach (Match a in Regex.Matches(m.Groups[1].Value, @"(?:^|\s)([a-zA-Z:-]+)=""[^""]*"""))
                Assert.Contains(a.Groups[1].Value, new[] { "href", "src", "alt", "title", "id", "class", "rel", "start", "style", "type", "disabled", "checked" });
    }

    [Fact]
    public void EveryLink_GetsTheRel_ExceptAnEmailAddressTurnedIntoALinkAutomatically()
    {
        var html = SafeMd.ToHtml("[abs](https://example.org) [rel](/blog/x) [frag](#top) [mail](mailto:a@b.c) <a@b.c> <https://example.org/auto>");

        Assert.Contains("<a href=\"https://example.org\" rel=\"noopener nofollow ugc\">abs</a>", html);
        Assert.Contains("<a href=\"/blog/x\" rel=\"noopener nofollow ugc\">rel</a>", html);
        Assert.Contains("<a href=\"mailto:a@b.c\">a@b.c</a>", html); // the automatic email link has no rel
        Assert.Contains("<a href=\"https://example.org/auto\" rel=\"noopener nofollow ugc\">", html);
    }

    [Fact]
    public void ARejectedLink_KeepsItsText_AndARejectedImage_KeepsItsAltText()
    {
        Assert.Equal("<p>click <strong>here</strong></p>\n", SafeMd.ToHtml("[click **here**](javascript:alert(1))"));
        Assert.Equal("<p>a picture</p>\n", SafeMd.ToHtml("![a picture](data:image/png;base64,AAAA)"));
    }

    [Fact]
    public void ImagesAndLinks_ToTheSiteAndToWebAddresses_Survive()
    {
        var html = SafeMd.ToHtml("![ok](/uploads/abc.jpg) ![ext](https://example.org/a.png) [l](./relative) [m](mailto:a@b.c)");
        Assert.Contains("<img src=\"/uploads/abc.jpg\" alt=\"ok\" />", html);
        Assert.Contains("src=\"https://example.org/a.png\"", html);
        Assert.Contains("href=\"./relative\"", html);
        Assert.Contains("href=\"mailto:a@b.c\"", html);
    }

    public static IEnumerable<object[]> DeeplyNested() => new[]
    {
        string.Concat(Enumerable.Repeat("- ", 100)) + "x",
        string.Concat(Enumerable.Repeat(">", 128)) + " x",
        new string('*', 256) + "word" + new string('*', 256),
        string.Concat(Enumerable.Repeat("_[", 256)),
        string.Concat(Enumerable.Repeat("![", 256)),
        string.Concat(Enumerable.Repeat("- ", 1000)) + "x",
    }.Select(p => new object[] { p });

    [Theory]
    [MemberData(nameof(DeeplyNested))]
    public void ATextNestedBeyondTheLibrariesLimit_NeverThrows_AndFallsBackToItsEncodedText(string markdown)
    {
        var html = SafeMd.ToHtml(markdown); // would throw ArgumentException from the Markdown library if it were not caught

        var problem = SafeMd.TryRender(markdown, out var again);
        Assert.Equal(html, again);
        if (problem != SafeMd.Problem.None) Assert.StartsWith("<pre>", html);
        Assert.DoesNotMatch(@"<(script|iframe|img|svg)\b", html);
    }

    [Fact]
    public void TheKnownDepthFailures_AreReportedAsTooDeeplyNested()
    {
        // Pinned: these are the inputs that made the library throw when this guard was written; if a library upgrade stops throwing, delete the case, not the guard.
        Assert.Equal(SafeMd.Problem.TooDeeplyNested, SafeMd.TryRender(string.Concat(Enumerable.Repeat("- ", 100)) + "x", out var html));
        Assert.StartsWith("<pre>", html);
        Assert.Contains("- - - -", html);
    }

    [Fact]
    public void MoreHeadingsThanTheLimit_AreRefused_AndTheLimitItselfIsRendered()
    {
        var atLimit = string.Join("\n", Enumerable.Repeat("# h", SafeMd.MaxHeadings));
        var over = string.Join("\n", Enumerable.Repeat("# h", SafeMd.MaxHeadings + 1));
        var setext = string.Join("\n\n", Enumerable.Range(0, SafeMd.MaxHeadings + 1).Select(i => $"Title {i}\n====="));
        var huge = string.Concat(Enumerable.Repeat("# h\n", 15_000)); // the input that took seconds to render before the limit

        Assert.Equal(SafeMd.Problem.None, SafeMd.TryRender(atLimit, out _));
        Assert.Equal(SafeMd.Problem.TooManyHeadings, SafeMd.TryRender(over, out var fallback));
        Assert.StartsWith("<pre># h", fallback);
        Assert.Equal(SafeMd.Problem.TooManyHeadings, SafeMd.TryRender(setext, out _));
        Assert.Equal(SafeMd.Problem.TooManyHeadings, SafeMd.TryRender(huge, out _));
    }

    [Fact]
    public void ARenderedText_IsServedFromTheCache_TheSecondTime_AndAnEditIsANewRender()
    {
        var text = "## Cache probe " + Guid.NewGuid() + "\n\nBody.";

        var first = SafeMd.ToHtml(text);
        var second = SafeMd.ToHtml(text);
        var edited = SafeMd.ToHtml(text + " More.");

        Assert.Same(first, second); // the very same string instance: nothing was rendered the second time
        Assert.NotEqual(first, edited);
    }

    [Fact]
    public void AnUnsavedPreview_UsesAKeptResult_ButAddsNone()
    {
        var text = "## Preview probe " + Guid.NewGuid() + "\n\nBody.";

        SafeMd.TryRender(text, out var a, remember: false);
        SafeMd.TryRender(text, out var b, remember: false);
        Assert.NotSame(a, b); // nothing was kept by the previews

        var kept = SafeMd.ToHtml(text);
        SafeMd.TryRender(text, out var viaPreview, remember: false);
        Assert.Same(kept, viaPreview); // a result that is already kept is used
    }

    [Fact]
    public void TextsThatDifferOnlyByALoneSurrogate_DoNotShareACacheEntry()
    {
        var unique = Guid.NewGuid().ToString("N");
        var replacement = "x" + unique + "\uFFFD";   // what a UTF-8 encoding of the lone surrogate would turn into
        var lone = "x" + unique + "\uD800";

        var first = SafeMd.ToHtml(replacement);
        var second = SafeMd.ToHtml(lone);

        Assert.NotSame(first, second);
        Assert.Contains("\uD800", second);
        Assert.DoesNotContain("\uD800", first);
    }

    [Fact]
    public void TheFallback_IsExactlyTheEncodedText_InAPreBlock()
    {
        var text = string.Concat(Enumerable.Repeat("- ", 100)) + "<script>x</script>&amp;";

        var problem = SafeMd.TryRender(text, out var html);

        Assert.Equal(SafeMd.Problem.TooDeeplyNested, problem);
        Assert.Equal("<pre>" + System.Net.WebUtility.HtmlEncode(text) + "</pre>\n", html);
        Assert.Contains("&lt;script&gt;x&lt;/script&gt;&amp;amp;", html);
    }

    private static string Fill(string unit, int chars = 60_000) => string.Concat(Enumerable.Repeat(unit, chars / unit.Length + 1))[..chars];

    [Theory]
    [InlineData("# h\n")]
    [InlineData("> # h\n")]
    [InlineData("- # h\n")]
    [InlineData("1. # h\n")]
    [InlineData("> > > # h\n")]
    [InlineData("- > # h\n")]
    [InlineData("> Title\n> =====\n>\n")]
    [InlineData("- Title\n  =====\n\n")]
    public void HeadingsAnywhereInTheText_AreCounted_SoSixtyThousandCharactersOfThemAreRefused(string unit)
    {
        var problem = SafeMd.TryRender(Fill(unit), out var html);

        // Either way it is answered at once and safely: refused for its headings, or (for the deepest nests) for its nesting; never rendered with thousands of headings.
        Assert.Contains(problem, new[] { SafeMd.Problem.TooManyHeadings, SafeMd.Problem.TooDeeplyNested });
        Assert.StartsWith("<pre>", html);
    }

    [Theory]
    [InlineData("> # h\n")]
    [InlineData("- # h\n")]
    [InlineData("1. # h\n")]
    public void TheLimitIsCountedInNestedContainersToo(string unit)
    {
        Assert.Equal(SafeMd.Problem.None, SafeMd.TryRender(string.Concat(Enumerable.Repeat(unit, SafeMd.MaxHeadings)), out _));
        Assert.Equal(SafeMd.Problem.TooManyHeadings, SafeMd.TryRender(string.Concat(Enumerable.Repeat(unit, SafeMd.MaxHeadings + 1)), out _));
    }

    [Fact]
    public void ThingsThatOnlyLookLikeHeadings_AreNotCounted()
    {
        var comments = "```bash\n" + string.Concat(Enumerable.Repeat("# comment\n", 300)) + "```\n";
        var rules = string.Concat(Enumerable.Repeat("---\n", 300));
        var hashtags = string.Concat(Enumerable.Repeat("#hashtag\n\n", 300));
        var emptyItems = string.Concat(Enumerable.Repeat("-\n", 300));
        var indentedCode = string.Concat(Enumerable.Repeat("    # not a heading\n", 300));

        foreach (var text in new[] { comments, rules, hashtags, emptyItems, indentedCode })
            Assert.Equal(SafeMd.Problem.None, SafeMd.TryRender(text, out _));
    }

    [Fact]
    public void OnlyTheDepthLimit_IsCalledNesting_AnyOtherFailureIsNeutral()
    {
        Assert.Equal(SafeMd.Problem.TooDeeplyNested, SafeMd.Classify(new ArgumentException("Markdown elements in the input are too deeply nested - depth limit exceeded.")));
        Assert.Equal(SafeMd.Problem.CannotRender, SafeMd.Classify(new InvalidOperationException("something else")));
        Assert.Equal(SafeMd.Problem.CannotRender, SafeMd.Classify(new ArgumentException("bad argument")));
        // And the real failure the library gives for the known deep input maps to nesting.
        try { Markdown.Parse(string.Concat(Enumerable.Repeat("- ", 100)) + "x", new MarkdownPipelineBuilder().Build()); }
        catch (Exception ex) { Assert.Equal(SafeMd.Problem.TooDeeplyNested, SafeMd.Classify(ex)); }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NothingRendersAsNothing(string? markdown) => Assert.Equal("", SafeMd.ToHtml(markdown));
}
