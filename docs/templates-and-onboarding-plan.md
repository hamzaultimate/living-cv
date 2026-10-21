# Profession templates, onboarding wizard, and UI-managed content

Plan for turning Living CV from a software-engineer portfolio into a multi-profession
portfolio + CV product, with a first-run wizard and a UI that replaces seed-file editing.

## 1. What the code does today (and what blocks this)

| Area | Today | Why it matters |
|---|---|---|
| Identity | `Site` section of `appsettings.json`, bound to `SiteOptions`, injected as `IOptions<SiteOptions>` in layout and pages | A UI cannot edit it. Needs a DB-backed profile. |
| Content | JSON files in `Portfolio.Data/Seed/`, applied by `PortfolioSeeder` | The seeder is **authoritative**: on every startup it deletes rows not in the JSON. Any UI edit would be wiped on the next restart. This must change before any editing UI ships. |
| Auth | None (`UseAuthorization()` with no scheme) | Admin UI needs owner login. |
| Software bias | `CvSkillGroupOrder` hardcodes "Backend / Cloud/DevOps / Embedded/IoT"; `Index.cshtml` hardcodes ".NET & AI" and "9+ years building .NET systems" in title and description; GitHub/Talks/Tech fields are first-class | Copy and structure must come from the template, not the Razor pages. |
| CV | One QuestPDF layout (`CvDocument`), one HTML page (`Cv.cshtml`) | Needs per-template variants. |
| ATS badge | `ats-report.json` generated offline from seed data and committed | Meaningless once content is per-user and edited in the UI. Must be computed from DB content. |
| Images | `wwwroot/img`, shipped at publish time over FTPS | User uploads need storage outside the publish output or they get overwritten on deploy. |
| Tests / CI | One placeholder test; CI only builds | Add real tests before refactoring the data path. |
| Styling | Tailwind compiled at build time | Templates must be switchable at runtime, so they have to be driven by CSS variables and precompiled classes, not by generating CSS per user. |

## 2. Key decisions

**D1. Single owner per deployment (assumed, please confirm).** "New user" means whoever deploys
and claims a fresh instance. This is how the app is built today (one `Site`, one DB). True
multi-tenant hosting (many users, one deployment, subdomains) is a different and much larger
project (tenant id on every table, routing, billing). The plan keeps that door open by putting
all profile data behind one `ISiteProfileProvider`, but does not build it.

**D2. A template is two independent things, not ten bespoke sites.**
- *Profession preset* (data): section list and order, labels ("Case study" vs "Campaign" vs
  "Publication"), skill groups, which entry fields are shown, starter copy and metric ideas.
- *Visual layout* (code): one of 4 layout archetypes plus a colour/typography theme.

Ten professions map onto four archetypes, so we build four layouts and ten presets.

**D3. Keep the existing tables.** Do not rename `Project`/`Experience`/`Skill`. The preset
supplies display labels and hides unused fields. This avoids a risky migration and keeps the
existing software template working unchanged.

**D4. Seeds become demo/starter data only.** Seed runs only when the DB is empty, and only
loads the sample persona if the owner picks "start with sample content". After that the DB is
the source of truth.

**D5. Take-over protection.** A public `/setup` route is an account-takeover risk on a deployed
site. First-run setup requires a one-time `Setup:Key` supplied via environment variable.

## 3. The 10 templates

| # | Profession | Archetype | Leads with | Notes |
|---|---|---|---|---|
| 1 | Software & IT | Showcase | Case studies, tech stack | Existing site, becomes the reference template |
| 2 | Data & Analytics | Showcase | Projects with metrics, tools | Reuses the showcase layout |
| 3 | Product & Project Management | Showcase | Outcomes, roadmaps shipped | Reuses the showcase layout |
| 4 | Design & UX | Gallery | Image-first work grid, case studies | Needs media-heavy cards |
| 5 | Photography, Film & Creative | Gallery | Full-bleed galleries, video | Reuses the gallery layout |
| 6 | Marketing & Content | Results | Campaign metrics, writing samples | Metrics band is the hero |
| 7 | Sales & Business Development | Results | Quota attainment, deals, logos | Reuses the results layout |
| 8 | Finance & Accounting | Results | Quantified impact, credentials | Conservative theme, CV-first |
| 9 | Healthcare | Credentials | Licences, registrations, clinical experience | Compliance wording (no patient data) |
| 10 | Education & Academia | Credentials | Teaching, publications, courses | Reuses the credentials layout |

Not in the first ten, kept as backlog: Legal, Skilled trades, Hospitality, Architecture & Engineering (non-software).

Archetype work: Showcase exists already. New: Gallery, Results, Credentials.

## 4. Phases (in priority order)

Every phase ends in a shippable, working app. Phases 0 to 4 are the critical path.

### Phase 0: Foundations (no user-visible change)
- Add `SiteProfile` (singleton row): identity, hero, metrics, about, contacts, photo, `TemplateKey`,
  `ThemeKey`, `AccentColor`, section settings (JSON). `appsettings` `Site` values remain as the
  defaults used to populate it on first run.
- `ISiteProfileProvider` (cached, invalidated on save) replacing direct `IOptions<SiteOptions>`
  use in `_Layout`, pages, `/cv.pdf`, `/og.png`, sitemap.
