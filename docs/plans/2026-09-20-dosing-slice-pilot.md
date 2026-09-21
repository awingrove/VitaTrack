# Dosing Slice Pilot — Lessons Learnt

- **Status:** Done (2026-09-21)
- **Slice:** `VitaTrack.Core/Features/Dosing/` (PD feature)
- **Gates:** build ✓ · 12 arch tests ✓ · 178 unit tests ✓ · 10 prescribed-dose e2e ✓

## What changed

- Moved `PrescribedDose` model, `IPrescribedDoseRepository` + `PrescribedDoseRepository`
  out of `VitaTrack.Core.{Models,Data}` into the `Features/Dosing` slice (namespace
  `VitaTrack.Core.Features.Dosing`).
- New value objects: `DoseMultiplier` (range 0.01–1000, step 0.25, server is authority)
  and `DosePeriod` (owns `IsActiveOn(DateTime)`).
- New request DTOs `CreateDoseRequest` / `EditDoseRequest`; controller no longer binds the
  entity directly (kills the overposting risk at `PrescribedDoseController.cs:40,65`).
- Handlers `PrescribeDoseHandler` / `AmendDoseHandler` carry the create/update rules;
  `Index` and `Delete` stay thin controller→repository.
- `ReportingService.GetActiveDosesAsync` no longer hand-rolls the active-dose rule — it
  calls `dose.IsActiveOn(today)` (the leaked rule is now in the slice that owns it).
- `shards.yaml` PD entry repointed to the slice; `RepositoryNamingTests` relaxed to allow
  repository implementations in `VitaTrack.Core.Data` **or** `VitaTrack.Core.Features.*`.

## Lessons Learnt

1. **Handler-per-rule threshold held.** Only create/update got handlers; CRUD passthrough
   (Index/Delete) stayed thin. The threshold is the right call — no ceremony where there's
   no rule. (This was the open question from the pilot design; resolved: keep the threshold.)

2. **Value objects paid off immediately.** `DosePeriod` removed a real cross-slice leak
   (the active-dose rule lived in Reporting). `DoseMultiplier` centralized a rule that was
   split between a `[Range]` attribute and client JS. Both are small, readable, and worth it.
   Rollout should keep converting primitives → value objects (`Dosage`, `Unit`, `Money`).

3. **Rename/relocation ripple is mechanical but real.** Moving the type touched ~10 consumer
   files (usings). `ShardOwnershipTests` and the build caught nothing broken — the guardrails
   did their job. Expect the same per slice; budget a few minutes of using-updates each time.

4. **The repo-in-slice move needed an architecture-test tweak.** `RepositoryNamingTests`
   originally pinned repos to `VitaTrack.Core.Data`. VSA puts repos in slices, so the rule
   was widened. Lesson: the factory's own tests must track the architecture decision
   (ADR-0006) — when a new slice moves its repo, this test should already permit it.

5. **Cross-slice SQL invariant is NOT yet machine-enforced.** ADR-0006 states a slice reads
   others only via published interfaces and never issues SQL on another slice's tables. This
   holds by convention today (`ReportingService` reads via `IPrescribedDoseRepository`). A
   reliable arch test would have to parse inline SQL for table names — brittle. **Deferred**:
   add a lighter check later (e.g., each `Features/*` repo's SQL references only its owned
   tables) once the table→slice map is explicit; for now the convention + this exemplar is
   the control.

6. **DTO binding change was low-risk.** The views' `@model` swapped from `PrescribedDose` to
   the request DTO with identical field names, so `asp-for` bindings needed no edits. The
   overposting fix came essentially for free.

7. **No-MediatR decision validated.** Two tiny handler classes via explicit DI — more
   readable than a pipeline, zero dependencies. Keep for rollout.

## Rollout gate

Pilot confirms the slice shape is correct and the guardrails hold. Proceed to remaining
slices in this order (each gated by `verify-shard`):

1. **Nutrients** — `Dosage` + `Unit` value objects; `DosageParser` → `Dosage.Parse`;
   **Composite** for the blend hierarchy; **Strategy** for unit normalization.
2. **Reporting** — **Visitor** over the blend tree; kill `Dictionary<string,string>` report
   contracts (`Result.cs`); `Money` value object.
3. **Supplements / CSV** — **Chain of Responsibility** for row validation; split the
   `SupplementController` partial-class debt (tracked in `FileSizeTests` allowlist).
4. **Family**, **LLM** — remaining slices.
5. Split `Result.cs` into one file per concept.
