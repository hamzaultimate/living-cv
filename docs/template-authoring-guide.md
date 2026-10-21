# Template authoring guide

Rules for adding a profession template so it fits the existing structure and stays
easy to extend. A template is **data plus an optional theme**. Adding a new profession should
normally need no C# changes and no new Razor pages.

## 1. Anatomy

A template is two independent parts:

| Part | What it is | Where it lives |
|---|---|---|
| Preset | Section list and order, display labels, skill groups, visible fields, starter content, SEO copy patterns | `src/Portfolio.Web/Templates/<key>/template.json` |
| Layout | One of a fixed set of archetypes (`showcase`, `gallery`, `results`, `credentials`) plus a theme | Razor partials under `Pages/Shared/Layouts/<archetype>/`, theme CSS under `Styles/themes/<key>.css` |

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
   profile plus the manifest, never hand-written per template.
9. **Sensitive professions need guardrails.** Healthcare, legal, and similar presets must include
   a `guidance` note telling users not to publish patient or client data.
10. **Starter content is fictional and clearly marked.** No real people, employers, or
    credentials.

## 3. Manifest (`template.json`)

Validated at startup against the schema in `Templates/template.schema.json`. A template that
fails validation is skipped with a logged error, never crashes the site.

```jsonc
{
  "key": "healthcare",              // lowercase kebab-case, unique, immutable once released
  "name": "Healthcare",
  "profession": "Healthcare professional",
  "description": "Credentials-first layout for clinicians and allied health.",
  "archetype": "credentials",       // showcase | gallery | results | credentials
  "theme": { "default": "healthcare", "accents": ["teal", "blue", "slate"] },
  "sections": [                     // order is the default nav and home order
    { "key": "experience",     "label": "Clinical experience", "enabled": true },
    { "key": "certifications", "label": "Licences & registrations", "enabled": true },
    { "key": "projects",       "label": "Research & initiatives", "itemLabel": "Initiative", "enabled": false }
  ],
  "fields": {                       // which optional fields the editor shows for this template
    "experience": { "tech": false, "highlights": true }
  },
  "skillGroups": ["Clinical", "Procedures", "Systems", "Languages"],
  "seo": {
    "title": "{name}: {title}",
    "description": "{name} is a {title}. Registrations, experience and contact details."
  },
  "heroSuggestions": { "eyebrow": "Registered Nurse", "metrics": ["Years of practice", "Patients per shift"] },
  "guidance": "Do not include patient-identifiable information.",
  "starter": { "file": "starter.json" }   // optional sample content, see below
}
```

Section keys are a closed list owned by the app (`experience`, `projects`, `skills`,
`certifications`, `blog`, `talks`, `testimonials`, `about`, `contact`). A template can relabel,
reorder, and disable them but cannot invent new ones.

## 4. Adding a template: checklist

1. Copy `Templates/_example/` to `Templates/<key>/` and edit `template.json`.
2. Choose an archetype. Add `Styles/themes/<key>.css` only if the default theme is not enough.
3. Add `starter.json` (optional) using the same shape as the seed files.
4. Add a thumbnail `Templates/<key>/preview.png` (1200x800).
5. Run the template tests (`dotnet test`). They automatically cover every template found on disk:
   schema validity, unique key, every section key is known, SEO lengths, theme contrast, and
   that each public page renders with starter content and with no content.
6. Update the template list in the README.
7. Open a PR. Description must state the archetype and attach desktop and mobile screenshots.

If step 5 passes with no code changes, the template is correct by construction.

## 5. Adding a new archetype (rare)

Only when no existing archetype can express the layout. Requires:
- partials for every section the archetype supports, with the same view-model contracts as the
  existing archetypes (so any preset can use it),
- an entry in the archetype registry,
- the same automated page-render tests passing for at least two presets,
- design review before merge.

## 6. Extension points (kept stable)

- `ITemplateCatalog`: lists and loads templates (disk today, could be plugins later).
- `ISiteProfileProvider`: the only way pages read profile data.
- `ISectionRegistry`: the closed list of section keys.

Do not read `appsettings` or the database directly from a template partial.
