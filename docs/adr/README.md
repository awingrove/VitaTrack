# Architecture Decision Records

This directory holds short-lived records of significant architectural
choices. Each file follows the [Michael Nygard
template](https://cognitect.com/blog/2011/11/15/documenting-architecture-decisions):

- **Title** — short noun phrase
- **Status** — Proposed / Accepted / Deprecated / Superseded
- **Context** — why this decision came up; forces in play
- **Decision** — what we chose
- **Consequences** — what follows (positive, negative, neutral)

ADRs are append-only. To reverse a decision, **add** a new ADR marked
`Supersedes ADR-NNNN` and mark the old one **Superseded**, don't edit
the original.

Conventions here:
- File name: `NNNN-kebab-case-title.md`, zero-padded number, single
  digit is fine for now (start at 0001).
- Keep each ADR under ~80 lines. If longer, link out to a design doc.

| # | Title | Status |
| - | ----- | ------ |
| 0001 | Pragmatic MVC over Clean Architecture | Accepted |
| 0002 | Dapper + raw SQL over Entity Framework Core | Accepted |
| 0003 | SQLite (file + in-memory shared cache) over PostgreSQL | Accepted |
| 0004 | No authentication | Accepted |
| 0005 | HTMX + vanilla JS for UI interactivity | Accepted |