# ADR-0001: Pragmatic MVC over Clean Architecture

- **Status:** Accepted
- **Date:** 2026-08-05 (documented retroactively in 2026-08-13)

## Context

A 3-project solution (`VitaTrack.Web → VitaTrack.Infrastructure`,
`VitaTrack.Tests` refs both) had to be picked before any code was
written. Forces in play:

- Single maintainer, evolving scope; no team to enforce onion metrics.
- Entity Framework Core isn't desired (see ADR-0002) — eliminates
  most Clean Architecture scaffolding that exists to abstract EF.
- Business logic is small (aggregations, nutrient persistence, LLM
  call); not enough complexity to justify domain vs application vs
  infrastructure split.
- Inter-project direction is already enforced by csproj references
  (`Web → Infrastructure`, no reverse). An architecture test [see
  `VitaTrack.ArchitectureTests.WebLayerDependencyTests`] makes the
  rule explicit and CI-gated.

## Decision

Use **pragmatic ASP.NET MVC** with three projects only:

- `VitaTrack.Web` — controllers, views, thin mapping. No business
  logic. ~300-line file limit per AGENTS.md.
- `VitaTrack.Infrastructure` — Dapper repositories + services +
  models. Service layer in `Infrastructure/Services` carries domain
  rules (e.g. `ReportingService`, `SupplementNutrientService`).
- `VitaTrack.Tests` — MSTest unit tests + `VitaTrack.ArchitectureTests`
  for rules csproj can't express.

No `Application`/`Domain` split. No use-case classes. Controllers
delegate to services / repositories. ViewModels live in `Web`.

## Consequences

- Behavior: simple to navigate; nothing to discover under
  `Application/Services/UseCases/…`.
- Maintainer additions stay small (no 4-project boilerplate tax).
- New business rules go into `Infrastructure/Services`, not Web.
- Boundaries are enforced by csproj + NetArchTest, not by
  architectural hope.
- If multi-tenant / multi-app surfaces later, reconsider — this
  structure assumes one Web app consumes Infrastructure.