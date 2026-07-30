# CvAtsReview

Offline console tool that scores the CV content for ATS-readiness and writes the result to
`src/Portfolio.Data/Seed/ats-report.json`, which the `/cv` page reads statically at render time.

```bash
dotnet run --project tools/CvAtsReview
```

Reads seed JSON and `appsettings.json` directly — no database or running web app needed. Set
`ANTHROPIC_API_KEY` in the environment beforehand to also get an AI-generated impact/clarity
review; without it, the tool writes a rule-only report.

See [docs/cv-ats-scoring.md](../../docs/cv-ats-scoring.md) for the full write-up: what the 8 rule
checks are, how the AI score is combined, and when to regenerate the report.
