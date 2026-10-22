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
**Status: done** (merged).

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
**Status: done** (pending independent review).

- **One-time registration, one account only.** `/setup` creates the owner account and then
  permanently closes (returns 404) once any user exists. The database holds at most one user,
  enforced by a constraint, not just by application code. Gated by `Setup:Key`. No sign-up, no
  second account, no roles. Password reset is an out-of-band CLI command run by whoever has the
  code (`dotnet run -- reset-owner`).
- Cookie auth with hashed password, `/admin/login`, lockout, anti-forgery, secure cookie settings.
- `/admin` shell (layout, nav, flash messages) with a "Profile" editor so the first UI-managed
  data exists end to end.
- Image upload service: size and type limits, magic-byte check, re-encode with SkiaSharp (already
  a transitive dependency of QuestPDF, so no new licence), strip EXIF, generated filenames.
  Storage root is configurable (`Uploads:Path`) and served via a static file provider, so it can
  live outside the publish folder and survive FTPS deploys.
- Exit criteria: owner can log in, edit profile fields and photo, see changes live.

Implementation notes (decisions worth reviewing):
- **Identity-free auth.** One fixed account does not need a user store, roles, claims tables, email
  confirmation or external logins, so ASP.NET Core Identity's EF stack would be mostly dead weight and a
  larger attack surface. The app uses the framework's `PasswordHasher<TUser>` (PBKDF2-HMAC-SHA512, random
  salt, 210,000 iterations stored in the hash, rehash-on-login) plus cookie authentication, and one
  `OwnerAccounts` table. Password policy: 12 to 128 characters.
- **Single account in the database.** `OwnerAccounts` has `Id` as primary key and `CHECK ([Id] = 1)`, like
  `SiteProfiles`. Setup inserts without a read-then-write check, so two concurrent `/setup` posts cannot both
  win: the second insert violates the key and is answered with 404.
- **Lockout on the account row** (failed count and lockout end, so it survives restarts and applies across
  instances), 5 failures then 15 minutes. With one account every failed attempt counts against it
  whatever user name was typed, so case, whitespace or invented names cannot dodge it. The cost is that a
  stranger could keep the owner locked out, so that is mitigated in two ways rather than accepted:
  a per-client-address throttle (10 attempts per 15 minutes, in memory, IPv6 grouped by /64) that runs
  before the account counter, the sign-in gate and the hash; and a signed known-device cookie
  (`lcv-device`, Data Protection, one year, bound to a digest of the password hash) that exempts a browser
  that has signed in before from the lockout while still requiring the password and the per-address limit.
  Failed guesses from such a browser do not add to the shared count: it is not subject to the lockout, so
  counting would only lock the owner's other devices after their own typos. The cookie is bound to the
  password hash, not the security stamp, because sign-out rotates the stamp (to end other sessions) and that
  must not revoke trusted browsers; `reset-owner` changes the hash, which does. Sign-in attempts are
  serialised per instance with a 5 second bounded wait, and a decoy hash equalises timing for a missing
  account. The per-address key is the connection address: it is the real client on IIS in-process, and behind
  any other proxy it is only meaningful once forwarded headers are configured for that proxy (an
  earlier note here claimed IIS hides the address; that is wrong for in-process hosting).
- **Setup key** is compared in constant time over SHA-256 digests, must be 16+ characters, and wrong guesses
  are throttled globally (5 then 15 minutes) in memory, so a restart clears the count. `/setup` is a 404 when the key is unset, the database is
  unavailable, or any account exists.
- **Sessions:** `__Host-` prefixed, `Secure`, `HttpOnly`, `SameSite=Strict` cookie, 30 minute sliding window,
  12 hour absolute cap, and a security stamp that `reset-owner` and sign-out rotate, which ends every session.
  Renewal of the sliding window happens on `/admin` requests only (the framework's own renewal is off,
  because it cannot be switched off per path). `reset-owner` prints the account's user name; on IIS it needs
  `ConnectionStrings__Default` set in the shell it is run from.
- **Uploads** use SkiaSharp (explicit `PackageReference`: QuestPDF 2026 ships its own private Skia binding,
  so SkiaSharp is not a transitive dependency after all). Header-first size checks, orientation applied,
  metadata dropped by re-encoding from pixels, 25 megapixel / 10,000 px source caps, 1600 px output cap.
- **Output cache:** the "Generated" policy is tagged `Generated` and also varies on the profile provider's
  version. Saving the profile primes `ISiteProfileProvider` with the saved values (bumping the version) and
  then evicts the tag, so output rendered from the old profile by a request already in flight is stored under
  a key nobody asks for again. Cleanup after the commit ignores the request's cancellation token.
