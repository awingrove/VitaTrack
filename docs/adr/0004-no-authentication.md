# ADR-0004: No authentication

- **Status:** Accepted
- **Date:** 2026-08-05 (retroactively documented 2026-08-13)

## Context

VitaTrack is a self-hosted family-supplement tracker. Plans were
always: one instance per household, not multi-tenant.

- Adding ASP.NET Identity (tables, migrations, login UI) would
  dominate first-week scope with no functional return for the
  intended use.
- Source repo size grows ~30%, important when the project's main
  reason to exist is exposing LLM-enrichment patterns cleanly.
- Inline LLM enrichment flow is interesting only as a thin layer
  above the supplement product facts; auth UX competes with that
  priority.

## Decision

No authentication. The single web app is open by design when run
locally. Deployment recommendation: behind a household reverse
proxy or a tunnel (Tailscale / claudflare gateway); not on the
open internet.

## Consequences

- No `Users`, `Roles`, `Claims` tables (auditing root AGENTS.md
  foreign key contract stays short).
- No anti-CSRF machinery beyond what MVC scaffolds
  (`[ValidateAntiForgeryToken]` is present on all POST actions).
- Cookies are not authenticated; no session story.
- Pivoting to multi-user requires a fresh ADR documenting the
  surface added (PasswordHasher, UseCase per-user scoping on every
  repo query, user FK on Supplement / FamilyMember for row-level
  isolation). This is a deliberate `Supersede` event, not a gap.