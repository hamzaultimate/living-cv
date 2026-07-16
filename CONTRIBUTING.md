# Contributing

Thanks for your interest in improving this project! This portfolio/living-CV template is
meant to be forked, personalised, and improved together.

## Getting started

1. **Prerequisites:** [.NET 10 SDK](https://dotnet.microsoft.com/download) and
   SQL Server LocalDB (ships with Visual Studio, or install SQL Server Express).
2. **Fork & clone** the repository.
3. **Run it:**
   ```bash
   dotnet run --project src/Portfolio.Web
   ```
   Migrations apply and the demo content seeds automatically on first run. The local
   connection string is already set in `appsettings.Development.json` (LocalDB — no secret).
4. Open the printed `https://localhost:xxxx` URL and confirm `/health` returns `200`.

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
  `src/Portfolio.Data/Seed/`. Please don't hardcode names, copy, or data into Razor pages.
- Match the style of the surrounding code (naming, comment density, idioms).
- Razor Pages are server-rendered — keep them accessible and SEO-friendly.
- Run `dotnet build` before pushing; the build should be warning-clean.
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