- Change `PortfolioSeeder` from authoritative to seed-if-empty. Keep the old behaviour behind a
  `Seed:Mode=Authoritative` flag for people who still want git-managed content.
- Remove software-isms from shared code: move `CvSkillGroupOrder` and hardcoded title/description
  copy into the template preset (software preset keeps today's exact strings).
- Real tests: `WebApplicationFactory` smoke tests for every public page, seeder tests (idempotent,
  never deletes in seed-if-empty mode), profile provider tests. CI runs `dotnet test`.
- Exit criteria: output of every public page is unchanged for the demo persona.

### Phase 1: Owner auth, setup key, and uploads
- Single-owner cookie auth with hashed password, `/admin/login`, lockout, anti-forgery, secure
  cookie settings. First-run owner creation gated by `Setup:Key`.
- `/admin` shell (layout, nav, flash messages) with a "Profile" editor so the first UI-managed
  data exists end to end.
- Image upload service: size and type limits, magic-byte check, re-encode with SkiaSharp (already
  a transitive dependency of QuestPDF, so no new licence), strip EXIF, generated filenames.
  Storage root is configurable (`Uploads:Path`) and served via a static file provider, so it can
  live outside the publish folder and survive FTPS deploys.
- Exit criteria: owner can log in, edit profile fields and photo, see changes live.

### Phase 2: Template engine v1
- `TemplateDefinition` manifest (JSON, in repo): key, name, profession, archetype, section list,
  labels, skill groups, starter content, default theme.
- Razor pieces resolve layout partials by archetype; theme is a `data-template` attribute plus
  CSS variables (colours, fonts, radius), so switching templates needs no rebuild.
- Ship two templates to prove the abstraction: **Software & IT** (parity with today) and
  **Healthcare** (credentials-first, forces the non-software path to be real).
- Template switcher in admin with live preview. Switching never deletes content; sections the
  new template does not use are hidden, not dropped.
- Exit criteria: same data renders correctly in both templates; sitemap and JSON-LD follow the
  enabled sections.

### Phase 3: 4-step onboarding wizard
Shown on first run (after setup key) and re-runnable from admin.
1. **Template**: cards grouped by profession with thumbnail preview and "what's included".
2. **Basics**: name, professional title, email, location, short summary (title and summary are
   pre-suggested from the template).
3. **Photo**: upload with crop/preview, skippable (falls back to monogram).
4. **Review and launch**: live preview, pick starter content (blank, or profession sample),
   accent colour, then "Publish".
Progress is saved per step, so a refresh does not lose work. Keyboard and screen-reader
friendly, works on mobile.
- Exit criteria: fresh DB to a live, correct site in under two minutes, no config file edits.

### Phase 4: Content management UI (replaces seed editing)
Split so users are never left with content they cannot edit:
- **4a (release gate for the wizard):** Experience, Skills, Projects/Work, Certifications.
  List with drag reorder, create/edit/delete, slug generation, image upload for hero and
  screenshots, validation, unsaved-change warning.
- **4b:** Blog (Markdown editor with preview, drafts), Talks, Testimonials, hero/metrics/about
  editing, section on/off and ordering.
- **4c:** JSON export/import of all content. This replaces hand-editing seed files and gives
  backup and portability.
Wizard and 4a ship together; 4b and 4c follow.

### Phase 5: Remaining templates and archetypes
Build one archetype per step, then the presets that use it:
1. Gallery layout, then Design & UX, Photography/Creative.
2. Results layout, then Marketing, Sales, Finance.
3. Credentials layout (already proven by Healthcare), then Education.
4. Data & Analytics and Product/Project Management on the Showcase layout.
Each template needs a preset, theme, starter content, thumbnail, and a11y/contrast check.

### Phase 6: CV output per template
- QuestPDF layout variants per archetype (section order and labels from the preset). HTML `/cv`
  follows the same data model.
- ATS scoring computed from DB content on save (the rule scorer already exists in
  `CvAtsRuleScorer`), stored on the profile, replacing the committed `ats-report.json`.
  The offline console tool is kept for CI/regression use.

### Phase 7: Cleanup and docs
- Update README, `new-user-guide.md`, `CONTRIBUTING.md` (the "no content in Razor pages" rule
  becomes "no content outside templates and the DB").
- Remove dead config paths, publish a "Deploying with persistent uploads" guide.
- Accessibility and Lighthouse pass across all templates.

## 5. Risks

| Risk | Mitigation |
|---|---|
| Seeder deletes UI edits | Phase 0 first; tested explicitly |
| Open `/setup` on a public site | One-time `Setup:Key`, closes after owner exists |
| Upload abuse (huge files, polyglots, SVG scripts) | Allow-list raster types only, re-encode, size cap, no SVG uploads |
| Uploads lost on FTPS deploy | Configurable uploads path outside publish dir; documented |
| Ten templates drift apart | Four archetypes, shared partials, visual regression screenshots per template |
| Healthcare/legal content sensitivity | Preset copy warns against patient/client data |
| EF migration on populated DBs | Additive migrations only; no renames (D3) |

## 6. Working agreement

- Implementation by Sonnet 5.5; each phase gets an independent review by Opus 5.5 before merge,
  and review findings are fixed before the next phase starts.
- No tool or assistant names in commit messages, code comments, or PR text.
- One branch and PR per phase (sub-phases where noted), small commits, `dotnet build` warning-clean
  and `dotnet test` green before review.
