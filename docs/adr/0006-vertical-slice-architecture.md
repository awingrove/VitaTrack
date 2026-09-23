# ADR-0006: Vertical Slice Architecture within Pragmatic MVC

- **Status:** Accepted
- **Date:** 2026-09-21
- **Supersedes:** ADR-0001 (organization decision only; ADR-0001's 3-project boundary remains in force)

## Context

ADR-0001 chose pragmatic MVC with a 3-project solution and explicitly rejected
`Application`/`Domain` splits, on the force *"single maintainer, no team to enforce
onion metrics."* That force no longer holds: the codebase is now built by an agent
fleet plus a machine-enforced factory (arch tests, story-map consistency, UI
reachability). The boundary decision of ADR-0001 — three projects, csproj-enforced
direction, no boilerplate tax — still pays and is retained. Its *organizing*
decision (business rules in `Infrastructure/Services`; no use-case classes) now
hinders clarity:

- Domain logic lives in a project named `Infrastructure` (a naming lie that ages badly).
- Logic bloats: `SupplementController.cs` 263 lines + partial `SupplementController.Editor.cs`;
  `ReportingService.cs` 218 lines.
- No file in the repo *is* a feature — the agent reassembles a slice across five folders.
- `ReportingService.cs:206-208` implements the "is this dose active today" rule that
  belongs to Dosing; `PrescribedDoseController.cs:40,65` binds entities directly (overposting risk).

## Decision

Adopt **Vertical Slice Architecture** *within* the existing three projects:

- Each feature is a **slice**: a folder (`VitaTrack.Core/Features/<Feature>/`) owning its
  handlers/use-cases, repository interface + implementation, domain models, value objects,
  and result records. Web keeps thin per-feature controllers; `Views/<Controller>/` stays
  (Razor convention); they link to the slice via the shard manifest.
- **Explicit handler classes** injected into controllers — **no MediatR**. Rationale:
  seven controllers, zero dependency, more readable, equally SOLID (SRP + DIP).
- **Handler-per-operation threshold:** extract a handler when the operation *carries a
  rule* (validation, defaults, cross-entity coordination); do not wrap a CRUD passthrough.
- **Cross-slice invariant:** a slice may read another slice's data only through that
  slice's published repository/query interface; it must never issue SQL against another
  slice's tables. (True today — `ReportingService.cs:204` reads doses via
  `IPrescribedDoseRepository`, no cross-slice SQL; an arch test locks it.)
  *Erratum, 2026-09-23: no arch test enforces this today — the claim above was
  aspirational and is corrected by `new-shard.md` (invariant split) and pilot lesson #5.
  Enforcement is convention + routed deletes (`AGENTS.md`) + the `design-review.md`
  human gate; the machine check is a tracked follow-up in the factory-v3 plan. TD-005
  in `technical-debt.md` records the violation the audit found and its fix.*
  *Update, 2026-09-23 (same day): `CrossSliceSqlTests` now enforces the table clause —
  each slice's SQL must reference only tables declared in its `tables` map in
  `shards.yaml`; declared cross-slice read dependencies are visible manifest diffs for
  design review. The interface-routing clause remains the human gate. The follow-up
  task in the factory-v3 plan is complete.*
- **Value objects over primitives/strings** where the domain demands: `DoseMultiplier`,
  `DosePeriod` (pilot); `Dosage`, `Unit`, `Money` (rollout).
- The project is renamed `VitaTrack.Infrastructure` → `VitaTrack.Core`. `Core` means
  "everything below the web surface" (feature slices + persistence + external clients);
  unlike `Domain`/`Application` it makes no false promise about containing no SQL.

## Consequences

- Obvious place to change a feature: open its slice folder.
- SRP/DIP without framework ceremony.
- Cross-slice coupling is visible and tested, not implicit.
- Renaming touches every csproj/namespace reference (done in Phase 0); future moves are
  slice-scoped.
- If slices grow beyond one app surface or need true multi-tenancy, reconsider — this
  still assumes one Web app consuming Core.
