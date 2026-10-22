# Template authoring guide

Rules for adding a profession template so it fits the existing structure and stays
easy to extend. A template is **data plus an optional theme**. Adding a new profession should
normally need no C# changes and no new Razor pages.

## 1. Anatomy

A template is two independent parts:

| Part | What it is | Where it lives |
|---|---|---|
| Preset | Sections and their order, wording ("copy"), skill groups, SEO patterns, guidance, optional starter content | `src/Portfolio.Web/Templates/<key>/template.json` (+ `starter.json`) |
| Layout | One of a fixed set of archetypes plus a theme | Razor partials under `Pages/Shared/Layouts/<archetype>/` (shared blocks in `Pages/Shared/Blocks/`), theme in `Styles/themes/<key>.css` |

Archetypes with a layout today: `showcase` (Software & IT) and `credentials` (Healthcare). `gallery` and `results`
are named in the plan but not built, so a manifest that names them is skipped until they exist.
A new profession picks an existing archetype. A new archetype is a larger change (section 5).

## 2. Hard rules

1. **No content in Razor.** Names, labels, headlines, and sample copy come from the profile,
   the database, or `template.json`. Never hardcode profession words ("case study", ".NET",
   "clients") in a shared page or partial.
2. **No profession checks in code.** Never write `if (templateKey == "healthcare")`. If behaviour
   differs, express it as a field in the manifest and read that field.
3. **Reuse the existing tables.** Do not add entities or columns for one template. Use label
   overrides and field visibility. If a genuinely new data shape is needed, propose it separately
   with a migration and make it available to every template.
4. **Templates never delete data.** Switching template only hides sections and fields. It never
   removes rows.
5. **Everything optional degrades gracefully.** A section with no entries is hidden (public site)
   or shows an empty state (admin). A missing photo falls back to the monogram.
6. **Theme with CSS variables only.** A theme sets variables (colours, fonts, radius, spacing
   scale) under `[data-template="<key>"]`. Do not add per-template Tailwind classes to shared
   partials, and do not rely on runtime CSS generation. Tailwind is compiled at build time.
7. **Accessibility is part of done.** Body text contrast at least 4.5:1 in light and dark, visible
   focus, semantic headings, alt text for every image, works at 360px wide, respects
   reduced-motion.
8. **SEO is generated, not typed.** Titles and descriptions come from manifest patterns
   (`{name}`, `{title}`, `{summary}`), kept within 65 and 160 characters. JSON-LD is built from the
   profile plus the manifest and the enabled sections, never hand-written per template.
9. **Sensitive professions need guardrails.** Healthcare, legal, and similar presets must include
   a `guidance` note telling users not to publish patient or client data.
10. **Starter content is fictional and clearly marked.** No real people, employers, or
    credentials.

## 3. Manifest (`template.json`)

Validated when the catalog loads, by `TemplateValidator` (and described for editors by
`Templates/template.schema.json`). A manifest that fails validation, has an unknown property, or names a missing or
non-fictional starter file is skipped with a logged error, never a crash. If the profile names a template that did not
load, the site renders the software template. See `Templates/healthcare/template.json` for a complete example.

```jsonc
{
  "$schema": "../template.schema.json",
  "key": "healthcare",               // lowercase kebab-case, equal to the folder name, immutable once released
  "name": "Healthcare",
  "profession": "Healthcare professional",
  "description": "Credentials-first layout for clinicians and allied health ...",
  "archetype": "credentials",        // showcase | credentials today (gallery | results planned)
  "sensitive": true,                 // required for healthcare/legal; needs "guidance"
  "guidance": "Never publish patient-identifiable information ...",
  "theme": { "default": "healthcare", "themeColor": "#0a1418" },  // data-template value; omit for the base look
  "sections": [                      // order = navigation order; a section not listed, or enabled:false, is hidden
    { "key": "certifications", "label": "licences & registrations", "shortLabel": "registrations" },
    { "key": "projects", "label": "research & initiatives", "shortLabel": "initiatives", "itemLabel": "Initiative" },
    { "key": "blog", "label": "articles", "itemLabel": "Article", "enabled": false }
  ],
  "skillGroups": ["Clinical", "Procedures", "Documentation", "Languages"],
  "seo": {
    "title": "{name} — {title}",                      // at most 65 characters once expanded
    "description": "{name}, {title}. ...",           // at most 160; cut at a word boundary at render time if a long name pushes it over
    "aboutDescription": "About {name}, {title}: ...", // at most 160
    "credentialsJsonLd": true                         // list earned certifications in the home page Person markup
  },
  "copy": { "layout": {}, "home": {}, "projects": {}, "experience": {}, "skills": {},
            "certifications": {}, "blog": {}, "talks": {}, "contact": {}, "cv": {} },  // every string is required
  "starter": { "file": "starter.json" }   // optional fictional sample content
}
```

