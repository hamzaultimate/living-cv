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
existing software template working unchanged. *Recorded exception (4b-2):* one additive nullable `SiteProfiles.ConcurrencyStamp` column (migration
`AddProfileConcurrencyStamp`, a single `ALTER TABLE ... ADD ... NULL`), because the profile is edited from several tabs and pages and would otherwise be last-write-wins. No rename, no data change.

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
  seed as before on the first pass; legacy sites are pinned afterwards by the seeding marker (see "Seeding" in the Phase 4a notes). Skipping the wizard on a first run loads the demo content into empty tables, which restores what a new install showed before the wizard.
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
- **4b (split in two PRs):** **4b-1 done, pending review** = Talks, Testimonials and Blog (Markdown with safe server-side preview, drafts); **4b-2 in progress** = hero / metrics / About editing and
  section on/off and ordering: **stage A (hero, metrics, About and the profile stamp) done, pending review; stage B (section settings) done, pending review**. Status, notes and the 4b-2 brief are in "Phase 4b: status and hand-over" below.
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
  (validated). A hidden `screenshots` setting removes the Images link and makes the Images page and its posts refuse. A template that hides `current` can still end a role: saving an end date clears a stored current flag, otherwise the flag is kept. A hidden field is not drawn, not validated and not read from the post, and its stored value is untouched by every save (tested with a
  template carrying unique wording, in all four sections). Sensitive templates show `guidance` on the hub and every editor. A section the template does
  not use stays editable, with a note that the public site does not show it.
