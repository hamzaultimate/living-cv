# Contributing

Thanks for your interest in improving this project! This portfolio/living-CV template is
meant to be forked, personalised, and improved together.

## Getting started

1. **Prerequisites:** [.NET 10 SDK](https://dotnet.microsoft.com/download) and
   SQL Server LocalDB (ships with Visual Studio, or install SQL Server Express).
2. **Fork & clone** the repository.
3. **Run it:**
   ```bash
   dotnet restore
dotnet run --project src/Portfolio.Web
   ```
   Migrations apply and the demo content seeds into empty tables automatically on first run. The local
   connection string is already set in `appsettings.Development.json` (LocalDB — no secret).
4. Open the printed HTTPS URL (for example `https://localhost:7239`) and confirm `/health` returns `200`.
5. If the browser warns about the local development certificate, trust it with `dotnet dev-certs https --trust`.
6. For a more detailed walkthrough, review [docs/new-user-guide.md](docs/new-user-guide.md).

## Branching model

| Branch | Purpose |
|---|---|
| `feature/*` | Your work — branch from `develop` |
| `develop` | Integration branch (default) |
| `main` | Release / production |

Open a pull request **from a `feature/*` branch into `develop`**. Keep PRs focused and
describe what you changed and why.

## Coding conventions

- **Content is data, never hardcoded markup.** Site identity/copy belongs in the `"Site"`
  config section (`SiteOptions`); page content belongs in the JSON seed files under
  `src/Portfolio.Data/Seed/`. Profession-specific copy (SEO patterns, skill-group order) belongs in
  `src/Portfolio.Web/Templates/<key>/template.json` (labels, section list, empty-state text, theme name), validated by
  `TemplateValidator`. Read it in pages through `RequestTemplate`, and mark a page that belongs to a section with
  `@attribute [SectionPage(SectionRegistry.X)]` so it 404s when the template disables it. Themes are CSS variables under
  `[data-template="<key>"]` in `Styles/themes/`. Please don't hardcode names, copy, or data
  into Razor pages. Read site identity through `ISiteProfileProvider`, not `IOptions<SiteOptions>`.
- Admin pages live under `src/Portfolio.Web/Pages/Admin`, which is protected as a whole (`AuthorizeFolder`
  plus a deny-by-default check). Add new admin pages there, inherit `AdminPageModel`, keep every
  state-changing action a POST (anti-forgery is automatic for Razor Pages), and call `ProfileEditor` (or
  an equivalent) so the profile cache and the "Generated" output cache are invalidated after writes. A page
  that must be public inside that folder needs an explicit `[AllowAnonymous]`; `AdminAccessTests` fails on
  any other unauthenticated admin route. Admin and setup pages are styled by `wwwroot/css/admin.css`, not
  Tailwind (they are excluded from the Tailwind scan so they never change the public stylesheet).
- The onboarding wizard (`Pages/Admin/Welcome`) keeps its answers as a draft on the profile row (`OnboardingDraft`), which
  nothing public reads, and applies them only in `OnboardingService.PublishAsync` (one transaction, then `ProfileEditor.AfterCommitAsync`).
  Sample content is seed-if-empty per table and must never update or delete a row. Unpublished photos live in the draft area of
  `UploadStore` (no public URL; `/uploads` serves flat files only). Wizard suggestions, accents and thumbnails come from the template
  manifest, never from Razor.
- The content editors (`Pages/Admin/Content`) follow one pattern: a page model derived from `ContentListModel`, `ContentDeleteModel` or
  `ContentPageModel`, a form class whose fields are read only when the template shows them (`EditorView.Visible`), validation through
  `FormCheck`, writes through `ContentEditor` (it stamps the row, refuses a stale form and, after the commit, refreshes the profile version
  and the "Generated" output cache), and a confirmation page for every removal. Labels, hints and the name of an entry come from the
  manifest's `editor` block (`EditorFields` holds the neutral defaults); never decide wording or visibility by template key. A new editable
  entity needs a `ConcurrencyStamp` (`IOrderedContent`) and an additive migration. Admin pages run no inline script: the one exception is the
  first-party `wwwroot/js/admin-unsaved.js` on the edit forms (the admin CSP allows `script-src 'self'` there only, see `AdminAuth.AllowsScript`).
- Never trust an upload: go through `ImageProcessor` and `UploadStore`, which sniff the bytes, re-encode,
  and generate the file name. The owner account is created only by `/setup` and reset only by
  `dotnet run --project src/Portfolio.Web -- reset-owner`; do not add other account paths.
- Seeding is seed-if-empty by default: a table is filled only when it has no rows and existing
  rows are never changed. Use `Seed:Mode=Authoritative` if you want seed files to overwrite and
  delete rows.
- Match the style of the surrounding code (naming, comment density, idioms).
- Razor Pages are server-rendered — keep them accessible and SEO-friendly.
- Run `dotnet build` and `dotnet test` before pushing; the build should be warning-clean and the tests green.
- If you add or change an entity, add an EF Core migration:
  ```bash
  dotnet ef migrations add <Name> --project src/Portfolio.Data --startup-project src/Portfolio.Web
  ```

## What makes a good contribution

- New optional features that stay **data-driven** and degrade gracefully when unconfigured.
- Accessibility, performance, and SEO improvements.
- Bug fixes with a clear description of the failure and the fix.
- Documentation improvements.

## Reporting issues

Open a GitHub issue describing the problem, steps to reproduce, and what you expected. For
security-sensitive reports, please avoid filing a public issue with exploit details.

## Code of conduct

Be respectful and constructive. We're here to build something useful together.
