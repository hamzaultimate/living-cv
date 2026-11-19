# Data & Analytics and Product / Project Management on the Showcase layout

Date: 2026-11-19
Status: design approved in brainstorming
Branch: `feature/phase-5e-showcase-templates`, stacked on `feature/phase-5c-finance-template` (rebase onto `develop` once the Finance PR merges; the pull request targets the Finance branch until then)
Delivery: one pull request (Phase 5, item 4 of the numbered list in `docs/templates-and-onboarding-plan.md`)

## 1. Goal

Add two templates, Data & Analytics and Product / Project Management, as data-only presets on the existing Showcase layout, the one Software & IT uses. Each site leads with its work and the numbers behind it, in the owner's own profession's words.

## 2. Decisions made

| Topic | Decision |
|---|---|
| Approach | Data-only presets on the existing Showcase layout. No new layout, no new section |
| Delivery | One branch, one pull request, two templates built in parallel by two subagents, each in its own worktree |
| Sensitivity | Neither template is `sensitive`. Data & Analytics editor hints warn against client data, personal data and material under NDA |
| Branch | Stacked on the Finance branch, because the keyed test lists and catalog order already contain `finance-accounting` |
| Hardcoded Showcase hero words | "LinkedIn", "GitHub", "Download CV" and the waveform backdrop stay as they are, shared with Software. Recorded as a known gap, not fixed here |

## 3. Constraints

- No C# change, no Razor change, no migration, no CSP change, no new script, no new copy key. `/cv`, `/cv.pdf` and `/og.png` keep the Software look (Phase 6).
- No layout partial, block or page names a template by key, theme or profession (an existing test greps for this).
- The Software parity snapshots pass unedited. Healthcare, Gallery and Results public output does not change. The shared stylesheet changes only by the two new `[data-template="..."]` theme blocks.
- The committed `src/Portfolio.Web/wwwroot/css/app.css` is the LF build output that CI regenerates and compares. On Windows with `core.autocrlf=true` a local build writes a byte-different file; build from an LF export before committing it.
- Out of scope: per-template CV output (Phase 6), making the Showcase hero words copy keys, and a SQL Server run (no migration).

## 4. The presets

| | Data & Analytics | Product / Project Management |
|---|---|---|
| Key | `data-analytics` | `product-management` |
| Profession | Data analyst or scientist | Product or project manager |
| Archetype | `showcase` | `showcase` |
| Work section | label "projects", item label "Analysis"; the `tech` field relabelled "Tools" | label "shipped work", item label "Release" |
| Skill groups | Analysis & statistics, Data engineering, Visualisation, Machine learning, Languages & tools | Discovery, Delivery, Stakeholders, Metrics, Tools |
| Metrics hint | rows processed, forecast accuracy, hours saved | adoption, revenue, cycle time |
| Blog | on, label "notes" | on, label "writing" |
| Talks | off, `youtubeId` field hidden | off, `youtubeId` field hidden |
| Theme | teal default; accents teal, indigo, orange, rose | warm slate and amber default; accents amber, blue, green, rose |
| `sensitive` | no | no |

Shared rules for both:
- Every visible noun comes from the manifest (`copy`, section labels, `editor` labels and hints), covering every key the Software manifest sets, so nothing falls back to Software wording. Final wording avoids nouns that other templates' wording guards forbid ("initiative" belongs to Healthcare and Education, which is why the item labels are "Analysis" and "Release").
- Sections, in menu order: about, experience, projects, skills, certifications, blog, testimonials, contact.
- `heroSuggestions` read correctly for anyone in the profession and make no claim about a person.
- SEO patterns stay within 65 and 160 characters once expanded.
- The theme sets variables only. Every text and accent token, and all four accent choices, are at least 4.5:1 on bg, surface and surface-2 in light and dark.
- Starter content is fictional: an invented persona, "(fictional)" employers with no legal-form suffix, "(sample)" work entries, coined single-word client names, `example.org` links, generated SVG covers under `wwwroot/img/starter/<key>/` (no text, no scripts, no downloads, alt text on every cover and a caption on every picture). Thumbnails are real 1200 x 800 dark-mode screenshots of the rendered home page.