- **Ordering.** The existing `SortOrder` columns for positions (the list stamp below is the only new storage). Move up / Move down are POST buttons (named after their row, disabled at the ends,
  keyboard operable); a move renumbers the scope consecutively and writes only rows whose position changed, without restamping them (a form open for another row stays valid, while the update still checks the stamp it loaded, so a move over a concurrent edit of the same rows is refused). **List stamp** (replaces the earlier trade-off that two overlapping moves, or a move racing a skill group change, could leave a duplicate `SortOrder`): a small additive table `ListStamps(Section PK nvarchar(40), Stamp nvarchar(32) concurrency token)`, one row per section list, created on the section's first order change (a GET only reads it and shows `0` when there is no row). Every write that can change positions names the stamp it saw and replaces it in the same `SaveChanges`, so the update only matches that stamp (same mechanism as the row `ConcurrencyStamp`, works on SQL Server and SQLite) and an overlapping writer gets `DbUpdateConcurrencyException`, which `CommitAsync` turns into `SaveOutcome.Conflict`. Moves post the stamp the list page was loaded with (hidden `listStamp` field, read before the rows on GET) and are refused with the existing "The list was changed somewhere else, so it was not reordered. It is shown again as it is now." message if it is stale or missing. Creates (Experience insert-at-top; Projects, Certifications and Skills appends) and a Skill group change take the current stamp with `ContentEditor.BeginOrderChangeAsync(section)` **before** they read the positions: their result does not depend on what the editor saw, so a create form opened before a move stays valid, but any order change committed between that read and the commit makes the save a conflict (a new entry is shown its form again with its input and "The list changed while saving, so nothing was saved. Press Save again."; a skill group change shows the generic "nothing was saved" message). Ordinary edits that do not change the order (including a skill edit that keeps its group) and deletes do not touch the list stamp: a delete leaves a gap and renumbers nothing, so it cannot create a shared position, and a move that names a deleted row fails on that row's own stamp only if it has to rewrite that row; either way no two rows can end up in one position. Start-up seeding in `Seed:Mode=Authoritative` and the wizard's sample loading (empty tables only) also do not replace the list stamp: they run when no editor is in the middle of a write to those tables, they renumber from the seed files, and a move made from an older page then still applies to the new rows, which cannot create a shared position (harmless). One stamp per section list is deliberate (Skills order within a group, but a single stamp per section is simpler and only costs a refused move when two groups are reordered at the same moment). Order of calls matters: `GuardOrderAsync` throws if other changes are already staged, because creating the section's stamp row is its own save. 4b reuses this for Talks, Testimonials, Blog and section ordering by naming a section key (no schema change); tests: `ListOrderConcurrencyTests` runs real overlapping requests (a test hook runs a complete second request right before the first saves) and two `ContentEditor`s over one database, for two moves, move vs skill group change (both ways), move vs insert-at-top (both ways), two inserts, two group changes and two appends. Rows that shared a position before this existed are separated by the next move. Skills order within their group, like the public page and the CV. New entries are appended (Skills: end of their group), except Experience, where a new entry is **inserted at the top**: the admin list, the public page, the home page and the CV all order by `SortOrder` ascending, so the new row takes `min(SortOrder) - 1` (1 in an empty list) and no other row is written (their positions, stamps and the demo persona's order stay as they were, which keeps the Software output identical). Positions below 1 are tidied by the next move. Chosen over shifting every row up by one because that would rewrite every row (and race the row stamps) for no visible difference. **No drag and
  drop**: it needs script, the admin CSP allows none, and the buttons are the mandatory fallback; left out on purpose.
- **Unsaved-change warning** is `wwwroot/js/admin-unsaved.js`, loaded only by the edit forms. The admin CSP gains `script-src 'self'` on
  `/admin/content/{section}/edit` only (`AdminAuth.AllowsScript`); tests assert every other admin page still has no `script-src`, no
  `unsafe-inline`/`unsafe-eval`, and that no admin page carries an inline script or handler. The form works without the script. A form shown again after a refused save starts dirty, and the project Images page carries the warning too (same CSP scope, `AllowsScript`). Checked in Chromium for the skill edit form (prompt after an invalid re-render, none after a save or on an untouched form); the Images page script path is covered by markup tests only.
- **Validation** is server side (`FormCheck`, `ContentRules`): required fields, lengths (column maxima, plus caps on unbounded columns), plain text
  only (a tag, comment or declaration start is refused; a bare `<50ms` is allowed), control characters refused, address fields http/https only
  (a site-relative value is accepted only when it is exactly the one already stored, because older seeded entries use `/img/...`), enums from
  allow-lists, free-text dates (`2021`, `Mar 2021`, `2021-03`) parsed for order and for the future (earned certifications cannot be future-dated; the
  clock is `TimeProvider`). Skill names, certification names and role (company, title) must be unique case-insensitively, because the authoritative
  seeder keys on them, and a `|` is refused in a role's company or title because that key joins them with one (a value the row already has is kept as stored). Two simultaneous identical creates can still both pass the pre-check (no unique index on these names): a known gap. No field of these four sections is Markdown (the public pages print project text as plain text), so `Md` is not involved.
- **Project addresses.** Generated from the title (accents folded, everything else a hyphen), numbered on collision, reserved words suffixed; typed
  addresses must be lower-case letters, digits and single hyphens and not reserved or taken; an edit keeps the current address exactly as stored (an unchanged legacy address is never re-validated) unless the owner types a
  different one (an emptied box keeps it too). A renamed or removed address answers the ordinary 404 (no redirect is kept). A unique-index violation
  from a race is mapped to the same friendly message, but only the pre-check path is covered by tests.
- **Images.** Cover image on the project form (replace / remove), gallery images one per request on the Images page (up to 12, description required,
  reorder, remove); all through `ImageProcessor` and `UploadStore`, so names are generated and the uploads folder stays flat. An upload saved for a save
  that fails is deleted again; a replaced, removed or deleted-with-its-project upload is deleted only if no other project or the profile photo still
  points at it, and addresses that are not generated `/uploads/...` names (the bundled images) are never touched. Alt text is required for any image
  the owner uploads (the cover's is `HeroImageAlt`, a gallery image's is its caption). Project and certification **proofs** are kept but not editable yet.
- **Seeding.** Nothing in the editors seeds. Because wizard-finished sites never seed at start-up, emptying every table stays empty across restarts
  (`ContentSeedingTests` replays `FirstRun.SeedAtStartupAsync` for blank, sample and skipped sites). A site that existed before the wizard is pinned by a
  nullable `SiteProfile.ContentSeededAt` (migration `AddContentSeededMarker`, additive, no backfill). Start-up records it with one conditional UPDATE only
  after the seeder has run over every table (`FirstRun.MarkContentSeededAsync`), and `ShouldDeferContentSeedAsync` defers once it is set (except
  `Seed:Mode=Authoritative`, which still seeds as configured). Why this marker and not "first admin content write": the first start-up pass is the event
  that defines "this site has been seeded", it needs no change in the editors (4b's new tables are covered automatically), and a legacy site whose owner
  never writes content needs no marker to behave as before. Ordering: the first pass behaves exactly as before for partly empty tables (IfEmpty fills only
  the empty ones); the marker is written after the pass, so a crash part-way, or a seed folder that is missing or a seed file that is present but cannot be read (the seeder reports whether every present file was read; a deleted individual file is an intentionally empty table and does not block the pin), leaves it null and the next start finishes the job; the migration does not
  backfill, so a site that never completed a pass is not pinned. A new install with no profile row yet (a development checkout, no `Setup:Key`) is marked
  by a second call in `Program` once the profile exists; a missing seed folder is not recorded as a pass. Unclaimable legacy installs go through the same
  path. Limits: a legacy site that emptied a table on the old version is refilled once at the first start after this upgrade (it cannot be told from
  one never seeded) and then stays empty; do not roll `AddContentSeededMarker` back on a live site (Down then Up clears the marker and the next start
  would refill emptied tables once). `LegacySeedPinTests` replays the start-up step after delete-all for legacy, published, skipped and Authoritative
  sites over every content table (add new tables to its list).
- **Known gaps.** The CV PDF of a project that has impact lines cannot be rendered on a host without the fonts it names (the "▸" glyph; the same limit the
  earlier tests note), so the end-to-end test builds a project without impact lines for the PDF check. The editors have not been run against a real SQL
  Server (tests and the browser check use SQLite). Parallel requests from one client against the test host's single shared in-memory SQLite connection ended
  with the session rejected (probably the session check failing closed on a busy connection; not investigated further), so the tests issue requests one at a time.
- **For 4b / 4c.** 4b adds editors for blog posts, talks and testimonials, hero / metrics / About editing, and section on/off and ordering; the content
  pages, `ContentEditor`, `FormCheck`, the `editor` manifest block and the stamp pattern are reusable for those. 4c (JSON export and import) should
  export `ConcurrencyStamp`-free rows and decide how imported rows interact with the unique name and slug rules above.

### Phase 4b: status and hand-over (4b-1 done, pending review; 4b-2 stages A and B done, pending review)
**Split.** 4b-1 = Talks, Testimonials and the Blog editors (this PR). 4b-2 = hero / metrics / About editing and section on/off + ordering (a later PR; **stages A and B done**). 4c (JSON
export / import) is untouched. Everything follows the 4a pattern: `ContentPageModel` / `ContentListModel` / `ContentDeleteModel`, `FormCheck`, `ContentEditor` (row stamp, list stamp,
`CommitAsync` -> `ProfileEditor.AfterContentCommitAsync`), the manifest `editor` block, `admin-unsaved.js`.

**Entity and field inventory (as it was before 4b).**

| Section | Entity | Fields | Public order | Notes |
|---|---|---|---|---|
| Talks | `Talk` | Title, Type (`Video`/`Talk`), YoutubeId, Event, Date (`DateOnly?`), Url, SortOrder | `Date` descending, then `SortOrder`, then `Id` | No image. YoutubeId goes into an iframe address. Seeder key: `Title`. |
| Testimonials | `Testimonial` | Quote, Author, AuthorTitle, SourceUrl, Relation, Featured, SortOrder | `SortOrder`, then `Id` | No image. Quote is escaped plain text. `Relation` and `Featured` are stored but shown nowhere. Seeder key: `Author` + first 40 characters of `Quote`. |
| Blog | `BlogPost` | Title, Slug, Description, Date, Tags, Draft, Cover, BodyMarkdown | `Date` descending, then `Id`; drafts hidden | Unique `Slug` index; no position column. Seeder key: `Slug`. |
| Hero / metrics / About | `SiteProfile` JSON columns | Hero (4 text), Metrics (value, unit, label), About (headline, paragraphs, value props, currently note, focus) | n/a | 4b-2 stage A (done) |
| Sections | `SiteProfile.Sections` JSON | Key, Label, Enabled, SortOrder | nav and home order | 4b-2 stage B (done) |

**Public surfaces each type feeds.** Talks: `/talks` only. Testimonials: the home page block (`Blocks/_Testimonials`). Blog: `/blog`, `/blog/{slug}`, `/sitemap.xml` (posts, with the date as
`lastmod`), `BlogPosting` + breadcrumb JSON-LD on the post page, the post's cover as its share image. Nothing else reads posts (no feed, no search, no tag pages, no related posts, no
home-page block, no per-post share image, not the CV); the repository filters drafts (`GetBlogPostsAsync`, `GetBlogPostAsync`), so every surface above is covered by two queries.

**Implementation notes for 4b-1.**
- **Routes.** `/admin/content/{talks|testimonials|blog}` (list), `/edit/{id?}`, `/delete/{id}`. Blog preview is `POST /admin/content/blog/edit/{id?}?handler=Preview` on the edit page (no new route, no script,
  nothing read from the database but the current cover, nothing written; the response is the edit page, so it carries the usual `no-store` / `noindex` headers and the form keeps what was typed).
- **Migrations (all additive, nullable, read in the generated SQL):** `AddTalkTestimonialStamps` (`ConcurrencyStamp` on `Talks` and `Testimonials`) and `AddBlogEditing` (`ConcurrencyStamp`
  and `CoverAlt nvarchar(300)` on `BlogPosts`). `IStampedContent` (Id + stamp) was split out of `IOrderedContent` so the blog, which has no position, can use the shared list / delete models.
- **Ordering.** Talks and Blog are ordered by date on the public pages, so their lists show that order and have **no Move buttons** (`CanReorder` false; a posted Move is refused and writes nothing; new
  talks append a tie-break position and take no list stamp). Testimonials append and move with the list stamp (section key `testimonials`). `Id` tie-breaks were added to the three public queries (ties were
  undefined before); the Testimonials one cannot be proven by a test on SQLite, which returns insertion order anyway.
- **Talks.** Title required (200); type from the enum allow-list with manifest wording (`typeVideo`, `typeTalk`); event 200; date as `yyyy-MM-dd`, month or year; YouTube field takes an 11 character id or a
  watch / youtu.be / embed / shorts link and stores the bare id (the public page only embeds a stored id of that shape); link http(s) only. Unique title, compared in memory with `OrdinalIgnoreCase` like the seeder.
- **Testimonials.** Quote required, plain text, 2000 (the column length); author 120; role 200; source link http(s). Unique author + first 40 characters of the quote (the seeder key).
- **Blog.** Title (200; **a duplicate title is not refused**, unlike Talks, because the seeder keys posts by slug: the slug gets a numeric suffix), slug (built from the title with `Slugs.From(title, "post")`, unique case-insensitively, reserved words and path tricks refused, an unchanged legacy slug never re-validated, an emptied
  box keeps it, a unique-index race shows the friendly message), description (plain, 300), date (`yyyy-MM-dd`, 1990 to 2100; a future date is allowed because the public page already shows it),
  tags (comma or line separated, up to 8, 30 characters, letters / digits / space and `. + # & _ -`), cover (image pipeline only, alt text required for an upload, replaced / removed / deleted files are removed
  only when no project, profile photo, post cover or post text still points at them), text (Markdown, 60 000 characters, no control characters), **Draft** (a new post starts as a draft; a post saved without
  the field stays a draft). Publishing is the Draft checkbox on the edit form; there is no list button (kept out on purpose: one path to publish).
- **Safe Markdown (`SafeMd`).** `Md.ToHtml` (raw HTML allowed) is gone; `SafeMd.ToHtml` is the only renderer and is used by the public post page **and** the preview. Markdig with `DisableHtml`, pipe tables, auto
  identifiers, auto links, emphasis extras, list extras, smarty pants and referral links (`rel="noopener nofollow ugc"`); no generic attributes, media links, math, diagrams, custom containers or footnotes. After parsing, every
  link, image and `<scheme:...>` autolink address is decided on the decoded value (control characters and whitespace removed, scheme lower-cased): relative, `http`, `https` (and `mailto` for links) pass; anything else is
  rendered as its text. **It changed no byte of the Software parity snapshots** (and none of the shipped posts' output): `SafeMdTests` renders every shipped post body with the old trusted pipeline and the safe one and requires identical HTML, and the parity
  snapshots pass untouched. That holds for the shipped posts only: an operator's own `blog.json` with links (now carrying `rel`) or raw HTML (now escaped) renders differently. Automatic heading ids are not prefixed, because that would change the bytes
  of the shipped posts (`<h2 id="the-symptom">`); a generated id can therefore equal a layout id (`main-content`, `theme-btn`): accepted, documented.
  `SafeMd` never throws (nested text and more than 200 headings fall back to the encoded text in a `<pre>`; the editor and preview refuse such text) and keeps renders by the hash of the text. `Seed:Mode=Authoritative` now copies `CoverAlt` too, and deletes browser-written posts that are not in `blog.json`, leaving their uploaded covers orphaned in storage. Known cost: a post cannot contain raw HTML, footnotes, definition lists or task lists any more (the shipped posts use none); say so to owners who ask.
- **Template wording.** `editor.talks`, `editor.testimonials`, `editor.blog` in the schema, `TemplateEditor`, `EditorFields`, the validator (required fields cannot be hidden: `title`; `quote`, `author`; `title`, `slug`, `date`,
  `body`, `draft`), both shipped manifests and the authoring guide. Healthcare keeps Talks and Blog off in the public template: the editors stay reachable with the "not shown" note and the guidance.
- **Tests.** `TalkEditorTests`, `TestimonialEditorTests`, `BlogEditorTests` (drafts on every surface, transitions with the cached sitemap, slug rules, covers and orphan cleanup, preview, XSS corpus on preview
  and public page, JSON-LD encoding, stamps), `TalkTestimonialTemplateTests`, `BlogTemplateTests`, `ContentSuiteEndToEndTests` (both templates, create -> public -> unpublish / delete), `SafeMdTests`,
  `BodyLimitCoverageTests` (reflection: every admin page model with a POST handler has `[BodyLimit]`), and the new routes in `AdminAccessTests`, `ContentSecurityTests`, `ContentAccessibilityTests`, `ContentSeedingTests`.
  The test host's save hook (`AdminApp.BeforeOrderedSave`) replays real overlapping requests for any stamped row.

**Known gaps / owner decisions.** `Testimonial.Relation` / `Featured` have no field (shown nowhere). No list-level publish button. Preview does not load external pictures (the admin CSP is `img-src 'self'`). The
preview and the public page are rendered by the same function but the preview sits in admin styles, not the site's Tailwind article styles. Raw HTML in posts is deliberately unsupported. Nothing has been run against a
real SQL Server (tests use SQLite; the SQL scripts were read). No browser check of the new pages was done.

#### Implementation notes for 4b-2 stage A (hero, metrics, About, profile stamp)
- **Routes.** `/admin/profile/hero`, `/admin/profile/metrics`, `/admin/profile/about` (one page model each under `Pages/Admin/Profile`, deriving from `ProfilePartModel`), linked from the dashboard and from `/admin/profile` with the
  template's words. The identity page (`/admin/profile`) **no longer edits** the hero, metrics or About text (`ProfileForm` lost those fields; its save leaves them untouched), so a field has exactly one editor. Every POST is antiforgery-protected with a
  `[BodyLimit]` (64 KB hero and metrics, 128 KB About, 256 KB identity), a GET only reads, the pages sit in the deny-by-default admin folder. Script: `AdminAuth.AllowsScript` now also allows `script-src 'self'` on exactly these three
  paths (for `admin-unsaved.js`); the identity page and the dashboard stay script-free. `_Field`, `_ErrorSummary` and `_Guidance` moved from `Pages/Admin/Content` to `Pages/Admin` so both folders share them; `FieldVm.For` is the one place a field is built.
- **Decision recorded: a profile stamp (D3 exception).** `SiteProfile.ConcurrencyStamp` (nullable `nvarchar(32)`, EF concurrency token, same shape as the content stamps). Written (replaced) by: the identity save, hero, metrics, About, a new photo, a template switch,
  the wizard's publish and `Seed:Mode=Authoritative`'s overwrite. **Not** replaced by the wizard's draft saves (the draft is not public), by the wizard's skip, or by content rows (they are other tables). A form posts the stamp it was loaded with (`Form.Stamp`, hidden);
  `ProfileEditor.UpdateAsync(apply, seenStamp)` names it as the original value, so the `UPDATE` only matches that stamp; a stale or missing stamp is `SaveOutcome.Conflict` with nothing written (a row that was never stamped matches an empty stamp once). The refusal
  re-draws the page with what was typed and "Nothing was saved." (focus on the alert). Writes that do not come from a form (photo, template) have no earlier copy to be stale against: they replace the stamp and, if another write lands between their read and write, start again from
  the stored row (up to three tries) instead of failing. A consequence worth knowing: after a photo change or a template switch, a hero / metrics / About form that was open is refused, because the profile did change. The migration SQL was read (one `ALTER TABLE [SiteProfiles] ADD [ConcurrencyStamp] nvarchar(32) NULL`); it was not run against SQL Server.
- **Wizard interplay.** (1) Draft saves used to be a tracked `SaveChanges` that a moved stamp would have made fail silently (the old "published in the meantime" path would then drop the answer); they are now one `ExecuteUpdate` of the draft column that still requires the
  wizard to be unfinished, so an answer is never lost to an unrelated edit. (2) Publish replaces the stamp. A form opened before a publish is refused after it (tested). (3) The first-run publish replaces the placeholder persona **per part** (hero, metrics, About) and only
  while that part still equals what the `Site` config supplied; a part the owner already edited is kept. Edits made before publishing are therefore not clobbered, and the publish is not clobbered by a stale form. The comparison needs `IOptions<SiteOptions>` in `OnboardingService`. A real HTTP overlap of publish and edit cannot be shown on the single shared in-memory SQLite connection of the tests
  (the publish transaction swallows the other request's write), so that direction is tested sequentially.
- **Manifest.** `editor.hero`, `editor.metrics`, `editor.about` (schema, `TemplateEditor`, `EditorFields`, `TemplateValidator`, drift test, both shipped manifests, authoring guide). Keys: hero `heading`, `eyebrow`, `headline`\*, `headlineAccent`, `lede`; metrics `heading`, `value`\*, `unit`, `label`;
  about `heading`, `headline`\*, `paragraphs`, `valueProps`, `valuePropTitle`, `valuePropBody`, `currentlyNote`, `focus` (\* cannot be hidden). `heading` (page title = label, line under it = hint) and `valuePropTitle`/`valuePropBody` are wording-only keys: new `EditorFieldInfo.WordingOnly`, which the validator refuses to hide.
  `itemLabel` names one row ("Add metric", "Metric 2", "Add card"). Every noun in a message (page title, save message, "changed somewhere else", "Metric 2 value is required", "Metric 3 added", limits, empty state) is built from these words; `EditorView.Heading` is the block's name. Neutral defaults live in `EditorFields`. The About editor shows the "not shown by your template" note when the template disables the About section; the hero and metrics are home blocks and never switched off.
  Software words them "Home page hero", "Headline metrics", "About page"; Healthcare "Home page introduction", "Key figures" (no unit), "About you", with no highlighted headline part, and its guidance on every page. A hidden field is not drawn, not validated, not read from the post and keeps its stored value; a hidden metric column follows its row (each row posts the position it had in the stored list as `Source`; an out of range, negative or repeated source borrows nothing).
- **Row editing without script.** Metrics (up to 8) and About cards (up to 6) have Move up / Move down / Remove / Add buttons. They are POST handlers (`?handler=Move|Remove|Add`) that return the page with the typed rows rearranged **and write nothing**; Save is what writes. The "Save" button is first in the form (so Enter in a text box saves) and is repeated at the end;
  the buttons are named after their row ("Move up: Metric 2"), the ends are disabled, a polite status region says what happened and "not saved yet", focus lands on the moved row's button (or the new row's first box, or "Add"), and the unsaved-changes warning starts on. Paragraphs and focus areas stay text areas (one blank-line separated paragraph, one line per focus area).
- **Validation.** `FormCheck` (plain text only: a `<` followed by a letter, `/`, `!` or `?` is refused; control characters refused; NUL is refused by the framework with 400), new `FormCheck.Paragraphs` and a `context` argument so a row's message says which row. Limits: hero fields 300 (lede 800); metric value 20, unit 20, label 80; About headline 300, paragraph 2000 (10), card title 120 and text 500, note 300, focus line 120 (30).
  Headline (hero and About) and the metric value are required. None of these fields is a link, so there is no URL rule to apply; the public pages print them through Razor's encoder as text (no `Html.Raw` on any of them), which the tests assert for a corpus of text that passes validation (`5 < 6 & 7 > 3`, quotes, entities, `javascript:...`, Razor and template syntax, Markdown).
- **Public surfaces.** The hero is read by the home page (both archetypes) and the share image (`/og.png` tagline); metrics by the home page; About by `/about`. The sitemap, JSON-LD, CV and its PDF do not read these fields, so they are unchanged by an edit (asserted), and the output cache is evicted by every save (asserted with the share image). The Software parity snapshots pass unedited.
- **Tests.** `ProfileStampTests` (stamp replaced by every kind of write, draft saves, photo overlapping an edit, stale / missing / forged stamps and real overlapping requests for the four forms, never-stamped rows), `ProfilePartEditorTests` (persistence, public follow, limits, row buttons, markup and control-character corpus on every text field, rendering corpus, first-run publish), `ProfilePartTemplateTests` (unique-word manifest: labels, hints, every message; hidden fields; hidden metric column; About disabled; both shipped manifests),
  `ProfileAccessibilityTests`, `ProfileEndToEndTests` (both templates on a blank wizard-finished site), the new routes in `AdminAccessTests`, `ContentSecurityTests` (anonymous, no token, stale session, GET never mutates with two rows or values per table, real stamps, body caps, script scope) and `BodyLimitCoverageTests`. `AdminApp.OverlapAsync` (the save hook now also fires for the profile row) is the shared way to overlap two real requests.
- **Mutations proven (scratch copy only).** Each of these was applied alone to a copy of the repository outside the working tree and the relevant tests run; every one was caught (no survivors): the owner check (an `[AllowAnonymous]` on the metrics page: `AdminAccessTests`, `ContentSecurityTests`), antiforgery (`[IgnoreAntiforgeryToken]` on the About page: the no-token replay and the editor tests), the stamp check (seen stamp ignored; stamp not replaced: 11 to 13 tests),
  hidden-field data loss (hero, metric column, About: `ProfilePartTemplateTests`), a repeated metric source, cache invalidation skipped after a save (16 tests), the URL scheme allow-list (on the identity page, the only page with links: `InvalidValues_AreRejected`), the markup rule switched off (13 tests), the first-run placeholder check always true, a draft save that replaces the stamp, `[BodyLimit]` removed, the photo / template retry removed,
  the script scope widened to the identity page, Move ignored and a row button that writes. Not applicable to stage A: "section disable not filtering a surface" (stage B).
- **Review fixes after stage A (each with a regression test; 11 further scratch-copy mutations, all caught).** (1) A wizard publish or skip that loses a race to a profile write is retried once while the wizard is unfinished; a second loss gives `PublishOutcome.Changed` / `SkipOutcome.Changed` with a truthful message (draft kept, setup still open), and a skip never flashes success unless it was recorded. (2) The first-run publish keeps topics, phone, WhatsApp, links, other emails and the light photo the owner edited (only values still equal to the `Site` config are cleared). (3) A photo or template write that loses three races, and an edit that races the creation of the profile row (duplicate key), end as a friendly refusal; the uploaded file is deleted; `SetPhotoAsync` returns `(Outcome, Previous)` and `SetTemplateAsync` returns the outcome. (4) The About headline is required only once the page has one (a freshly published site has none). (5) Template words are used as written: buttons say "Save <heading>" / "Add <item>", row messages read `Figure 2: "What it measures" is required.`, limits say "At most 8 rows in <heading>."; the content editors (4a / 4b-1) keep their older lower-cased phrasing. `WriteDraftAsync` now says callers must not re-read tracked state. Not changed: bidi and zero-width characters pass the control-character check (as in the content editors); the first-run placeholder comparison is per part against the current `Site` config.
- **Known gaps / owner decisions.** The migration and the new pages were not run against SQL Server (SQLite in tests; the SQL was read). No browser check of the new pages was done (markup and header tests only). The wizard's skip is guarded by the old token and the new stamp only through the completion time: a skip that loses a race with an edit logs and does nothing (unchanged behaviour). Requiring a headline means a wizard-published site (About headline empty) must enter one before it can save its About text; that is intentional (an empty page heading), say so to owners who ask.
  The identity page's own save wording ("Profile saved", the conflict message) still names "the profile", a product word. Metrics and cards are reordered by buttons and persisted by Save; there is no drag and drop for the same reason as in 4a.

#### Implementation notes for 4b-2 stage B (section settings)
- **Route.** `/admin/sections` (`Pages/Admin/Sections.cshtml`, `SectionsModel : ProfilePartModel`), linked from the dashboard and the admin menu. Same pattern as stage A: POST + antiforgery + `[BodyLimit(64 KB)]`, GET only reads, `admin-unsaved.js` allowed on exactly this path by `AdminAuth.AllowsScript`, polite status region, focus on the moved row or the error summary, Save first in the form, `admin.css` only. Move up / Move down are POST handlers that rearrange the typed rows on the page and write nothing; Save writes. `_RowButtons` moved to `Pages/Admin` and can leave out Remove.
- **What it can do.** Show / hide, rename and reorder the sections the **template lists** (`SectionRegistry` keys). It cannot add one: the posted list must be exactly the template's sections, each once (matched by key, never by position); a missing, repeated, unknown, empty, differently cased or path-like key refuses the whole post ("does not match your template any more, so nothing was saved"). Stored settings for keys the template does not list (left by another template or by hand) are carried over untouched on a save, and the resolver only ever applies settings to listed sections, so a stored `Shown: true` for an unlisted key shows nothing.
- **No migration.** `SiteProfile.Sections` is the existing JSON column; `SectionSetting` / `SectionOption` gain one nullable property, `Shown`. The EF model snapshot was edited by hand to match (`dotnet ef migrations has-pending-model-changes` says there are none), so `Migrate()` does not complain; there is no schema change. The existing `Enabled` flag keeps its older meaning (false hides; true is "no opinion" and can never switch on a section the template has off: the old `ASectionSetting_CannotEnableASectionTheTemplateDoesNotHave` test passes untouched). `Shown` is the owner's explicit choice from the new page.
- **What "on" persists (Healthcare defaults).** Only differences from the template in effect when the page is saved are stored: `Shown` only when the choice differs from the template's default for that section, a name only when it differs from the template's, the positions (all nine, 1-based) only when the order differs. Saving the page unchanged stores nothing (tested for both templates), so no default is frozen into the profile. Healthcare's Articles and Presentations show as off with a note; switching them on stores `Shown: true`, switching them back removes the entry. A choice survives a template switch (a switch writes nothing; tested both ways); saving the page under another template re-derives the differences against *that* template (documented in the new-user guide). Resolver rule: `enabled = required || (Shown ?? (template.enabled && Enabled != false))`; order = stored positions when present, otherwise the template's, with unpositioned sections after positioned ones in template order.
- **Rules.** Names: plain text through `FormCheck` (markup, control characters refused), at most 40 characters, empty or equal to the template's name means "use the template's", **unique case-insensitively** over the names the owner would end up with (switched off ones included; swapping two names is allowed). **At least one section that has its own page** must be on (testimonials is a home block and does not count; contact and the others do). **Required sections**: `required: true` on a manifest section (schema, validator: not with `enabled: false`) makes it always on, with no switch and a stored hide ignored; nothing in the engine needs any particular section (nav, home and routes all degrade through `IsEnabled`), so **no shipped template sets it** (owner decision below). Order only among the template's keys.
- **Surfaces checked (each follows a save at once, tested for both templates where it applies).** Navigation (desktop, mobile, the "Get in touch" button), the home page blocks (featured work, testimonials, credentials, recent roles, skills), `/sitemap.xml` (pages in the owner's order; project and post addresses follow their sections), JSON-LD (credentials only for an enabled certifications section; hidden pages have no breadcrumb because they 404), the CV page and PDF (`PortfolioContentService`), routes (`[SectionPage]` 404, still the site's styled not-found page), the "back to" links (use the owner's name). The robots file, the footer and the share image do not read sections. A relabel reaches the menu (the short label follows a custom name), the mobile menu and the "back to" links; page headings and titles come from the manifest's `copy`, not from section labels, so Software output with no stored settings is unchanged (parity snapshots pass without edits).
- **Ordering and the list stamp.** The order is the menu and sitemap order; blocks on the home page keep their fixed places. The `ListStamps` pattern is **not used**: a save replaces the whole `Sections` list as one value, guarded by the profile stamp the form carries (stage A), so there are no per-row writes that could leave two rows in one position; two overlapping saves conflict at the database (tested with real overlapping requests, the stored positions stay one consistent set). Adding a list stamp would only refuse a save for a reason the profile stamp already covers.
- **Manifest.** `editor.sections` (`heading`, `label`, `shown`, wording-only `locked` and `defaultOff`; `label` can be hidden, in which case stored names are kept; `shown` cannot), schema, validator, drift test, both shipped manifests, authoring guide. Every noun in a message comes from `itemLabel` or these words (tested with a unique-word manifest, including the duplicate, mismatch, at-least-one, conflict and flash messages).
- **Tests.** `SectionSettingsTests`, `SectionTemplateTests`, `SectionAccessibilityEndToEndTests` (both templates, blank wizard-finished site), the route in `AdminAccessTests`, `ContentSecurityTests` (anonymous, no token, stale session, GET never mutates with two stored settings, real stamps, body caps, script scope), `BodyLimitCoverageTests`, `TemplateManifestTests`.
- **Mutations proven (scratch copy only).** 22 mutations, each applied alone to a copy of the repository outside the working tree, the relevant tests run, every one caught (no survivors): navigation, `/sitemap.xml` (project pages, blog posts), the home blocks (featured work, testimonials), the route gate, JSON-LD credentials and the CV each no longer filtering a hidden section; reorder ignored; the seen stamp ignored; `[AllowAnonymous]` and `[IgnoreAntiforgeryToken]` on the page; cache invalidation skipped; a hidden name field losing stored names; the key check removed (a key the template lacks); the older `Enabled` flag able to switch a template-off section on; the at-least-one rule, the duplicate-name rule and the required flag removed; defaults frozen into the profile (`Shown` always stored); stored unknown keys dropped; the markup rule and the length cap off.
- **Follow-ups from the stage B review.** (1) A template switch is refused when it would leave no section in the menu (the stored choices on top of the new template's defaults), in the target template's words; the preview says so and also lists the sections a switch would show again. (2) The page keeps every earlier explicit choice (`Shown`, or the older `Enabled: false`) even when it now equals the template's default, so a hide cannot be undone by a later save plus a switch; a fresh site's unchanged save still stores nothing. A hide of a section that is off by default in the current template is not recorded (the owner never changed anything), so switching to a template where it is on shows it: the preview's "Shown again" list is the warning. (3) Names refuse Unicode format characters (category Cf: zero-width, bidi), locally to the sections page (the shared `FormCheck` is unchanged, a decision for the content editors). (4) A typed name must also differ from every other section's short menu name and from the fixed "CV" entry. (5) Known gaps: moving Contact or Testimonials has no menu effect (the rows say so, the buttons stay); renaming changes the menu and "back to" links only, not page headings, titles or breadcrumbs (they come from the template's copy); `Seed:Mode=Authoritative` also discards the section settings. (6) The dashboard uses section names as written and takes its word for a section from the manifest.
- **Known gaps / owner decisions.** (1) No shipped template marks a section required: a Healthcare owner may hide Contact; say if either shipped template should lock one. (2) Order affects the menu and sitemap only, not the fixed order of the home page blocks or the CV. (3) Duplicate names are refused (not silently allowed) and compared without case. (4) Not run against SQL Server and no browser check (as for stage A). (5) 4c (JSON export / import) should export `Sections` (including `Shown` and unknown keys) with the profile and import it through `ProfileEditor.UpdateAsync` with the same key rules; imported settings for keys the template lacks must stay inert.

#### 4b-2 brief (stages A and B done; self-contained)
**Goal.** Let the owner edit the hero, metrics and About text, and switch sections on / off and reorder them, from the admin, in the template's words.
**Start from.** `Pages/Admin/Profile.cshtml(.cs)`, `Services/ProfileEditor.cs` and `ProfileForm.cs` (identity editing, `AfterCommitAsync` refreshes caches), `Data/Entities/SiteProfile.cs` (`Hero`, `Metrics`,
`About`, `Sections` are JSON-mapped owned types), `Services/SiteProfileMapper.cs`, `Services/SectionRegistry.cs`, `Services/TemplateDefinition.cs` + `TemplateView.cs` (how a template's sections, labels and `enabled` combine
with the profile's `Sections` overrides), `Services/TemplateCatalog.cs`/`TemplateValidator.cs`, the wizard (`OnboardingService`, `Pages/Admin/Welcome/*`, which already writes hero suggestions), and the 4a / 4b-1 editors as the
pattern (`Pages/Admin/Content/*`, `ContentEditor`, `FormCheck`, `EditorFields`, `admin-unsaved.js`, `AdminAuth.AllowsScript`).
**Requirements carried from the owner.**
- Public pages for the default Software template stay **byte-identical** (parity snapshots untouched); if a change would alter them, stop and report.
- **No migration unless proven necessary**: hero / metrics / about / sections already live in `SiteProfile` JSON columns. Profile edits today have no per-write stamp (`ProfileEditor` is last write wins);
  `OnboardingCompletedAt` is a concurrency token but only moves when the wizard publishes, so it cannot guard these edits. Recommended: one additive nullable `ConcurrencyStamp` column on `SiteProfiles`
  (replaced on every profile write, checked from the form like the content stamps; D3: no renames), justified because two tabs would otherwise overwrite each other. If the owner prefers no migration, keep last write wins and say so.
- **Hiding a section never deletes rows.** Turning a section off only hides its page, nav entry and home block; the entries stay and show again when it is turned on.
- Nav, `/sitemap.xml`, JSON-LD, the CV, routes (`[SectionPage]` 404s) and the home page blocks must all follow a save immediately (`ProfileEditor.AfterCommitAsync` already invalidates the provider version and the
  "Generated" output cache). Sections a template lists as `enabled: false` (Healthcare: Blog and Talks) default off; the owner may turn them on.
- Labels, hints and visibility come from the manifest (extend `editor` with `hero`, `metrics`, `about`, and the section settings page wording); never `if (templateKey == ...)`; no hardcoded nouns in messages.
- Everything admin: under `Pages/Admin`, `AdminPageModel`, POST + antiforgery + `[BodyLimit]` (the reflection test will fail otherwise), GET never mutates, concurrency (a stamp on the profile row; section order uses
  the list-stamp pattern with a section key such as `sections`, or the profile token), friendly conflict, plain-text validation (`FormCheck`), URL allow-lists, no inline script, first-party script only on edit routes via
  `AllowsScript`.
- Section rules to decide and test: required sections that cannot be turned off (e.g. About / Contact: check what the home page and nav need), at least one section on, order only among enabled / known keys
  (`SectionRegistry.IsKnown`), unknown keys in stored JSON tolerated.
**Tests to write.** Persist and public follow (nav, home, sitemap, JSON-LD, CV) for each edit; validation / injection corpus on every text field; row / profile stamp conflicts with real overlapping requests (the
`AdminApp.BeforeOrderedSave` hook); every new route in `AdminAccessTests` + `ContentSecurityTests` (anonymous, no token, stale session, GET-never-mutates, body caps); template-driven labels via a test manifest with
unique words including messages; hiding keeps rows; Healthcare defaults; Software snapshots unchanged; accessibility; CSP scoping.
**Mutations to prove (scratch copy only).** owner check, antiforgery, section disable not filtering a surface (nav, sitemap, home, route), reorder ignored, stamp check, hidden-field data loss, URL scheme allow-list,
cache invalidation.
**4c pointer.** JSON export / import of all content (backup, portability): export `ConcurrencyStamp`-free rows (and the profile, sections and uploads manifest), decide how imported rows interact with the unique keys
(talk title, testimonial author + quote start, blog slug, project slug, certification / skill name, role company + title) and with `Seed:Mode`; reuse `ContentEditor` for writes and `SafeMd` rules for imported posts.

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
