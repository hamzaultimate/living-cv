# CV ATS scoring

How the `/cv` page's "ATS Score" badge is produced. Added in PR #12
("Add CV ATS scoring: rule-based checks, offline console tool, /cv badge").

## Why it's offline, not live

The score is **not** computed by the running web app on each request. It's generated ahead of
time by a standalone console tool and written to a static JSON file that ships with the site.
That keeps `/cv` fast, keeps the (optional) Claude API key off the deployed web server, and means
the score only changes when someone deliberately regenerates it — not on every page load.

## Pieces

| Piece | File | Role |
|---|---|---|
| Rule scorer | `src/Portfolio.Web/Services/CvAtsRuleScorer.cs` | Pure, deterministic — 8 checks, 0–100 |
| Report model | `src/Portfolio.Web/Services/CvAtsReport.cs` | `CvAtsReport` record persisted as JSON |
| Report reader | `src/Portfolio.Web/Services/CvAtsReportProvider.cs` | Singleton; reads `Seed/ats-report.json` once, caches in memory, returns `null` if missing/malformed |
| Report generator | `tools/CvAtsReview/Program.cs` | Standalone console app — reads seed content directly (no DB), runs the scorer, optionally calls Claude, writes the report |
| Generated report | `src/Portfolio.Data/Seed/ats-report.json` | Committed to the repo — the badge's data source at render time |
| Badge UI | `src/Portfolio.Web/Pages/Cv.cshtml` | Renders the badge; hides itself entirely if the provider returns `null` |

## The rule scorer (deterministic, always runs)

`CvAtsRuleScorer.Score` runs 8 checks against the same `CvData` the CV page and PDF use, summing
to 100:

| Check | Points | Passes when |
|---|---|---|
| Contact | 15 | Email present plus at least one other contact method |
| Summary | 10 | Summary is 40–500 characters |
| Experience | 15 | At least one entry, and every entry has Company/Title/Start |
| Impact | 20 | ≥50% of experience-highlight / project-impact lines contain a digit, `%`, `$`, `€`, or `£` (a proxy for "quantified") |
| Length | 10 | No highlight, project summary, or impact line exceeds 220 characters |
| Skills | 10 | At least one skill group, and no group is empty |
| Dates | 10 | All experience dates use one consistent format (`Mon YYYY` or `YYYY`) |
| Bonus | 10 | At least one certification or project present |

Each check also produces a `CvAtsFinding` (category, passed, human-readable message) — these are
what render as the ✓/✗ list under the badge.

## The optional AI half

`tools/CvAtsReview` looks for an `ANTHROPIC_API_KEY` environment variable:

- **Not set** → writes a rule-only report (`AiScore` stays `null`). This is the default for
  anyone cloning the public template, and for this repo's own CI.
- **Set** → calls Claude (`claude-opus-5`, structured JSON output) for a 0–100 impact/clarity
  score plus up to 3 strengths and 5 critiques, and combines it with the rule score:
  `CombinedScore = round(0.5 * ruleScore + 0.5 * aiScore)`.

The AI prompt is explicitly scoped to impact/clarity, not keyword matching — that's what the
rule scorer already covers, so the two halves aren't redundant.

## Regenerating the report

Run from the repo root — no running web app or database required, since the tool reads
`src/Portfolio.Data/Seed/*.json` and `src/Portfolio.Web/appsettings.json` directly:

```bash
dotnet run --project tools/CvAtsReview
```

Add `ANTHROPIC_API_KEY=<key>` to the environment first if you also want the AI half. The tool
overwrites `src/Portfolio.Data/Seed/ats-report.json` in place.

**Regenerate and commit the updated report whenever CV-relevant content changes** — experience,
projects, skills, certifications, or `Site` identity fields (name, role, summary, contacts).
Because the report is read statically, the badge otherwise silently drifts out of sync with what
the CV page actually shows.

## Failure modes are intentionally quiet

- `CvAtsReportProvider` swallows a missing file or a `JsonException` and returns `null` — the
  `/cv` page checks for `null` and simply omits the badge rather than erroring.
- The console tool itself is not silent: if `ANTHROPIC_API_KEY` is set but the Claude call fails,
  it prints the error and exits with code `1` rather than writing a partial report.