## 5. Acceptance criteria

1. Both keys appear in the template catalog, the wizard's template step and the admin template picker, each with its thumbnail, and pass manifest validation.
2. Every public route renders in the template's own wording with no Software, Healthcare, Gallery, Results or Education wording, and a section the template leaves off answers 404.
3. The home page is the Showcase layout and the menu follows the section order in section 4.
4. Every text and accent token meets 4.5:1 in light and dark, including the four accent choices.
5. Switching to either template and back to Software deletes nothing and restores Software exactly; row counts are identical after round trips with every other template.
6. Export from either template imports into every other template and back, with unknown section keys left inert.
7. Public response headers equal Software's, and the Software parity snapshots pass unedited.
8. A before and after render of 14 routes for Software, Healthcare, Design & UX, Photography & Film, Marketing, Sales and Finance is identical after normalising asset fingerprints.
9. Starter names follow the existing shape rules, including the legal-form suffix rule.

## 6. Testing

Each criterion maps to an automated test, or to a manual check where it is visual.

- **New keyed list.** `ShowcaseArchetypeTests.Keys = ["data-analytics", "product-management"]`, modelled on `GalleryArchetypeTests` and `ResultsArchetypeTests`: partials, starter, blank, sample and full rendering, wording read from the manifest, accessibility, headers. Software stays covered by its parity snapshots and is not in the list.
- **Existing lists extended.** `TemplateManifestTests` (folder list and catalog order), `OnboardingFlowTests` (card counts), `ArchetypeTemplateFlowTests` (admin paths), the stylesheet-coverage test and the accent-contrast test.
- **Mutation testing.** In a scratch copy, break each new guard (hide a heading, swap two sections, lower an accent below 4.5:1, add a legal suffix to a client name) and confirm a test fails.
- **Suite.** The full suite on an LF export of the branch, the way CI builds it, reported as it is. Two tests fail on this Windows host's CRLF checkout (`SoftwareParityTests.DemoPersona_RendersByteForByteLikeTheSnapshots` and `OnboardingAccessibilityAccentTests.WithNoAccentPicked_ThePublicPageHasNoAccentOverride`), the same way as on clean develop.
- **Real-browser pass.** At 390, 768 and 1280 px in light and dark: the wizard card and publish, the public pages, switching templates, the sections editor, export and import, and the keyboard.
- **Independent review.** A `quality-reviewer` pass on the whole diff before the work is called done.

## 7. How the work is split

1. **Lead, on this branch, first.** Commit this spec and the plan. Add both keys to the shared test lists, the wizard card counts, the README template list and the two theme `@import` lines in `Styles/app.css`. Add `ShowcaseArchetypeTests` and stub files for each template, so the shared files are touched only once.
2. **Two subagents in parallel, each in its own worktree** branched from the lead's commit. Each owns only `Templates/<key>/`, `Styles/themes/<key>.css` and `wwwroot/img/starter/<key>/`, and runs its own keyed tests. Neither touches `app.css`, the shared test lists or the README. Separate worktrees stop two builds fighting over `obj/` and `bin/`.
3. **Lead, last.** Merge both commits, rebuild `app.css` from an LF export, run the full suite, take the thumbnails, do the real-browser pass, and run the review.

## 8. How a person sees the change

Sign in at `/admin/login`. Open Appearance, Template (or Tools, Setup wizard, step 1) and choose Data & Analytics or Product / Project Management. Publish the sample content from the wizard, then open `/`, `/projects`, `/experience` and `/cv`.

## 9. Risks

- The wording guards reject another template's nouns, so a natural word may be forbidden. Mitigation: the final wording is checked against every guard list in the plan before the subagents start.
- The Showcase hero's "GitHub" button and waveform backdrop suit Software better than Product / Project Management. Mitigation: recorded as a known gap; a copy-key change is a separate, shared change.
- Two worktrees edit the same catalog-wide tests if a subagent strays outside its files. Mitigation: file ownership is stated in each brief and checked on merge.
- Stacking on an unmerged branch. Mitigation: rebase onto `develop` after the Finance PR merges; the overlap is the shared test lists and `app.css`.
