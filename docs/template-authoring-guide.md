# Template authoring guide

Rules for adding a profession template so it fits the existing structure and stays
easy to extend. A template is **data plus an optional theme**. Adding a new profession should
normally need no C# changes and no new Razor pages.

## 1. Anatomy

A template is two independent parts:

| Part | What it is | Where it lives |
|---|---|---|
| Preset | Sections and their order, wording ("copy"), skill groups, SEO patterns, guidance, wizard suggestions, accent colours, editor wording and field visibility, optional starter content and thumbnail | `src/Portfolio.Web/Templates/<key>/template.json` (+ `starter.json`, `preview.png`) |
| Layout | One of a fixed set of archetypes plus a theme | Razor partials under `Pages/Shared/Layouts/<archetype>/` (shared blocks in `Pages/Shared/Blocks/`), theme in `Styles/themes/<key>.css` |

Archetypes with a layout today: `showcase` (Software & IT), `credentials` (Healthcare) and `gallery` (Design & UX, Photography & Film). `results`
is named in the plan but not built, so a manifest that names it is skipped until it exists.
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
    credentials. The convention: the persona and the profile summary say "fictional", titles carry
    "(sample)", employers in experience carry "(fictional)", and client names (`org`) are coined plain
    names without that suffix (see "Bundled starter images" below).

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
  "archetype": "credentials",        // showcase | credentials | gallery today (results planned)
  "sensitive": true,                 // required for healthcare/legal; needs "guidance"
  "guidance": "Never publish patient-identifiable information ...",
  "theme": {
    "default": "healthcare", "themeColor": "#0a1418",               // data-template value; omit for the base look
    "accents": [                                                    // alternative accent colours the wizard offers (allow-list)
      { "key": "blue", "label": "Blue", "dark": { "accent": "#60a5fa", "strong": "#93c5fd" }, "light": { "accent": "#1d4ed8", "strong": "#1e40af" } }
    ]
  },
  "heroSuggestions": {               // what the wizard's Basics step starts from (required)
    "title": "Registered Nurse",
    "summary": "Registered nurse committed to safe, compassionate and evidence-based care, ..."
  },
  "sections": [                      // order = navigation order; a section not listed, or enabled:false, is hidden
    { "key": "certifications", "label": "licences & registrations", "shortLabel": "registrations" },
    { "key": "projects", "label": "research & initiatives", "shortLabel": "initiatives", "itemLabel": "Initiative" },
    { "key": "blog", "label": "articles", "itemLabel": "Article", "enabled": false }   // optional: "required": true makes a section always on
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
  "editor": {                        // optional: wording and field visibility in the admin content editors
    "experience": {
      "itemLabel": "Position",        // what one entry is called ("Add position")
      "fields": {
        "company": { "label": "Employer or placement" },
        "summary": { "hint": "Describe your responsibilities. Never include patient information." },
        "tech": { "label": "Systems and equipment" }
      }
    },
    "certifications": { "itemLabel": "Licence or registration", "fields": { "badgeImageUrl": { "hidden": true } } }
  },
  "starter": { "file": "starter.json" }   // optional fictional sample content: a file, or { "source": "seed" } for the built-in demo persona
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

> **Upgrading a custom template:** `heroSuggestions` is now required. A manifest without it fails validation, is skipped with a logged
> error, and the site falls back to the default template for any profile that named it. Add the block (and ideally `theme.accents`
> and a `preview.png`) before upgrading.

`editor` is optional and shapes the admin content editors (`/admin/content`: experience, skills, projects, certifications, talks, testimonials, blog) and the three profile editors
(`hero`, `metrics`, `about`, at `/admin/profile/hero`, `/metrics`, `/about`; described below). Anything it
leaves out uses the neutral wording in `EditorFields`, so a template only needs to say what differs. For each of the seven sections:

| Setting | Effect |
|---|---|
| `itemLabel` | What one entry is called in buttons and headings ("Add position"). `projects` uses the section's own `itemLabel` from `sections` when this is empty. Plain text, at most 40 characters. |
| `fields.<key>.label` | The field's label (at most 60 characters, plain text). |
| `fields.<key>.hint` | The short help under the field (at most 200 characters, plain text). |
| `fields.<key>.hidden` | `true` leaves the field off the form. The value already stored in it is kept untouched by every save, and a crafted post cannot write to it. |

Field keys are a closed list (a typo or unknown key fails validation): `experience`: `company`, `title`, `start`, `end`, `current`,
`summary`, `highlights`, `tech`; `skills`: `name`, `group`; `projects`: `title`, `slug`, `role`, `org`, `period`, `summary`, `category`, `problem`,
`approach`, `impact`, `tech`, `links`, `featured`, `heroImage`, `screenshots`; `certifications`: `name`, `issuer`, `status`, `date`, `category`,
`credentialUrl`, `certificateUrl`, `badgeImageUrl`; `talks`: `title`, `type`, `event`, `date`, `youtubeId`, `url`; `testimonials`: `quote`, `author`,
`authorTitle`, `sourceUrl`; `blog`: `title`, `slug`, `description`, `date`, `tags`, `cover`, `body`, `draft`. Fields an entry cannot exist without (`company`, `title`, `start`, `summary`; `name`, `group`;
`title`, `slug`, `summary`; `name`, `issuer`, `status`; `title`; `quote`, `author`; `title`, `slug`, `date`, `body`, `draft`) can be relabelled but not hidden. (`talks` also has the wording-only keys `typeVideo` and `typeTalk` for the two choices of its type field.) The talks list has no manual order (the public page
lists talks newest first by `date`), the testimonials list does. Some labels already come from `copy`: a project's
`problem`, `approach`, `tech`, `links` and `screenshots` fields default to `copy.projects` headings, and a certification's status options
are `copy.certifications.statusEarned`, `statusInProgress` and `statusPlanned`, so a template that words those correctly needs no `editor`
entry for them. Sensitive templates (`sensitive: true`) have their `guidance` shown on every editor. The section's label and
whether it is enabled also drive the editor headings and a "not shown by your template" note, and a test adds a template with
unique wording to prove none of this is hardcoded.

**The profile editors** (`editor.hero`, `editor.metrics`, `editor.about`) use the same `itemLabel` and `fields` settings. They edit the hero, the headline metrics and the
About text stored on the profile, so a template can name them and leave out what its profession does not use. Keys:
`hero`: `heading`, `eyebrow`, `headline`, `headlineAccent`, `lede`; `metrics`: `heading`, `value`, `unit`, `label`; `about`: `heading`, `headline`, `paragraphs`, `valueProps`,
`valuePropTitle`, `valuePropBody`, `currentlyNote`, `focus`. `heading` is a **wording-only** key: its `label` is the page title (and what the dashboard tile, the save message and the
"changed somewhere else" refusal call the block) and its `hint` is the line under the title. `valuePropTitle` and `valuePropBody` are the wording-only labels of the two boxes of one About card. Wording-only
keys can be relabelled but never hidden (the validator refuses `hidden: true` on them). The fields a block cannot do without (`hero.headline`, `metrics.value`, `about.headline`) can be relabelled but not hidden. `itemLabel`
names one row ("Add metric", "Metric 2", "Add card") on `metrics` and `about`; the hero has no rows, so it is accepted and unused there. A hidden field is not drawn, not validated and not read from the post, and what is stored
in it is kept by every save (for a hidden metric column the stored value follows its row when rows are moved or removed). About is a section: when the template does not enable it, its editor says the public site does not show it and keeps the text. The hero and the metrics are blocks of the home page, which every template shows.
**The sections page** (`editor.sections`, at `/admin/sections`) has the keys `heading`, `label` (the owner's name for a section in the menu; may be hidden, in which case stored names are kept untouched), `shown` (the switch; cannot be hidden), and the wording-only `locked` (the note on a section that cannot be switched off), `noOrder` (the note on a row whose position has no effect on the menu: contact and home-page blocks) and `defaultOff` (the note on a section the template lists as `enabled: false`). `itemLabel` is what one section is called in messages ("Each zork needs its own name"). A template section may carry `"required": true` to be always on (the owner gets no switch and a stored hide is ignored); it cannot be combined with `enabled: false`. The owner's settings only ever apply to sections the template lists: a section listed as `enabled: false` stays off until the owner explicitly switches it on, an unlisted key is never shown, and the order is the owner's when they saved one and the template's otherwise.

**The export and import page** (`editor.transfer`, at `/admin/transfer`) has only wording-only keys, none of which can be hidden: `heading` (page title and the line under it), `exportHeading`, `exportButton`, `importHeading`, `fileLabel`,
`applyProfile` (the tick box that also applies the profile in the file), `previewButton`, `confirmButton`, `discardButton`, `profile` (what messages call the owner's profile: "The profile was changed after the preview was made") and `authoritative` (the notice shown only when
`Seed:Mode=Authoritative` would overwrite an import). `itemLabel` is accepted and unused. Everything else on the page is named with words you already give: each table is headed by the section's name, an entry is called what its own editor's `itemLabel` says, and
every field message (a missing value, a limit, "not plain text") uses that editor's field labels, so a template needs no extra words for the entries. Neutral defaults live in `EditorFields.TransferFields`; Software & IT sets a few of them and Healthcare gives its own
(including "Never put patient information in an entry" through the template's `guidance`, shown on the page like on every editor).

Only plain text is accepted in all of these fields (no `<` followed by a letter, `/`, `!` or `?`), whatever a template says. Messages that name the block or a row, including errors and the "saved" and "not saved" notices, are built from these words:
a test adds a template with unique wording and checks that none of the neutral wording appears.

`heroSuggestions` is required. `title` (at most 120 characters) and `summary` (at most 400) are plain text offered in the
wizard's Basics step, which pre-fills them; the owner edits them and **whatever stays is published as written**, so write
a summary that reads correctly for anyone in the profession, never an instruction ("Add your specialty") and never a
claim about a person. Suggestions are never hardcoded in Razor: a test adds a template with a unique suggestion and checks
it appears. The wizard's review step also shows the SEO title and description that your `seo` patterns produce.

`theme.accents` lists at most 8 colours the owner can pick on the review step. The stored choice is the **key**, never a
CSS value, and a key the template does not list is ignored. Each accent gives an `accent` and a `strong` (hover) colour as
`#rrggbb` for the dark and the light mode, and every tone must keep 4.5:1 contrast on the template's `bg`, `surface` and
`surface-2` tokens (a test checks this against the template's own stylesheet, or `Styles/app.css` for the base look). An
accent is applied by one small `<style>` block that sets only `--c-accent` and `--c-accent-strong`; when none is picked
nothing is emitted, so the page is exactly the template's own.

`starter` is either `{ "file": "starter.json" }` or `{ "source": "seed" }`, never both. `seed` points at the app's built-in
demo persona (`Portfolio.Data/Seed`), which Software & IT uses so that data is not copied into a second file.

`starter.json` holds `"fictional": true`, a `profile` (same shape as the `Site` config section) and arrays in the seed
file shape (`experiences`, `skills`, `projects`, `certifications`, `testimonials`). Mark every person, employer and
credential as sample content. Starter content is only applied on request, in the wizard's review step, and only into tables that have no rows (seed-if-empty
per table): it never updates or deletes a row, and it brings the sample rows only, never the starter profile's identity or
text, so the owner's name, title and summary are always their own.

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

### Thumbnail

Ship `Templates/<key>/preview.png`, 1200 x 800, under about 600 KB: a real screenshot of the home page of a running site in
that template (dark mode, sample content) works well. The wizard finds it through the catalog by key
(`ITemplateCatalog.GetPreviewImagePath`) and serves it from `/admin/welcome/thumb/<key>`, owner-only: the key must be in the
catalog and the file name is fixed, so nothing in the request becomes a path. It is not a static file and needs no manifest
field. A template without one still works: its card shows the template name instead of a picture.

### Draft preview

The wizard's review step frames `/?preview=<key>&draft=1`. It uses the same owner-only session check, `noindex`, `no-store`,
no analytics and no canonical link as the template preview, and additionally swaps in the owner's unpublished identity,
accent and photo for that one request. Anyone else, a stale session, or a missing draft gets the normal page.

## 4. Adding a template: checklist

1. Copy `Templates/healthcare/` to `Templates/<key>/` and edit `template.json` (every `copy` string and `heroSuggestions` are required).
2. Choose an archetype. Add `Styles/themes/<key>.css` and its `@import` line in `Styles/app.css` only if the base theme
   is not enough.
3. Add `starter.json` (optional, fictional, clearly marked), a few `theme.accents` and a `preview.png` thumbnail. Add an `editor` block where the
   neutral editor wording does not fit the profession (what an entry is called, employer vs company, fields the profession never uses).
4. Run `dotnet test`. The manifest tests cover every template found on disk: validity, unique key, known section keys,
   SEO lengths, guidance for sensitive professions, suggestions, accent contrast, the thumbnail, schema drift, theme contrast, and the rendering tests show no other
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

How the gallery archetype was added, as the worked example:
- **Partials.** `Pages/Shared/Layouts/<archetype>/_Home.cshtml` is required (the home page renders `Layouts/<archetype>/_Home`). `_Projects.cshtml` and
  `_ProjectDetail.cshtml` are optional: `Projects.cshtml` and `ProjectDetail.cshtml` ask `LayoutPartials.Find` whether the archetype has one and render it (with a
  `ProjectsPage` or `ProjectPage` model carrying the data and the structured data the page built), otherwise they use the shared markup. An archetype that
  provides none of them leaves the shared pages byte-for-byte as they were, which is how Software and Healthcare stay unchanged. Shared pieces inside an archetype
  (the gallery's `_WorkCard`) live next to its other partials. Reuse the blocks in `Pages/Shared/Blocks/` (testimonials, contact call to action) rather than copying them.
- **Registry.** Add the constant and the name to `Archetypes.Implemented` in `TemplateDefinition.cs` (it is already in `Known` and in the schema enum). A manifest naming an archetype outside
  `Implemented` is skipped with a logged error. If the home page needs data other archetypes do not load, extend the decision in `IndexModel` by archetype
  (`Archetypes.ShowsSkillsOnHome`), never by template key.
- **Theme variables.** Besides the colour tokens, fonts and radii, a theme may set variables the layout reads with a fallback, so a template can change density or shape
  with data only. The gallery reads `--gallery-card-aspect` (the shape of a work card, fallback `4/3`) and `--gallery-card-min` (the narrowest a card may get, fallback `17rem`; a
  smaller value fits more columns) and `--gallery-card-max` (the widest one card may be, fallback `40rem`; with auto-fit columns a row of few cards fills the width and a single card is capped and centred). Photography & Film sets `3/2` and `14rem`; Design & UX sets neither. Use a fallback for every new variable, keep the values free of class selectors (a test checks theme files),
  and note that an arbitrary Tailwind value with a comma (`minmax(a,b)`) is written by Tailwind as a hex escape, which the stylesheet test already accounts for.
- **Tests to extend.** `GalleryArchetypeTests.Keys` lists every template on the archetype; adding a key runs the whole suite of checks for it (partials, starter, blank / sample / full rendering,
  wording read from the manifest, accessibility, headers) and `ArchetypeTemplateFlowTests` (shared with the results key list) runs the admin paths (wizard, preview, switch, sections, export and import, a blank site end to end). Add the key to the
  hard-coded lists in `TemplateManifestTests` (shipped folders and catalog order) and the wizard card counts in `OnboardingFlowTests`. A new archetype should get a test file of the same shape and
  must prove the shared pages are unchanged for the other archetypes (the Software parity snapshots, and a before / after render of Healthcare).

### Optional wording of the gallery layout

Five optional `copy` keys (empty or absent uses the English default, which is built from the section and item labels, so a template that does not set them reads as before). Patterns take `{work}` (the work section's label) and `{item}` (what one entry is called, in lower case):
`home.viewWork` (default "View {work}"), `home.allWork` ("All {work} →"), `projects.nextItem` ("Next {item}"; also the accessible name of the next-entry navigation), `projects.readItem` ("Read {item} →") and `projects.watchVideo` ("Watch the video ↗"). They are plain text like all copy. Each key may use only the tokens listed for it (`watchVideo` takes none): the validator rejects any other `{...}`, so a token is never silently dropped. A pattern is filled in one pass, so a label that itself contains the text `{item}` is printed as written.
The gallery hero's "Download CV" and "LinkedIn" buttons stay fixed words, as in the credentials layout.

### The results layout

`results` is for work judged by numbers (campaigns, writing, deals). The home page's hero ends in a large band of the profile's **Key figures** (the metrics editor's value, unit and label; hidden when there are none), then a strip of **client names**, then the featured work as result cards. No new column or table:
- **Key figures** are `SiteProfile.Metrics`, set large in the band.
- **Client names** are the distinct `Project.Org` values in work order, drawn as plain text wordmarks (never a picture or logo), loaded by `IndexModel` through `Archetypes.ShowsClientsOnHome`.
- **Result lines** are `Project.Impact`. A line that starts with a short figure (`+38%`, `2.1M`, `$1,200`, `10x`) is split by `ImpactLine.Split` into the figure, set large, and the words after it; any other line is shown whole. A bare year ("2024 President's Club winner"), a list number ("1. Opened the region") and a figure with a space before its unit ("+38 % lift") are not treated as figures, and a figure longer than nine characters is shown as plain text. A line that merely starts with a number ("3000 users") is a figure; owners control the lead figure by how they word the line. The card shows the first non-blank line; the case-study page shows all of them as tiles above the cover.
- Every project, testimonial and hero (eyebrow, headline, accent, introduction) field the manifest offers in an editor appears in the page's own content, and the key figures band shows every metric (a test writes a value into each and looks for it inside `<main>`, outside the structured data). The About fields are shown by the shared About page, which this layout does not change.
- **Every verb comes from a copy key.** The results hero's buttons and the filter's "all" chip are not hardcoded: `home.downloadCv` ("Download CV"), `home.linkedIn` ("LinkedIn"), `home.clientsHeading` ("Worked with") and `projects.filterAll` ("All") are optional copy keys with those English defaults, plain text, no placeholders (the validator rejects any `{...}` in them, and the schema, the drift test and a unique-word test cover them). The layout also reads `home.viewWork`, `home.allWork`, `projects.nextItem`, `projects.readItem` and `projects.watchVideo` as the gallery does. A test greps the results partials for fixed button words, so a new UI verb in a results partial must be a new optional key (add it to `HomeCopy` / `ProjectsCopy` with `[OptionalCopy]`, a text helper with the default, `CopyText.AllowedTokens`, the schema, and the optional-copy tests).
- **Theme variables, all with fallbacks:** `--results-card-aspect` (`16/10`), `--results-card-min` (`20rem`), `--results-card-max` (`40rem`) and `--results-band-min` (`11rem`, the narrowest a figure tile may get). Marketing & Content sets only `--results-band-min`; Sales & Business Development sets `3/2`, `18rem` and `10rem`. A theme file sets variables only.
- **Contrast.** `ThemeStylesheetTests` checks every theme's text, muted, accent and accent-strong tokens against bg, surface and surface-2 (and bg on accent) at 4.5:1 in light and dark; `ResultsArchetypeTests` repeats the check by name for each results key.
- **Starter content.** Client names (`org`) are coined single words (no address, industry word, "&" or company suffix), used only by "(sample)" entries; at least one result line starts with a figure and one with a word, so both shapes show in the sample.
- **Adding a results template.** Add its key to `ResultsArchetypeTests.Keys` (every check, and the flow tests in `ArchetypeTemplateFlowTests`, then run for it), to the hard-coded lists in `TemplateManifestTests` and the wizard card counts in `OnboardingFlowTests`, and write a manifest, starter, theme, bundled pictures and a real screenshot thumbnail. The layout needs no change.

The validator's "has no layout yet" check (an archetype in `Known` but not in `Implemented`) is kept as a safety net for the next archetype, but it has no test while `Implemented` equals `Known`; add one with the first archetype that ships in two steps.

### Media and video in layouts

A layout never embeds a third-party player or script. The gallery and results layouts draw images only: a project cover or picture whose address is a video file is not drawn as an image or a player (it appears as a plain link, see below; on a card the plain initials tile is only the placeholder where a cover image would be), and
film is shown as a link in the entry's `links`, with a still as the cover. There is no poster field: the cover image is the only "poster". A video file used as a cover or picture is drawn as a plain first-party link (`target="_blank" rel="noopener"`, labelled "caption — watchVideo wording" when it has a caption, else by `copy.projects.watchVideo`) on the card and the case-study page, never as `<video>` or `<img>`; an address that is not a site path or an http(s) address is not linked at all (`LayoutPartials.IsLinkable`). Talks, which embed a YouTube player when a video id is stored, are off by default in the gallery templates and their `youtubeId` field is hidden.
Long unbroken words (a category, a tool name, a role line) must wrap: use `break-words` and `min-w-0` utilities in the layout partials, never a change to the shared `.tag` or `.filter-chip` rules, which Software and Healthcare use.
Every picture needs alt text: a cover uses the entry's `heroImageAlt`, then `copy.projects.imageAlt` with the title; a gallery picture uses its caption. A layout must also respect reduced motion (the hover zoom has `motion-reduce` variants).

### Bundled starter images

Starter content may bring pictures, which are ordinary static files, not uploads:
- Put them under `wwwroot/img/starter/<key>/` and refer to them by site-relative address (`/img/starter/<key>/<name>.svg`). Never point at another site; a test fails for any address outside `/img/`.
- Generate them yourself or use work you own outright. Simple generated SVG or PNG placeholders are enough; keep each under about 60 KB, with no scripts and no text that needs a font. Do not download images, and do not include anyone's real photograph, logo or product.
- Give every cover a `heroImageAlt` and every extra picture a caption (which becomes its alt text); a test checks both.
- Titles carry "(sample)" and employers in experience "(fictional)", as in the Healthcare starter. Client names on work entries (`org`, which the results layout also shows as a strip of wordmarks) are coined single words with no industry word, place, "&" or company suffix (for example "Quillbarrow"), used only by "(sample)" entries; a test enforces that shape. No trademark search was possible, so owners replace them with their own clients, and an author should avoid anything that reads like an existing business.

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
