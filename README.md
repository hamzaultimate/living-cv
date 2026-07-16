# Portfolio & Living CV

A **database-backed personal portfolio and living CV**, built with ASP.NET Core Razor Pages
and designed to double as proof of the skills it describes. Content lives in the database
(seeded from JSON) and identity lives in configuration — so you can make it your own without
touching the code.

The repository ships with a **fictional demo persona ("Alex Rivera")** so every feature is
populated and testable out of the box. Swap in your own details to personalise it.

## Features

- Server-rendered Razor Pages (SEO-first) with light/dark theme
- Home, About, Experience, Work (case studies with galleries + lightbox), Skills,
  Certifications (badge grid + branded issuer cards), Blog (Markdown, drafts), Talks
  (YouTube embeds), CV (HTML + generated **PDF**), Contact
- `og.png` share image, `sitemap.xml`, `robots.txt`, and schema.org JSON-LD — all generated
- Idempotent JSON seeder — edit a seed file, restart, and rows upsert by natural key
- Health checks at `/health` (liveness) and `/health/db` (DB readiness)

## Tech stack

| Concern | Choice |
|---|---|
| Web | ASP.NET Core **Razor Pages**, server-rendered |
| Runtime | **.NET 10 (LTS)**, published self-contained |
| Data | **SQL Server** via EF Core (LocalDB for local dev) |
| Styling | Tailwind CSS |
| PDF CV | QuestPDF, generated from the same data as the site |
| Hosting | IIS-compatible (self-contained publish) |
| CI/CD | Azure Pipelines → publish → FTPS deploy |

## Run locally

Requires the **.NET 10 SDK** and **SQL Server LocalDB** (ships with Visual Studio; or install
SQL Server Express). The local connection string is already configured in
`src/Portfolio.Web/appsettings.Development.json` and points at `(localdb)\MSSQLLocalDB`.

```bash
dotnet run --project src/Portfolio.Web
```

On first run the app **applies EF Core migrations and seeds the demo content automatically**.
Open the printed `https://localhost:xxxx` URL. Health check: `/health`.

> No database configured? The site still runs — DB-backed pages show empty states and
> `/health/db` reports the status.

## Make it yours

Everything personal lives in two places — **no code changes required**:

1. **Identity & copy** → the `"Site"` section of `src/Portfolio.Web/appsettings.json`
   (name, role, hero text, metrics, About narrative, contact links).
2. **Content** → the JSON files in `src/Portfolio.Data/Seed/`
   (`projects`, `experiences`, `skills`, `certifications`, `talks`, `blog`, `testimonials`).
   Each file has a header comment describing its shape.

Add your own images under `src/Portfolio.Web/wwwroot/img/` and drop a `profile.jpg` into
`wwwroot/img/` to show your photo (the site hides it gracefully when absent).

## Build & publish (as the pipeline does)

```bash
dotnet publish src/Portfolio.Web/Portfolio.Web.csproj -c Release \
  -r win-x64 --self-contained true -o ./publish
```

Set the production database via the `ConnectionStrings__Default` environment variable (or
`appsettings.Production.json`). **Never commit real credentials.**

## Repository layout

```
Portfolio.slnx                    Solution (modern .slnx format)
global.json                       Pins the .NET SDK (10.x)
src/Portfolio.Web/                ASP.NET Core Razor Pages app
  Configuration/SiteOptions.cs    Bound identity/content config ("Site" section)
  Pages/                          Razor Pages
src/Portfolio.Data/               EF Core entities, migrations, and JSON seed data
  Seed/                           Seed content (edit these to change the site)
  Seeding/PortfolioSeeder.cs      Idempotent upsert-by-natural-key seeder
pipelines/                        Azure Pipelines build + FTPS deploy
docs/                             Build plan and notes
```

## Contributing

Contributions are welcome! See [CONTRIBUTING.md](CONTRIBUTING.md) for how to get started,
the branching model, and coding conventions.

## License

[MIT](LICENSE).
