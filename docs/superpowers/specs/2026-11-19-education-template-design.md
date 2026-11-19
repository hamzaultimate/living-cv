# Education template on the Credentials layout

Date: 2026-11-19
Status: design approved in brainstorming, awaiting written-spec review
Branch: `feature/phase-5d-education-template`, stacked on `feature/phase-5c-finance-template` (rebase onto `develop` once the Finance PR merges)
Delivery: one pull request (Phase 5, item 3 of the numbered list in `docs/templates-and-onboarding-plan.md`)

## 1. Goal

Add Education, a template for school teachers, on the existing Credentials layout, as a data-only preset. A teacher's site leads with teaching qualifications and registration, then teaching experience, then curriculum work, in the teacher's own words.

## 2. Decisions made

| Topic | Decision |
|---|---|
| Audience | School teacher (primary or secondary classroom). Not a broad teacher-and-trainer template, not higher-ed academics |
| Approach | Data-only preset on the existing Credentials layout. No new layout, no new section |
| Sensitivity | `sensitive: true` with a guidance note about pupil privacy, repeated on the editors, as Healthcare does |
| Branch | Stacked on the Finance branch, because the keyed test lists and catalog order already contain `finance-accounting` |

## 3. Constraints

- No C# change, no Razor change, no migration, no CSP change, no new script, no new copy key. `/cv`, `/cv.pdf` and `/og.png` keep the Software look (Phase 6).
- No layout partial, block or page names a template by key, theme or profession (an existing test greps for this).
- The Software, Healthcare, Gallery, Results (Marketing, Sales, Finance) public output must not change. The shared stylesheet changes only by the new `[data-template="education"]` theme rules.
- The committed `src/Portfolio.Web/wwwroot/css/app.css` is the LF build output that CI regenerates and compares. On Windows with `core.autocrlf=true` a local build writes a byte-different file; restore the committed file or build from an LF export before committing.
- Out of scope: per-template CV output (Phase 6), the Finance owner decisions, the shared-layout gaps already listed in the plan, and a SQL Server run (no migration).

## 4. The preset

- **Identity.** Key `education`, name "Education", profession "School teacher", archetype `credentials`, `sensitive: true`.
- **Guidance.** Never publish pupil-identifiable information: names, photographs, work samples that carry names, behaviour, attendance, special-educational-needs or safeguarding details, or anything that identifies a pupil, family or colleague. Check the school's and employer's policies before publishing. It is shown on the wizard card, the review step and the template page, and repeated on the editors.
- **Sections, in menu order.** about; qualifications (label "teaching qualifications & registration", short label "qualifications"); experience ("teaching experience", short "experience"); skills ("teaching skills", short "skills"); projects ("curriculum & initiatives", short "initiatives", item label "Initiative"); testimonials ("references"); contact. Blog and Talks are off, and the Talks `youtubeId` editor field is hidden.
- **Skill groups.** Teaching and learning, Curriculum and assessment, Pastoral and inclusion, Technology, Languages.
- **Wording.** All visible nouns come from the manifest (`copy`, section labels, `editor` field labels and hints), covering every key the Healthcare manifest sets, so nothing falls back to Software or Healthcare wording. Editor hints for experience, projects and talks warn against pupil information, in the way Healthcare's warn against patient information.
- **Structured data.** `seo.credentialsJsonLd: true`, listing held qualifications only.
- **Theme.** A warm, calm look distinct from Healthcare's blue: ink dark and cream light, with a deep green default accent and four accent choices (green, terracotta, blue, gold), and a system font stack. Exact values are set in the plan and checked against the contrast rule. Every text and accent token is at least 4.5:1 on bg, surface and surface-2 in light and dark.
- **Starter content.** An invented persona; fictional schools marked "(fictional)" with no legal-form suffix; sample initiatives marked "(sample)"; qualifications from invented bodies with "Example ..." names; two references; `example.org` links; generated SVG placeholders under `wwwroot/img/starter/education/` (no text, no scripts, no downloads, alt text on every cover and a caption on every picture); no pupil data of any kind. The thumbnail is a real 1200 x 800 dark-mode screenshot of the rendered home page.

## 5. Acceptance criteria

1. `education` appears in the template catalog, the wizard's template step and the admin template picker, with its thumbnail and guidance note, and passes manifest validation.
2. Every public route renders in the template's wording with no Software or Healthcare wording, and sections the template leaves off answer 404.
3. The home page leads with qualifications and recent roles (the Credentials layout), and the menu follows the section order in 4.
4. The held-only qualifications rule holds in the Person structured data in both directions (held listed, in progress or planned not listed).
5. The guidance note appears on the wizard card, the review step, the template page and each editor.
6. Every text and accent token meets 4.5:1 in light and dark, including the four accent choices.
7. Switching to Education and back to Software deletes nothing and restores Software exactly; row counts are identical after round trips with every other template.
8. Export from Education imports into every other template and back, with unknown section keys left inert.
9. Public response headers equal Software's, and the Software parity snapshots pass unedited.
10. A before and after render of the 14 routes for Software, Healthcare, Gallery, Marketing, Sales and Finance is identical after normalising asset fingerprints.
11. Starter names follow the existing shape rules, including the legal-form suffix rule, and no starter text names a pupil.

## 6. Testing

Each criterion maps to an automated test, or to a manual check where it is visual.

- **Credentials keyed list.** Add `CredentialsArchetypeTests.Keys = ["healthcare", "education"]` and make the Healthcare-named rendering checks in `TemplateRenderingTests` run over it. `DemoSite.HealthcareWording` becomes a per-key wording guard, as the Gallery and Results lists already work. The Healthcare assertions keep their strength.
- **Existing lists extended.** `TemplateManifestTests` (folder list and catalog order), `OnboardingFlowTests` (card and guidance counts), the stylesheet-coverage test, and the accent-contrast test (which already runs for every template in the catalog).
- **New guards.** The guidance appears on every editor for a `sensitive` template; the held-only credentials markup; the starter has no legal-form suffix and no pupil-identifying shape.
- **Mutation testing.** In a scratch copy, break each new guard (hide a heading, drop `credentialsJsonLd`, swap two sections, lower an accent below 4.5:1, add a suffix to a name) and confirm a test fails.
- **Suite.** The full suite on an LF export of the branch, the way CI builds it, with the result reported as it is. Two tests fail on this Windows host's CRLF checkout, `SoftwareParityTests.DemoPersona_RendersByteForByteLikeTheSnapshots` and `OnboardingAccessibilityAccentTests.WithNoAccentPicked_ThePublicPageHasNoAccentOverride`. They fail the same way on clean develop and are not a defect of this work.
- **Real-browser pass.** At 390, 768 and 1280 px in light and dark: the wizard card and publish, the public pages, switching templates, the sections editor, export and import, and the keyboard.

## 7. How a person sees the change

Sign in at `/admin/login`. Open Appearance, Template (or Tools, Setup wizard, step 1) and choose Education. Publish the sample content from the wizard, then open `/`, `/experience`, `/certifications`, `/projects` and `/cv`.

## 8. Risks

- Tests for the Credentials layout are Healthcare-named today. Generalising them risks weakening a Healthcare check. Mitigation: keep the same assertions, drive them from a keyed list, and mutation-test.
- The starter could look like real people or schools. Mitigation: invented persona, "(fictional)" and "(sample)" marks, coined or generic names, and the existing shape rules, with a test.
- Stacking on an unmerged branch. Mitigation: rebase onto `develop` after the Finance PR merges; the only overlap is the shared test lists and `app.css`.