Section keys are a closed list owned by the app (`SectionRegistry`: `about`, `experience`, `projects`, `skills`,
`certifications`, `blog`, `talks`, `testimonials`, `contact`). A template can relabel, reorder, and hide them but cannot
invent new ones. `testimonials` has no page of its own; it is a block on the home page. The CV page is always present.
`projects` and `blog` need an `itemLabel` (what one entry is called; the pages build "Read initiative", "Initiative not
found" from it).

`copy` has no defaults on purpose: a template that forgets a string fails validation instead of showing another
profession's words. Copy is plain text (no `<` or `>`); pages escape it with `TemplateView.Text`. Description strings
accept `{name}`, `{title}`, `{summary}` and are held to 160 characters; a page also cuts an expanded description that a long
name or title pushes past 160, at a word boundary with an ellipsis (at the length limit if there is no space to cut at, never inside a
character pair). Word patterns so they read correctly for any title
("{name}, {title}." rather than "{name} is a {title}.").

`copy.projects` includes the image wording so a template never shows another profession's word for a picture:
`screenshotsHeading` (gallery heading), `galleryLabel` (accessible name of the gallery), `openImageLabel` (accessible name of
the button that opens an image; a caption is appended) and `imageAlt` (alt text for an image without a caption; must contain `{title}`, the project title). Software says "screenshot", Healthcare says "image".

`starter.json` holds `"fictional": true`, a `profile` (same shape as the `Site` config section) and arrays in the seed
file shape (`experiences`, `skills`, `projects`, `certifications`, `testimonials`). Mark every person, employer and
credential as sample content. Starter content is only applied on request (the Phase 3 wizard); it never overwrites a site.

### Theme

`theme.default` is written to `<html data-template="...">`; the base look (software) leaves it empty and adds no
attribute. A theme file sets variables only: the colour tokens as RGB channels per mode, `--font-sans`, `--font-display`,
`--font-mono`, `--radius-lg`, `--radius-md`, under `:root[data-template="<key>"]` and
`:root[data-template="<key>"]` and `:root[data-template="<key>"][data-theme="dark|light"]`. The dark block's selector also lists
`:root[data-template="<key>"]` itself, because dark is the default before the theme script sets `data-theme` (as the base tokens do on `:root`). It is imported from `Styles/app.css` and compiled into the one
stylesheet, so switching needs no rebuild. Tailwind scans `Pages/**/*.cshtml` and its fonts and radii read those
variables with the base look as the fallback. Tests check both modes for 4.5:1 contrast and that every class used by the
layout partials exists in the generated `wwwroot/css/app.css`.

### Preview

The admin switcher frames the real public pages as `/?preview=<key>`. It works only for a signed-in owner with a valid
session, only for keys in the catalog, and only on GET requests for public pages. The response is `noindex`
(header and meta), `no-store`, never output-cached, carries no analytics script and is not in the sitemap; nothing is saved until the owner presses
"Use ...". Anyone else gets the normal page, indistinguishable from one without the parameter.

## 4. Adding a template: checklist

1. Copy `Templates/healthcare/` to `Templates/<key>/` and edit `template.json` (every `copy` string is required).
2. Choose an archetype. Add `Styles/themes/<key>.css` and its `@import` line in `Styles/app.css` only if the base theme
   is not enough.
3. Add `starter.json` (optional, fictional, clearly marked).
4. Run `dotnet test`. The manifest tests cover every template found on disk: validity, unique key, known section keys,
   SEO lengths, guidance for sensitive professions, schema drift, theme contrast, and the rendering tests show no other
   template's wording. Add the new template to the rendering tests' wording lists.
5. Update the template list in the README.
6. Open a PR. Description must state the archetype and attach desktop and mobile screenshots.

## 5. Adding a new archetype (rare)

Only when no existing archetype can express the layout. Requires:
- partials for every section the archetype supports, with the same view-model contracts as the
  existing archetypes (so any preset can use it),
- an entry in the archetype registry,
- the same automated page-render tests passing for at least two presets,
- design review before merge.

## 6. Extension points (kept stable)

- `ITemplateCatalog`: lists and loads templates (disk today, could be plugins later); `TryGet` is the allow-list for
  any untrusted key.
- `ISiteProfileProvider`: the only way pages read profile data.
- `RequestTemplate` / `TemplateResolver`: the template in effect (saved or previewed) with its sections after the owner's
  settings. Pages read copy and section state here.
- `SectionRegistry`: the closed list of section keys and their routes. Mark section pages with
  `@attribute [SectionPage(SectionRegistry.X)]`; a test fails for a public page that is neither marked nor listed as
  not a section page.

Do not read `appsettings` or the database directly from a template partial.