- **Seed:Mode=Authoritative and the admin:** it overwrites the stored profile from config once per process,
  discarding `/admin` edits, and resets the photo URL, orphaning the uploaded file.

### Phase 2: Template engine v1
**Status: done** (pending independent review).

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

Implementation notes (decisions worth reviewing):
- **Extended, not replaced.** `TemplatePreset` became `TemplateDefinition`; the catalog still loads `Templates/<key>/template.json`.
  Manifests are deserialized strictly (unknown properties rejected), validated by `TemplateValidator` and skipped with a logged
  error when invalid. `Templates/template.schema.json` documents the shape and a test keeps it in step with the model. An unknown
  key on the profile falls back to software.
- **Copy has no defaults.** Every profession-specific string (headings, empty states, status words, meta descriptions) is a required
  manifest field, so a template cannot silently inherit another profession's wording. Pages escape it with a minimal escaper so
  apostrophes and dashes render exactly as the old literals did.
- **Software parity is enforced byte for byte.** Golden HTML for 15 routes of the demo persona was captured before any change and
  is compared after normalising only tokens, nonces, asset fingerprints and the copyright year. Software adds no `data-template`
  attribute (its look is the base `:root`), so no markup changed.
- **Archetype layouts.** `Layouts/<archetype>/_Home.cshtml` composes the home page; featured work, testimonials and the contact call
  to action are shared blocks. Only the home page differs by archetype for now; other pages share one implementation driven by copy.
  Credentials is the second archetype; gallery and results are rejected by the validator until they have layouts.
