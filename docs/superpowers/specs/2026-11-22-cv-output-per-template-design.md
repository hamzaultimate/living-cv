# CV output per template (Phase 6a): design

Date: 2026-11-22. Branch: `feature/phase-6a-cv-per-template`, based on `feature/phase-5e-showcase-templates`.
Roadmap: `docs/templates-and-onboarding-plan.md`, "Phase 6: CV output per template", first half. The second half (ATS score
computed on save and stored in the DB) is Phase 6b and has its own spec.

## Goal

`/cv` (HTML) and `/cv.pdf` (QuestPDF) follow the active template: the order of their sections, their headings and certification
status words, and their accent colour come from the template manifest. Software output is unchanged byte for byte.

## Today

- `PortfolioContentService.BuildCvDataAsync` already resolves the template. It gates sections with `IsEnabled`, orders skill groups,
  and fills `CvHeadings` from `copy.cv.*` and `copy.certifications.status*`.
- The section order is hard-coded in both renderers: Summary, Experience, Skills, Projects, Certifications
  (`CvDocument.cs`, `Pages/Cv.cshtml`). It differs from the site's `sections[]` order (experience, projects, skills, certifications),
  so reusing `sections[]` would reorder Software's CV and break `SoftwareParityTests`.
- The PDF accent is a constant `#1D4ED8`. The HTML picks up the template theme through CSS variables.
- The HTML prints the certification status enum name (`Earned`); the PDF maps it to the template's wording.
- The PDF upper-cases a default heading and the HTML uses manifest casing, so "Selected projects" differs between them.

## Design

### Manifest

`copy.cv` gains two optional fields, added to `TemplateDefinition.CvCopy` and `template.schema.json`:

- `order`: a list of CV section keys, a subset of `experience`, `skills`, `projects`, `certifications`, no duplicates. Default (field
  absent): `experience, skills, projects, certifications`, which is today's order.
- `accent`: a `#rrggbb` print colour. Default (field absent): `#1D4ED8`, today's PDF colour.

All nine shipped manifests set both. Software sets the defaults explicitly, so its output does not change. The summary is always
first and is not part of `order`.

### Accent resolution

1. If the owner picked one of the template's `theme.accents[]`, use that option's `light.strong`.
2. Otherwise use `copy.cv.accent`.

The colour must reach at least 4.5:1 contrast on white. A manifest test enforces this for `copy.cv.accent` and for every
`light.strong` value.

### Data flow

`CvData` gains `SectionOrder` (ordered keys, disabled sections already removed) and `Accent` (hex). `BuildCvDataAsync` fills them from
the resolved template and the owner's accent. Both renderers loop over `SectionOrder` instead of the fixed sequence, and use `Accent`
instead of the constant. Defaults on the new `CvData` members keep `CvAtsRuleScorer` and `tools/CvAtsReview` compiling and scoring
as before.

### Bundled fixes

- The HTML shows the template's certification status word, as the PDF does.
- Headings in both renderers use the manifest text as written (no upper-casing), so the two outputs agree.

### Out of scope

Per-archetype PDF layouts, the PDF font gap, `/og.png`, the PDF filename, any change to the ATS badge or scorer (6b), and contact
fields beyond the current four.

## Testing

Tests are written first.

1. `SoftwareParityTests` passes with no snapshot edit. This is the proof that Software is unchanged.
2. A manifest test for all nine templates: `order` has only known, unique keys; `accent` is valid hex and meets 4.5:1 on white; every
   `accent` option's `light.strong` meets 4.5:1 on white.
3. Per template, `/cv` renders sections in its `order` with its headings and status words; on non-Software templates the existing
   software-wording check still passes.
4. An owner-picked accent changes the HTML and PDF colour; with no pick the template's `accent` is used.
5. A disabled section is absent from `/cv` and from the PDF data.
6. `/cv.pdf` stays deterministic for equal input and changes when content changes (`EveryWrite_RefreshesTheGeneratedCvAtOnce` keeps
   passing). PDF tests keep to blank sites or sites without impact lines because of the font gap.
7. Mutation checks of the new guards, as in Phase 5.

## How a human sees it

Path `/cv` and `/cv.pdf` after choosing a template in `/setup` or in the admin template switcher. Precondition: sample or real content
with experience, skills, projects and certifications. Steps: switch templates and compare section order, headings, status words and
accent; pick a different accent and reload.

## Risks

- Windows Razor output carries CRLF, so two parity-style tests fail locally for an unrelated reason. Compare against the parent
  commit's result, as the Phase 5 notes did.
- The PDF needs glyph fonts that some hosts lack. Verification of `/cv.pdf` visuals needs a host with them.
