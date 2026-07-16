# Build Spec — example.com (Personal Portfolio & Living CV)

> **How to use this file:** Paste sections into Claude Code in the order given under
> "Phased Build Order." Each phase has a ready-to-use prompt. Build and verify one phase
> before moving to the next.

---

## 1. Project overview

Build a fast, SEO-friendly personal portfolio site that acts as a **living CV** — a single
place someone can be referred to in order to understand who Alex Rivera is
professionally. It hosts: an about/bio, work experience, project case studies,
skills, certifications, a blog, a talks/videos section (empty for now, ready for future
YouTube content), and contact links. It must also offer a **downloadable PDF CV generated
from the same source data** so the web version and PDF never drift apart.

**Owner:** Alex Rivera
**Domain:** example.com (already purchased)

---

## 2. Positioning (drives all copy)

The site's single message:

> **A senior software engineer who builds reliable, high-performance systems — and now
> brings AI into them.**

Supporting proof to weave through the copy:
- 7+ years, .NET / .NET Core / C#, distributed systems & microservices
- Systems at scale: a license system handling ~20k rps; a microservice with sub-10ms avg
  response pulling a 1.8M-user / 6GB repository in under ~50s; 20+ systems managed
- Leadership: led LAS (License Access Service), delivered on time, zero major bugs since
  Mar 2022; mentors juniors
- Current pivot: applied AI on Azure (at Nimbus Systems), Azure AI cert path
  in progress, fluent with agentic tooling (Claude Code)
- Differentiator: also an IoT/embedded hobbyist (ESP32-S3, Arduino, MicroPython) — understands
  systems from the silicon up

Copy voice: confident, plain, specific. Lead with outcomes and numbers, not adjectives.
Avoid "passionate developer" clichés.

---

## 3. Tech stack

- **Framework:** Astro (latest). Content-first, ships minimal JS, excellent Lighthouse/SEO.
- **Styling:** Tailwind CSS.
- **Content:** Astro Content Collections — every project, blog post, certification, and
  experience entry is a Markdown/MDX file with typed frontmatter. Adding a new project
  later = one new `.md` file.
- **Icons:** a lightweight set (e.g. lucide via astro-icon).
- **PDF CV:** generated at build time from the same data (see §8).
- **Hosting:** Azure Static Web Apps (free tier) with a GitHub Actions deploy pipeline.
  *(Reinforces the Azure story. Fallback: Cloudflare Pages.)*
- **Analytics:** Cloudflare Web Analytics or Plausible (privacy-friendly, no cookie banner).
- **Repo:** GitHub, public (the repo itself is a portfolio artifact — keep commits clean).

---

## 4. Information architecture / sitemap

```
/                      Home (hero + highlights of everything)
/about                 Full bio + the story of the pivot into AI
/experience            Roles: Nimbus Systems, Contoso Cloud, Fabrikam Labs, Northwind Trading Co.
/projects              Grid of project case studies
/projects/[slug]       Individual case study (EKey, GAS, SHARP, LAS, UPS, IoT project, etc.)
/skills                Grouped skills (Backend, Cloud/DevOps, AI, Data, Embedded/IoT)
/certifications        Certs earned + in-progress (Azure AI path, LinkedIn courses, Claude)
/blog                  Blog index
/blog/[slug]           Blog post
/talks                 Talks & videos (empty state now; ready for YouTube embeds later)
/cv                    Web CV view + button to download the PDF
/contact               Email + LinkedIn + GitHub (+ optional simple contact form)
```

---

## 5. Content model (Astro Content Collections)

Define typed collections so content is data, not hardcoded HTML.

**`projects`** frontmatter:
`title, slug, role, org, period, summary, problem, approach, impact (array of
metric strings), tech (array), links (array), featured (bool), order`

**`experience`** frontmatter:
`company, title, start, end, current (bool), summary, highlights (array), tech (array)`

**`certifications`** frontmatter:
`name, issuer, status ("earned" | "in-progress" | "planned"), date, credentialUrl,
category`

**`blog`** frontmatter:
`title, slug, description, date, tags (array), draft (bool), cover (optional)`

**`talks`** frontmatter:
`title, type ("video" | "talk"), youtubeId (optional), event, date, url`

Seed the site with real content from the CV: experience entries for Nimbus Systems, Contoso Cloud
(2023–2025), Fabrikam Labs (2019–2023), Northwind Trading Co. (2017–2019); project case
studies for EKey, GAS, SHARP, LAS, UPS, plus the Water Level & Quality Monitoring IoT
project; certifications for the Azure AI path (mark AI-901 in-progress) and the completed
LinkedIn courses (Decision-Making Strategies; Transitioning from Manager to Leader) and
Claude / Claude Code.

---

## 6. Design direction

Deliberately avoid the generic AI-portfolio look (cream background + high-contrast serif +
terracotta accent). This subject is a **systems/backend engineer with an embedded/IoT
streak**, so the identity should feel precise, technical, and calm.

- **Mood:** engineered, high-signal, quietly confident. Dark UI, disciplined grid, generous
  whitespace. Nothing decorative that doesn't encode information.
- **Palette:** dark base (near-black, not pure black), one restrained accent used sparingly
  for emphasis/links, plus neutral grays for structure. Pick 4–6 named hex values and use
  them consistently. Choose an accent that is NOT terracotta/#D97757.
- **Type:** a characterful but readable display face for headings + a clean body face + a
  **monospace utility face** for labels, metrics, section eyebrows, and metadata (leans into
  the engineering identity). Set a clear type scale.
