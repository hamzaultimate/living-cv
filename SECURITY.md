# Security Policy

## Supported Versions

This is an actively developed template; security fixes are applied to the default branch.

## Reporting a Vulnerability

Please **do not open a public issue** for security vulnerabilities.

Instead, report privately via GitHub's
[**Private vulnerability reporting**](https://github.com/hamzaultimate/living-cv/security/advisories/new)
(Security → Report a vulnerability). If that is unavailable, contact the maintainer through
their [GitHub profile](https://github.com/hamzaultimate).

Please include:

- A description of the vulnerability and its impact
- Steps to reproduce (proof of concept if possible)
- Affected files or endpoints

You can expect an initial response within a few days. Thank you for helping keep the project and
its users safe.

## Notes for forkers

This template never commits secrets. When you deploy your own instance:

- Set the database connection string via the `ConnectionStrings__Default` environment variable
  (or `appsettings.Production.json`) — never commit real credentials.
- Keep deployment secrets (FTP, DB) in your CI provider's secret store, not in the repo.
