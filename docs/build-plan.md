# Portfolio Build Plan — example.com

Living plan for the portfolio / living-CV site. Supersedes the original
`portfolio site build specs.md` where they conflict (that spec assumed a static Astro site on
Azure; we are building a database-backed ASP.NET Core app on Plesk).

## Confirmed decisions

- **Stack:** ASP.NET Core **Razor Pages**, server-rendered (SEO-first).
- **Runtime:** **.NET 10 (LTS)**, published **self-contained** (`win-x64`).
- **Data:** PostgreSQL via EF Core + Npgsql (Plesk PostgreSQL, unlimited on plan).
- **Content:** seed files in the repo → applied to PostgreSQL on deploy. **No admin UI in v1.**
- **Styling:** Tailwind CSS, compiled in CI (no Node runtime needed on the server).
- **PDF CV:** QuestPDF, generated from the same DB entities.
- **Hosting:** Plesk — **Windows / IIS**, confirmed: ASP.NET support + Dedicated IIS app pool +
  SSL/TLS + SEO-safe HTTP→HTTPS 301. Free **Let's Encrypt** SSL via Plesk "SSL It!".
- **CI/CD:** Azure Pipelines (YAML in `pipelines/`) → self-contained publish → **FTPS deploy**.
- **Open host item:** confirm the **ASP.NET Core Hosting Bundle (ANCM)** is installed — the
  Phase 0 hello-world deploy is the probe. If missing, ask the host to install it.

## Environments & branching

| Branch | Environment | URL | Database |
|---|---|---|---|
| `feature/*` | local | localhost | local Postgres |
| `develop` | Dev | `dev.example.com` | `portfolio_dev` |
| `main` | Production | `example.com` | `portfolio_prod` |

`feature/*` → PR into `develop` (auto-deploys to dev) → PR into `main` (deploys to prod, gated
by a manual approval on the `portfolio-prod` environment).

## Data model (Phase 1)

`Projects`, `Experiences`, `Skills` (+ `SkillGroup`), `Certifications`, `BlogPosts`, `Talks`,
and `Proofs` (image/PDF proof links). Proof assets live in `wwwroot/proofs/`
(version-controlled). Fields follow the original spec §5.

## Phased delivery

- **Phase 0 — Scaffold + CI/CD + hello world** *(in progress)*
  Razor Pages app + `/health`, `web.config` (ANCM), `.gitignore`, README, `azure-pipelines.yml`
  (build → self-contained publish → FTPS deploy), deploy to dev subdomain over HTTPS, then prod.
- **Phase 1 — Data + design system:** EF Core + Npgsql, entities, migrations, idempotent
  seeder; design tokens (palette/type/fonts, confirm before styling); Tailwind pipeline.
- **Phase 2 — Content pages:** Home, About, Experience, Projects (+ case-study template),
  Skills, Certifications — seeded with real CV content.
- **Phase 3 — Blog, Talks, CV, Contact.**
- **Phase 4 — PDF CV** (QuestPDF from DB data).
- **Phase 5 — SEO + polish:** meta, OG images, JSON-LD Person, sitemap, robots, Lighthouse 95+,
  a11y, reduced-motion.
- **Phase 6 — Production cutover:** prod DB seeded, approval-gated deploy, analytics, QA.

## Access / setup checklist (owner: Alex)

- [ ] Provide **FTP deployment creds** (dedicated account) + remote document-root paths (dev & prod).
- [ ] Create **`dev.example.com`** subdomain in Plesk.
- [ ] Create PostgreSQL DBs **`portfolio_dev`**, **`portfolio_prod`**.
- [ ] Enable **Let's Encrypt** SSL on both sites.
- [ ] Confirm Azure DevOps **parallelism grant** (free tier) so pipelines can run.

## Pipeline variables (set in DevOps, passwords as secret)

`Ftp.Host`, `Ftp.User`, `Ftp.Password` (secret), `Ftp.RemoteRootDev`, `Ftp.RemoteRootProd`.
