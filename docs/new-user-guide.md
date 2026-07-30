# New user guide

This guide is for anyone who wants to run the portfolio site locally, personalize it, and understand how the project is organized.

## 1. Prerequisites

The project is built with .NET 10 and SQL Server.

- .NET 10 SDK
- SQL Server LocalDB (recommended on Windows) or SQL Server Express
- A terminal and a code editor such as Visual Studio Code or Visual Studio

If you are using Windows, the default local configuration already points to LocalDB, so the quickest path is usually to install the SQL Server Express/LocalDB tooling and run the app as-is.

## 2. Run the app locally

From the repository root:

```bash
dotnet restore
dotnet run --project src/Portfolio.Web
```

The first run will:

- apply EF Core migrations
- seed the demo content automatically
- launch over HTTPS by default at a local URL such as `https://localhost:7239`

Open the printed URL in your browser. The app also exposes health endpoints at:

- `/health` for liveness
- `/health/db` for database readiness

> If you do not have a database configured yet, the site can still start, but database-backed pages may show empty states until you provide a valid connection string.

## 3. Personalize the site

The app is designed so you can make it your own without editing code.

### Identity and copy

Edit the `Site` section in [src/Portfolio.Web/appsettings.json](../src/Portfolio.Web/appsettings.json) to change:

- your name and role
- the hero copy and summary
- links such as GitHub, LinkedIn, and email
- the profile photo URL

### Content and pages

The seeded content lives in [src/Portfolio.Data/Seed](../src/Portfolio.Data/Seed). Update those JSON files to change:

- projects
- experience entries
- skills
- certifications
- blog posts
- talks

### Images

Add images under [src/Portfolio.Web/wwwroot/img](../src/Portfolio.Web/wwwroot/img) and point the `Site:PhotoUrl` setting to them.

### SEO and analytics

Every page ships schema.org JSON-LD (breadcrumbs, plus `Person`/`WebSite`/`CreativeWork`/`BlogPosting` where relevant) with no configuration needed. GA4 analytics is opt-in: set `Site:GoogleAnalyticsId` in `appsettings.json` to your Measurement ID to enable it, or leave it blank to omit the analytics script entirely. See [docs/seo-analytics.md](seo-analytics.md) for the full picture, including why the `dev.` subdomain never reaches production analytics.

## 4. Recommended development workflow

A typical contributor loop looks like this:

```bash
dotnet build
dotnet run --project src/Portfolio.Web
```

If you change the data model, create a migration:

```bash
dotnet ef migrations add <Name> --project src/Portfolio.Data --startup-project src/Portfolio.Web
```

## 5. Common issues

### LocalDB is not available

If the app cannot connect to the database, install SQL Server Express or LocalDB and try again. You can also update the connection string in [src/Portfolio.Web/appsettings.Development.json](../src/Portfolio.Web/appsettings.Development.json).

### HTTPS certificate warning

The app is configured to launch over HTTPS by default. If you see certificate issues when launching locally, run:

```bash
dotnet dev-certs https --trust
```

### The app starts but content is empty

That usually means the database was not initialized successfully. Check the `/health/db` endpoint and review the startup logs in the terminal.

## 6. Project layout

A quick map of the repository:

- [src/Portfolio.Web](../src/Portfolio.Web) — Razor Pages app, configuration, and static assets
- [src/Portfolio.Data](../src/Portfolio.Data) — EF Core context, entities, and seed content
- [docs](../docs) — implementation notes and design documentation

For contribution guidance, see [CONTRIBUTING.md](../CONTRIBUTING.md).
