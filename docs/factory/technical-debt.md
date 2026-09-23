# Technical Debt Register

Each entry states the **interest rate** — what it costs per change — so the team can
prioritize paydown. Entries are added in the same change that creates (or discovers)
them, per the post-mortem rule in `AGENTS.md`.

## Open entries

### TD-003 — `DosageParser` should be a `Dosage` value object
- **Where:** `VitaTrack.Core/DosageParser.cs`
- **What:** parsing of free-text `"500 mg"` lives in static helpers; callers recombine
  amount + unit by hand.
- **Interest:** amount/unit logic is duplicated at every call site; unit typos pass
  silently.
- **Paydown:** `Dosage` value object (`Amount` + `Unit`) with `Parse`; `Unit` already
  shipped (`VitaTrack.Core/Primitives/Unit.cs`). ReportingService adopted `Unit`; the
  `Dosage` adoption on `SupplementNutrient.Dosage` is pending the Nutrients slice work.

## Closed entries

### TD-002 — `SupplementController` exceeds the type-size split trigger
- **Where:** was `VitaTrack.Web/Controllers/SupplementController.cs` (263) +
  `SupplementController.Editor.cs` (50) = 313 lines across partials.
- **What:** controller owned CRUD, CSV import, and nutrient editing; `KnownTypeDebt`
  allowlist in `FileSizeTests` suppressed the violation.
- **Closed 2026-09-23:** CSV import extracted to `SupplementImportController` +
  `ImportSupplementsHandler`; controller is back to 190 + 52 partial lines and the
  `KnownTypeDebt` set is deleted from `FileSizeTests`. MS slice conversion.

### TD-001 — `Result.cs` is six concepts in one file
- **Where:** was `VitaTrack.Core/Models/Result.cs`
- **What:** `NutrientFailure`, `ReplaceNutrientsResult`, `MemberCostRow`,
  `SupplementCostRow`, `NutrientContributionRow`, and the report-data records all lived in
  one file.
- **Interest:** every report or result change touched a shared file; review noise and
  merge friction.
- **Closed 2026-09-23:** report records moved one-file-per-concept under
  `VitaTrack.Core/Features/Reporting/` in the RP slice conversion; `Result.cs` deleted
  (the nutrient-result records had already moved to the NT slice).

### TD-004 — report contracts use `Dictionary<string,string>` view data
- **Where:** was `ReportingService` (`IReadOnlyList<Dictionary<string,string>>` and
  similar), `Result.cs:23`
- **What:** report rows were passed to views as stringly-typed dictionaries.
- **Interest:** view typos were runtime-only; no compile-time safety; hard to evolve.
- **Closed 2026-09-23:** typed row records (`MemberNutrientTotals`,
  `NutrientContributionsCell`, `NutrientUnitRow`, …) passed straight to the views —
  the ViewData JSON round-trip is gone. RP slice conversion.

### TD-005 — `FamilyRepository.DeleteAsync` issued cross-slice SQL against `PrescribedDoses`
- **Where:** `VitaTrack.Core/Data/FamilyRepository.cs`
- **What:** deleting a family member ran raw `DELETE FROM PrescribedDoses` — a second
  instance of the cross-slice SQL violation fixed for `SupplementRepository` in the NT
  conversion. Found by the glm-flash NT session (its notice, correctly not fixed in-scope).
- **Interest:** the ADR-0006 invariant was untrue for the MF→PD edge; every audit re-found it.
- **Closed 2026-09-23:** `DeleteByFamilyMemberIdsAsync` added to `IPrescribedDoseRepository`
  and routed — same pattern as the NT fix. Remaining exposure: the invariant still has no
  machine check (tracked as the cross-slice arch test follow-up in the factory-v3 plan).

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

- **DL-002 — NT briefing contained two design-stage defects, caught by the executing
  cheap-model session** (found Sep 2026 during the NT slice conversion; no code damage —
  both resolved by logged mechanical deviations).
  - **Defect a (step ordering):** the briefing put all `shards.yaml` surgery in step 5,
    but the pre-commit hook runs ShardOwnershipTests on every commit — file moves in
    step 2 cannot go green without re-pointing the five core paths in the same commit.
    The agent re-pointed paths in step 2 and left full ownership surgery for step 5.
  - **Defect b (wrong current-state claim):** the briefing said `BlendEnrichmentTests`
    "stays in LLM" but it was claimed by MS; the agent moved the claim to LLM, matching
    intent.
  - **Injection stage:** briefing authoring (design). **Detection stage:** cheap-model
    execution with escalation protocol — the deviations were logged, not silent.
  - **Systemic gap:** briefings stated current-state facts from memory instead of
    checking the manifest, and weren't dry-run against the guardrail gating each step.
    Closed in `design-review.md` (checklist now requires verifying claimed current-state
    facts against `shards.yaml`, and dry-running each step against its gate).
