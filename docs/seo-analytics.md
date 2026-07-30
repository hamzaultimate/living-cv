# SEO structured data & analytics

How this site generates schema.org structured data, keeps page titles/descriptions within
search-engine limits, and wires up optional GA4 analytics. Added alongside PR #13
("Fix meta title/description lengths, add structured data + GA4 wiring").

## Structured data (JSON-LD)

Every page emits one or more `<script type="application/ld+json">` blocks. Two are shared
site-wide from `Pages/Shared/_Layout.cshtml`, the rest are page-specific:

| Scope | Type | Where |
|---|---|---|
| Every page | `WebSite` | `_Layout.cshtml` — site identity (name, url) |
| Every content page | `BreadcrumbList` | Per-page, via `SeoHelpers.BreadcrumbJsonLd` |
| Home | `Person` | `Index.cshtml` — name, jobTitle, knowsAbout, sameAs (LinkedIn/GitHub/YouTube) |
| Project detail | `CreativeWork` | `ProjectDetail.cshtml` — title, summary, tech as `keywords` |
| Blog post | `BlogPosting` | `BlogPost.cshtml` — headline, datePublished, author |

`Person` (site-wide identity) and `WebSite` (the site as a thing) are deliberately separate
schemas — don't merge them.

### Adding breadcrumbs to a new page

Use the shared helper (`src/Portfolio.Web/Services/SeoHelpers.cs`) rather than hand-rolling the
`ListItem` array — pass `(name, path)` pairs root-first, with the site root as `"/"`:

```csharp
var absBase = $"{HttpContext.Request.Scheme}://{HttpContext.Request.Host}";
var breadcrumbLd = SeoHelpers.BreadcrumbJsonLd(absBase, ("Home", "/"), ("Skills", "/skills"));
```

```cshtml
<script type="application/ld+json">@Html.Raw(breadcrumbLd)</script>
```

`CreativeWork`'s `datePublished` is intentionally omitted for project case studies — `Period` is
free text like `"2022-2023"`, not a reliable ISO date, so it's left out rather than guessed
(same reasoning as the sitemap's `<lastmod>` omission for project routes).

## Title & description length

Search engines truncate or flag `<title>` over ~65 characters and meta descriptions outside
~70–160 characters. `Site.Role` and `Site.Summary` are long-form strings shared with the CV page
and PDF, so they're **not** reused verbatim as meta tags — pages that need SEO copy (Home, About)
define their own short `ViewData["FullTitle"]` / `ViewData["Description"]` instead of truncating
the shared strings. Other pages already had page-appropriate short descriptions.

## GA4 analytics (opt-in)

Set `Site.GoogleAnalyticsId` (e.g. `"G-XXXXXXXXXX"`) in `appsettings.json` /
`appsettings.Production.json` to load `gtag.js` site-wide via `_Layout.cshtml`. Leave it empty
(the default in this public template) and the analytics script is omitted entirely — no
Measurement ID ships in the repo.

Even when set, analytics is skipped on hosts starting with `dev.` — the same rule
`/robots.txt` (`Program.cs`) uses to keep the dev subdomain out of search results, so local/dev
browsing never pollutes production GA4 data. See `SiteOptions.HasGoogleAnalytics` and the
`isDevHost` check in `_Layout.cshtml`.

## Verifying changes

- View source (or `curl`) a page and confirm each `<script type="application/ld+json">` block is
  valid JSON with the expected `@type`.
- Google's [Rich Results Test](https://search.google.com/test/rich-results) and the
  [Schema Markup Validator](https://validator.schema.org/) both accept a pasted URL or raw JSON-LD.
- To check the GA4 gating locally, request a page with `Host: dev.<yourdomain>` and confirm the
  `gtag.js` `<script>` tags are absent from the response.
