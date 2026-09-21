# Technical Debt Register

Each entry states the **interest rate** — what it costs per change — so the team can
prioritize paydown. Entries are added in the same change that creates (or discovers)
them, per the post-mortem rule in `AGENTS.md`.

## Open entries

### TD-001 — `Result.cs` is six concepts in one file
- **Where:** `VitaTrack.Core/Models/Result.cs`
- **What:** `NutrientFailure`, `ReplaceNutrientsResult`, `MemberCostRow`,
  `SupplementCostRow`, `NutrientContributionRow`, and the report-data records all live in
  one file.
- **Interest:** every report or result change touches a shared file; review noise and
  merge friction.
- **Paydown:** one file per concept under `VitaTrack.Core/Models/` (or a `Reports/`
  sub-namespace). Not urgent — file is below the 300-line cap.

### TD-002 — `SupplementController` exceeds the type-size split trigger
- **Where:** `VitaTrack.Web/Controllers/SupplementController.cs` (263) +
  `SupplementController.Editor.cs` (50) = 313 lines across partials.
- **What:** controller owns CRUD, CSV import, and nutrient editing; `KnownTypeDebt`
  allowlist in `FileSizeTests` currently suppresses the violation.
- **Interest:** the largest, most-edited controller; new endpoints pile on.
- **Paydown:** split into `SupplementController` (CRUD), `SupplementImportController`
  (CSV), `SupplementNutrientController` (already separate). Removes the `KnownTypeDebt`
  entry. Candidate for the Supplements/CSV slice conversion.

### TD-003 — `DosageParser` should be a `Dosage` value object
- **Where:** `VitaTrack.Core/DosageParser.cs`
- **What:** parsing of free-text `"500 mg"` lives in static helpers; callers recombine
  amount + unit by hand.
- **Interest:** amount/unit logic is duplicated at every call site; unit typos pass
  silently.
- **Paydown:** `Dosage` value object (`Amount` + `Unit`) with `Parse`; `Unit` already
  shipped (`VitaTrack.Core/Primitives/Unit.cs`). ReportingService adopted `Unit`; the
  `Dosage` adoption on `SupplementNutrient.Dosage` is pending the Nutrients slice work.

### TD-004 — report contracts use `Dictionary<string,string>` view data
- **Where:** `ReportingService` (`IReadOnlyList<Dictionary<string,string>>` and similar),
  `Result.cs:23`.
- **What:** report rows are passed to views as stringly-typed dictionaries.
- **Interest:** view typos are runtime-only; no compile-time safety; hard to evolve.
- **Paydown:** typed row records per report. Part of the Reporting slice conversion
  (Visitor over the blend tree).

## Defect log

Escaped defects are recorded here with **injection stage** + **root cause**, feeding the
post-mortem rule (a systemic gap updates `AGENTS.md`/ADR in the same change).

- **DL-001 — `Money +` silently kept the left operand's currency** (found Sep 2026 in branch
  review; fixed same day, "fix: Money mixed-currency addition throws").
  - **Injection stage:** value-object design (Phase 6 rollout) — mixed-currency semantics
    left unresolved and papered over with a "caller guarantees same currency" comment.
  - **Detection stage:** human code review. Unit tests and CI **both passed** the defective
    semantics — the tests were written by the same agent that wrote the defect, so they
    encoded the bug as expected behavior. Evidence for keeping review mandatory.
  - **Systemic gap:** the value-object recipe had no rule about invalid-combination
    semantics. Closed in `new-shard.md` (value objects fail loudly on invalid
    combinations; test the error edges, not just the happy path).
