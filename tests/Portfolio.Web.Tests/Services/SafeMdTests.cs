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

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NothingRendersAsNothing(string? markdown) => Assert.Equal("", SafeMd.ToHtml(markdown));
}