- **Sections.** The template's list decides navigation, sitemap, CV and routes. A page marked `[SectionPage]` answers 404 when its
  section is disabled (also under preview). The filter marks the request and `UseNotFoundPage` turns that empty 404 into the site's styled `/not-found`
  page (GET/HEAD only), without mentioning the section. Every other 404 (unknown address, closed /setup, /admin/*) stays the plain empty
  response it always was, so the software template's public output is unchanged. The page is `noindex` and has no canonical or og:url. The owner's per-section settings on the profile (existing JSON column, no migration)
  can relabel or hide a section but never enable one the template lacks. Hidden means not shown; no row is ever removed.
- **Switching** only writes `SiteProfile.TemplateKey`, through `ProfileEditor` so the profile cache is primed (version bump) and the
  `Generated` output cache evicted, like a profile save. The method takes a catalog entry, not a string. `ThemeKey` is unused.
- **Preview** is `?preview=<key>` on public GET pages. Because public requests do not validate the session cookie, it calls the same
  session check as /admin (stamp and age) rather than trusting `User.IsAuthenticated`. It is ignored for anyone else, for keys outside
  the catalog, and for the generated endpoints (sitemap, CV PDF, share image), which always use the saved template. Responses are
  noindex, no-store and framable only by the same origin; the admin CSP gains `frame-src 'self'` on the template page only. Links
  inside a preview keep the parameter through a small preview-only script.
- **Theme** is variables only, imported into `app.css` and compiled by Tailwind; fonts and radii in the Tailwind config read variables
  with the previous values as fallbacks. Healthcare uses system fonts, so no font files were added.
- **JSON-LD** follows sections: no page, no breadcrumb for a disabled section; `seo.credentialsJsonLd` adds `hasCredential` from the
  earned certifications only, never in-progress or planned ones (Healthcare on, software off to keep its output unchanged).
- **Review fixes.** The preview page has no analytics script; the Space Grotesk and JetBrains Mono preloads are emitted only for the base
  look (no theme); project image wording (`imageAlt`, `galleryLabel`, `openImageLabel`) is manifest copy; Healthcare's dark colours also
  apply when the page has no `data-theme`; the catalog is resolved at startup so a broken default manifest fails the start; expanded
  manifest-driven meta descriptions (including projects, blog and talks) are cut to 160 characters at render time; the layout
  fallback (the profile summary on pages without their own description) is left as it was to keep software output identical.
- **Known gaps.** The CV ATS badge still shows the committed demo-persona report on every template (Phase 6). The CV page prints
  certification status with the enum names as before. Issuer logos on the certifications page are matched by name, as before.
  /cv.pdf and /og.png keep the software look (per-template CV output is Phase 6).

### Phase 3: 4-step onboarding wizard
**Status: done** (pending independent review).

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

Implementation notes (decisions worth reviewing):
- **Routes.** `/admin/welcome` (template), `/basics`, `/photo`, `/review`, plus owner-only `/admin/welcome/thumb/{key}` and
  `/admin/welcome/draft-photo`. All sit under the deny-by-default admin folder; every POST is antiforgery-protected with a body limit; a GET
  only reads. `/setup` now redirects to the sign-in page with `ReturnUrl=/admin/welcome`, so the first sign-in lands in the wizard (setup itself
  still does not sign anyone in). The existing exact-redirect assertion in `SetupTests` and the page-to-file mapping in `AdminAccessTests`
  were updated for this; the route enumeration now lists every wizard route.
- **"Wizard completed"** is `SiteProfile.OnboardingCompletedAt`, a nullable timestamp (also the concurrency token for publishing). The migration
  backfills it for any profile row that exists, so deployed sites are not sent into the wizard. A site whose profile row did not exist at migration
  time (an upgrade straight from before Phase 0, or a brand new database) counts as unfinished: it only gets a dashboard prompt.
- **The admin shell does not redirect an unfinished wizard.** Redirecting could trap an owner and would break every admin test; the dashboard shows
  "Continue setup" / "Skip setup" instead, and no other page is blocked.
- **Draft** is a JSON column (`OnboardingDraft`) on the existing profile row (D3 kept, additive migration with `Location`). `ToOptions` ignores it, so
  the provider, public pages, sitemap, JSON-LD, `/cv.pdf` and `/og.png` cannot see it. Draft saves deliberately do not prime the provider or evict the
  output cache (nothing public changed); publish does both through `ProfileEditor.AfterCommitAsync`.
- **Publish** is one transaction: profile row first (so an overlapping publish blocks on it and fails the concurrency check), then sample rows, then
  commit. The draft is cleared in the same save, so a double click or replay finds no draft and changes nothing. A failure part-way rolls back the
  profile and rows, removes the promoted photo copy and keeps the draft.
- **Starter content** is seed-if-empty per table and never updates or deletes (the seeder is called with `IfEmpty` regardless of `Seed:Mode`). Sample
  content brings rows only, never a profile identity or profile text. Software has no `starter.json`: its manifest says `"starter": {"source": "seed"}`,
  which loads the built-in demo persona through the existing seeder, so nothing is duplicated. On a non-empty site the tables that already have rows are
  skipped and the review step says which. The software sample rows are the existing fictional persona; they are not marked "sample" inline (the
  seed files are covered by the parity snapshots), the wizard labels them fictional.
- **Start-up seeding is deferred on a claimable, unfinished install** (a `Setup:Key` is set or an owner exists, and the wizard is not finished):
  otherwise the first start would already have loaded the demo persona and "blank" could not be honest. It also **never runs again on a site the wizard
  finished**: a second additive migration column, `OnboardingOutcome` ("published" or "skipped"), is set by publish and skip and left empty by the backfill, so
  a restart cannot refill a blank site (review finding). Sites that existed before the wizard, unclaimable legacy installs and `Seed:Mode=Authoritative`
  seed as before. Skipping the wizard on a first run loads the demo content into empty tables, which restores what a new install showed before the wizard.
  The start-up step is `FirstRun.SeedAtStartupAsync`, tested by replaying it after blank, sample and skipped completions.
- **First-run publish replaces the placeholder persona** from the `Site` config (hero, metrics, About, links, phone, light photo) with a plain hero built
  from the owner's answers (title as eyebrow, name as headline, summary as lede) and the summary as the first About paragraph. A re-run changes only
  template, name, short name, title (and the role line when the title changed), email, location, summary, address, accent and photo.
- **Photo.** Uploads use `ImageProcessor` and `UploadStore`. A draft photo lives in a hidden draft area (no public URL) and is served to the owner only;
  publish copies it to a public file, then deletes the draft and the replaced upload; stale drafts are purged on later uploads. `/uploads` now serves
  flat files only (`UploadFileProvider`), because the physical provider's dot-prefix exclusion does not hide a file inside a dotted folder. Both theme
  photo fields are set, since the pages hide the dark photo in the light theme when no light photo exists; `ProfileEditor.SetPhotoAsync` keeps a light photo that
  equals the replaced one in step (replace or remove on the Photo page), and leaves a separately chosen light photo alone. There is no client-side crop: the admin policy
  allows no script, so the step is a server-rendered preview and the site shows a centred circle.
- **Draft preview** reuses `?preview=<key>` with `&draft=1`: same owner-session check, noindex, no-store, no analytics, no canonical. A wrapper around the
  profile provider returns the draft identity only when the middleware set it for that request after the session check; sitemap, CV and share image never
  go through the preview middleware. The preview shows identity, template, accent and photo; sample rows appear at publish.
- **Accent.** `theme.accents` in the manifest is the allow-list; the stored `AccentColor` is the key, and a key the template does not list is ignored at
  render. One `<style>` sets only `--c-accent` and `--c-accent-strong` (specificity raised above the theme blocks); nothing is emitted when unset, so
  software output is byte-identical (the parity snapshots are untouched). Contrast of every tone is tested against the template's own backgrounds.
- **Suggestions and thumbnails come from the manifest** (`heroSuggestions`, `preview.png` by fixed name through the catalog), validated strictly and
  described in the schema and the authoring guide. Suggested text is published as written if the owner keeps it, so it is written to stand alone.
- **Contact page.** Location is shown there and in Person markup only when set, and the "Online" card is hidden when there are no links (software output
  is unchanged because the demo persona has links).
- **Re-run** keeps an edited short name and role line unless the name or title changes; a renamed owner gets a new short name but the first-run hero headline stays
  until edited in `/admin/profile`. The review step's site address is prefilled from the request address, which behind a proxy may be an internal host (documented).
- **Owner decision: residual blank edge.** An install first started with no `Setup:Key` and no owner seeds the demo persona at that start; if the key is
  added later and the owner publishes "Start blank", those rows remain (blank only avoids adding rows). Documented in the new-user guide.
- **Rollback.** Do not roll `AddOnboardingOutcome` back on a live site: Down then Up drops the outcome, and the next restart would refill wizard-finished blank
  sites with demo content. The outcome is recorded on a first run only, so a re-run on a pre-wizard site leaves it empty.
- **Upgrade note.** `heroSuggestions` is a required manifest field: a custom manifest without it is skipped and the site falls back to the default template.
- **Not done / limits.** No crop. No per-step "back" without a full page load. Sample rows are not previewed before publish. Two simultaneous publishes are
  covered by the concurrency token and a unit-level test, not by a parallel HTTP test; the `Conflict` outcome path of `PublishAsync` itself is not exercised
  by a test.

### Phase 4: Content management UI (replaces seed editing)
Split so users are never left with content they cannot edit:
- **4a (release gate for the wizard):** Experience, Skills, Projects/Work, Certifications.
  List with reorder, create/edit/delete, slug generation, image upload for hero and
  screenshots, validation, unsaved-change warning. **Status: done** (pending independent review); notes below.
- **4b:** Blog (Markdown editor with preview, drafts), Talks, Testimonials, hero/metrics/about
  editing, section on/off and ordering.
- **4c:** JSON export/import of all content. This replaces hand-editing seed files and gives
  backup and portability.
Wizard and 4a ship together; 4b and 4c follow.

Implementation notes for 4a (decisions worth reviewing):
- **Routes.** `/admin/content` (hub with counts), and per section `/admin/content/{experience|skills|projects|certifications}` (list, with the Move
  up / Move down POST handler), `/edit/{id?}` (create and edit), `/delete/{id}` (GET shows the confirmation, POST removes), plus
  `/admin/content/projects/images/{id}` for gallery images. All sit in the deny-by-default admin folder, every POST is antiforgery-protected and
  has a body limit, a GET never changes state; `AdminAccessTests` lists every route and `ContentSecurityTests` replays anonymous, no-token,
  stale-session and GET-with-handler requests against all of them and compares the whole content tables before and after.
- **Concurrency.** Each of the four tables gets a nullable `ConcurrencyStamp` (32 character GUID set by the app on every admin write, an EF
  concurrency token), not a SQL Server `rowversion`: like `SiteProfile.OnboardingCompletedAt` it works the same on SQL Server and on the SQLite the
  tests use, and existing rows simply have no stamp until their first edit. A stale form, a stale confirmation, a stale gallery page and a reorder that
  loses a race are all refused with a message that says nothing was saved; the owner's typing stays on the page. `Project.HeroImageAlt` is the other
  additive column (nullable); public pages use it only when set, so Software output is unchanged (the parity snapshots pass untouched).
- **Template-driven editors.** A new optional `editor` block in the manifest (schema, validator, schema-drift test, both shipped manifests, authoring
  guide) gives each section an `itemLabel` and per-field `label`, `hint` and `hidden`. `EditorFields` holds neutral fallback wording (a deliberate exception
  to "copy has no defaults": these are structural field names, and every profession word still comes from the manifest or `copy`; projects reuse the
  `copy.projects` headings and certification statuses reuse `copy.certifications`). Fields an entry cannot exist without can be relabelled, never hidden
  (validated). A hidden field is not drawn, not validated and not read from the post, and its stored value is untouched by every save (tested with a
  template carrying unique wording, in all four sections). Sensitive templates show `guidance` on the hub and every editor. A section the template does
  not use stays editable, with a note that the public site does not show it.
- **Ordering.** The existing `SortOrder` columns, no new column. Move up / Move down are POST buttons (named after their row, disabled at the ends,
  keyboard operable); a move renumbers the scope consecutively and writes only rows whose position changed, so rows that shared a position are
  separated. Skills order within their group, like the public page and the CV. New entries are appended (Skills: end of their group). **No drag and
  drop**: it needs script, the admin CSP allows none, and the buttons are the mandatory fallback; left out on purpose.
- **Unsaved-change warning** is `wwwroot/js/admin-unsaved.js`, loaded only by the edit forms. The admin CSP gains `script-src 'self'` on
  `/admin/content/{section}/edit` only (`AdminAuth.AllowsScript`); tests assert every other admin page still has no `script-src`, no
  `unsafe-inline`/`unsafe-eval`, and that no admin page carries an inline script or handler. The form works without the script.
- **Validation** is server side (`FormCheck`, `ContentRules`): required fields, lengths (column maxima, plus caps on unbounded columns), plain text
  only (a tag, comment or declaration start is refused; a bare `<50ms` is allowed), control characters refused, address fields http/https only
  (a site-relative value is accepted only when it is exactly the one already stored, because older seeded entries use `/img/...`), enums from
  allow-lists, free-text dates (`2021`, `Mar 2021`, `2021-03`) parsed for order and for the future (earned certifications cannot be future-dated; the
  clock is `TimeProvider`). Skill names, certification names and role (company, title) must be unique case-insensitively, because the authoritative
  seeder keys on them. No field of these four sections is Markdown (the public pages print project text as plain text), so `Md` is not involved.
- **Project addresses.** Generated from the title (accents folded, everything else a hyphen), numbered on collision, reserved words suffixed; typed
  addresses must be lower-case letters, digits and single hyphens and not reserved or taken; an edit keeps the current address unless the owner types a
  different one (an emptied box keeps it too). A renamed or removed address answers the ordinary 404 (no redirect is kept). A unique-index violation
  from a race is mapped to the same friendly message, but only the pre-check path is covered by tests.
- **Images.** Cover image on the project form (replace / remove), gallery images one per request on the Images page (up to 12, description required,
  reorder, remove); all through `ImageProcessor` and `UploadStore`, so names are generated and the uploads folder stays flat. An upload saved for a save
  that fails is deleted again; a replaced, removed or deleted-with-its-project upload is deleted only if no other project or the profile photo still
  points at it, and addresses that are not generated `/uploads/...` names (the bundled images) are never touched. Alt text is required for any image
  the owner uploads (the cover's is `HeroImageAlt`, a gallery image's is its caption). Project and certification **proofs** are kept but not editable yet.
- **Seeding.** Nothing in the editors seeds. Because wizard-finished sites never seed at start-up, emptying every table stays empty across restarts
  (`ContentSeedingTests` replays `FirstRun.SeedAtStartupAsync` for blank, sample and skipped sites). A site that existed before the wizard keeps
  the old behaviour: an emptied table is refilled from the seed files at the next start. That is documented in the user guide and left as an owner decision.
- **Known gaps.** The CV PDF of a project that has impact lines cannot be rendered on a host without the fonts it names (the "▸" glyph; the same limit the
  earlier tests note), so the end-to-end test builds a project without impact lines for the PDF check. The editors have not been run against a real SQL
  Server (tests and the browser check use SQLite). Parallel requests from one client against the test host's single shared in-memory SQLite connection ended
  with the session rejected (probably the session check failing closed on a busy connection; not investigated further), so the tests issue requests one at a time.
- **For 4b / 4c.** 4b adds editors for blog posts, talks and testimonials, hero / metrics / About editing, and section on/off and ordering; the content
  pages, `ContentEditor`, `FormCheck`, the `editor` manifest block and the stamp pattern are reusable for those. 4c (JSON export and import) should
  export `ConcurrencyStamp`-free rows and decide how imported rows interact with the unique name and slug rules above.

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

## 6. Template rules

See [template-authoring-guide.md](template-authoring-guide.md). Templates are data plus an
archetype, validated by automated tests, so a new profession needs no code changes.

## 7. Working agreement

- Implementation by Sonnet 5.5; each phase gets an independent review by Opus 5.5 before merge,
  and review findings are fixed before the next phase starts.
- No tool or assistant names in commit messages, code comments, or PR text.
- One branch and PR per phase (sub-phases where noted), small commits, `dotnet build` warning-clean
  and `dotnet test` green before review.