- **Signature element (pick ONE, execute well):** a subtle "telemetry/signal" motif that nods
  to the monitoring + IoT background — e.g. a faint animated waveform or grid in the hero,
  or metrics that render like sensor readouts. Keep everything else quiet around it.
- **Motion:** minimal and purposeful (one considered hero reveal, subtle hover states).
  Respect `prefers-reduced-motion`.
- **Quality floor:** fully responsive to mobile, visible keyboard focus, semantic HTML,
  accessible color contrast.

---

## 7. Page requirements

**Home:** hero with the positioning line + one-line proof; a strip of 3–4 headline metrics
(20k rps, sub-10ms, 7+ yrs, 20+ systems); featured projects (3); a short "currently" line
(Nimbus Systems + AI cert path); links to LinkedIn/GitHub/CV.

**About:** the narrative — where he's been (deep backend at scale) and where he's going
(applied AI on Azure), including the IoT/embedded thread. Photo optional.

**Experience:** reverse-chronological, outcome-focused. Each role: what the system did, scale,
his contribution, tech.

**Projects:** grid → case study pages. Each case study uses the problem → approach → impact
structure with concrete metrics. These are the strongest asset — treat them as mini
engineering write-ups, not resume bullets.

**Skills:** grouped, scannable. Groups: Backend (.NET, C#, EF Core, MediatR, CQRS),
Cloud/DevOps (Azure Functions, DevOps, CI/CD YAML, IaC/Bicep, load balancing), AI
(Azure AI services, agentic tooling / Claude Code — grow this), Data (MSSQL, RavenDB,
MongoDB), Architecture (Microservices, Clean Architecture, DDD, ABP), Embedded/IoT
(ESP32-S3, Arduino, MicroPython).

**Certifications:** show earned, in-progress, and planned with clear badges/status.

**Blog:** clean reading layout, tags, reading time. Ship with 1–2 seed posts (see §10).

**Talks/Videos:** graceful empty state now ("Coming soon"); YouTube embeds when `talks`
entries exist.

**CV:** rendered web CV + a prominent "Download PDF" button (see §8).

**Contact:** email, LinkedIn, GitHub. Optional: a simple form via a serverless function /
Formspree — but do NOT collect data without a clear purpose.

---

## 8. Living-CV PDF

Generate the PDF from the same content collections so it can never fall out of sync with the
site. Approach: a dedicated print-styled `/cv` route rendered to PDF at build time (e.g. via
Playwright in the CI step, or a print-optimized stylesheet the user can "Save as PDF").
Prefer the build-time generation so a fresh `cv.pdf` is produced on every deploy and served
as a static download.

---

## 9. SEO & metadata (do not skip)

- Per-page `<title>` and meta description.
- Open Graph + Twitter card tags; generate an OG image (name + role).
- **JSON-LD `Person` schema** on the home page (name, jobTitle, sameAs: LinkedIn/GitHub
  URLs, knowsAbout: key skills) so search engines and AI tools represent him correctly.
- `sitemap.xml` (Astro sitemap integration) + `robots.txt`.
- Semantic headings, alt text on all images, canonical URLs.
- Fast by default (Astro helps) — target 95+ Lighthouse across the board.

---

## 10. Seed blog posts (drafts to start the engine)

1. "How I cut memory leaks in a multi-tenant .NET SaaS" — dependency-scope misconfig story
   from the Contoso Cloud work (real, specific, credible).
2. "Building a service that serves 1.8M users' data in under a minute" — the SHARP
   architecture and performance decisions.

These double as LinkedIn posts.

---

## 11. Phased build order (feed these to Claude Code in sequence)

**Phase 1 — Scaffold**
> "Create a new Astro + Tailwind project for a personal portfolio site. Set up Content
> Collections for `projects`, `experience`, `certifications`, `blog`, and `talks` with the
> typed frontmatter schemas in this spec. Create the routing/sitemap from §4. Set up a base
> layout, and a design system implementing the direction in §6 — give me the token system
> (palette hex values, type scale, fonts) first and let me confirm before styling everything."

**Phase 2 — Content + pages**
> "Populate the collections with the real seed content from §5 (experience, projects, certs).
> Build the Home, About, Experience, Projects (+ case study template), Skills, and
> Certifications pages per §7."

**Phase 3 — Blog, talks, CV, contact**
> "Build the blog (index + post template) with the two seed posts from §10, the talks page
> with a 'coming soon' empty state ready for YouTube embeds, the `/cv` page, and contact."

**Phase 4 — PDF CV**
> "Implement build-time PDF generation of the CV from the same content collections per §8,
> served as a static `cv.pdf` download."

**Phase 5 — SEO + polish**
> "Add all SEO/metadata from §9 including JSON-LD Person schema and OG images. Run a
> Lighthouse pass and fix anything under 95. Verify mobile responsiveness, keyboard focus,
> and reduced-motion."

**Phase 6 — Deploy**
> "Set up deployment to Azure Static Web Apps via GitHub Actions, wire up the custom domain
> example.com, and add Cloudflare Web Analytics."

---

## 12. Definition of done

- [ ] Live at example.com over HTTPS
- [ ] All CV content represented; projects as case studies with metrics
- [ ] Downloadable PDF CV generated from site data
- [ ] Blog working with 2 posts; talks section ready for future videos
- [ ] Lighthouse 95+ (Performance / SEO / Accessibility / Best Practices)
- [ ] JSON-LD Person schema + OG images present
- [ ] Adding a new project = adding one Markdown file
- [ ] Repo is clean and public