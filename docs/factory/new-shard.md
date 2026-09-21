# new-shard — scaffold a VitaTrack feature slice

This is the committed, versioned recipe behind the `new-shard` skill. It codifies the
Dosing pilot (`VitaTrack.Core/Features/Dosing/`) as a repeatable way to build a vertical
feature. Read it before adding or extracting a slice.

> Also loadable as a local skill at `.claude/skills/new-shard/SKILL.md` (not committed —
> `.claude/` is gitignored). This doc is the source of truth.

## When to use

- A new user-facing capability (new entity + CRUD + report) that deserves its own bounded
  context.
- Extracting a tangled area (e.g. `SupplementController`, 263 + 50 partial lines) into a
  slice.
- **Do NOT use** for cross-cutting primitives (`Unit`, `Money`, `Dosage` live in
  `VitaTrack.Core/Primitives/` and are allowlisted in `shards.yaml`).

## Hard invariants (enforced by architecture tests)

1. **Ownership** — every feature `.cs` under `VitaTrack.Core` belongs to exactly one shard
   in `shards.yaml` (or the `allowlist`). `ShardOwnershipTests` fails on orphans and
   double-claims.
2. **Cross-slice reads** — a slice reads another slice's data ONLY via that slice's
   published repository/query interface. Never issue SQL on another slice's tables.
3. **Type size** — no complete type (incl. partials) exceeds 300 lines (`FileSizeTests`).
   Split on the split-trigger; don't dodge with partials.
4. **Repository naming** — repos end in `Repository`; live in `VitaTrack.Core.Data` or a
   `VitaTrack.Core.Features.*` slice (`RepositoryNamingTests`).

## Recipe (tracer-bullet, then fill)

1. **Claim the slice.** Add an entry to `shards.yaml` under `slices:` with `id`
   (2-letter, also a `storymap.yaml` task-id prefix), `name`, `controller`, `core`,
   `views`, `js`, `unit_tests`, `e2e_specs`. Resolve every path to a real file
   (ShardOwnershipTests errors on globs that match nothing). Add a matching
   `storymap.yaml` task with an `entry_point`.
2. **Model + repository in the slice dir.**
   `VitaTrack.Core/Features/<Name>/<Entity>.cs`, `I<Entity>Repository.cs`,
   `<Entity>Repository.cs`. Repos are Dapper-only; delete child rows before parents (FK
   order in `AGENTS.md`).
3. **Value objects, not primitives.** Wrap amounts/units/money in value objects
   (`DoseMultiplier`, `DosePeriod`, `Unit`). A bare `decimal`/`string` for a domain
   amount is a defect.
4. **Request DTOs + handlers.** One request record per write (`CreateXRequest`,
   `EditXRequest`), one handler per business rule (`PrescribeXHandler`,
   `AmendXHandler`). Handlers return a result record (`XCommandResult`); no exceptions for
   control flow. Controller stays thin: bind the DTO, call the handler, return
   view/redirect. Never bind entities from the form.
5. **Thin controller + views.** Controller actions map to `shards.yaml` `controller` paths.
   Views under `VitaTrack.Web/Views/<Name>/`. No orphan pages: every GET is reachable via
   nav/tag-helper/redirect (see `UiReachabilityTests`). Use `asp-append-version="true"` on
   `<script>/<link>` to `wwwroot`.
6. **Tests.** Unit tests in the slice's `unit_tests` path (in-memory SQLite). E2E spec in
   `e2e-tests/playwright/tests/<name>.spec.js`, arriving by clicking in-app links (not
   `goto` deep URLs). Exercise each model-validation rule at every binding surface.
7. **Run the verify-shard gate** (below) and keep it green.

## verify-shard gate (same checks CI runs)

```bash
dotnet format VitaTrack.sln --verify-no-changes   # format
dotnet build VitaTrack.sln -c Release             # build
dotnet test VitaTrack.sln -c Release              # arch (12) + unit (~184)
cd e2e-tests/playwright && npx playwright test tests/<name>.spec.js
```

`ShardOwnershipTests` + `FileSizeTests` are inside `dotnet test`. A red gate means stop —
do not widen the change to fix forward; fix the slice.

## Human design-review gate (Phase 7)

If the slice needs an **ADR** or a **slice-boundary** decision (e.g. where a shared
concept lives, whether to introduce `Money` which needs a currency column), STOP and
prompt the developer with the options before writing slice code. The design-review
checklist requiring a named architecture reviewer lives in
`docs/factory/design-review.md`.

## Exemplar files to copy

- `VitaTrack.Core/Features/Dosing/PrescribedDose.cs`, `IPrescribedDoseRepository.cs`,
  `PrescribedDoseRepository.cs`
- `VitaTrack.Core/Features/Dosing/DoseMultiplier.cs`, `DosePeriod.cs`
- `VitaTrack.Core/Features/Dosing/CreateDoseRequest.cs`, `EditDoseRequest.cs`,
  `DoseCommandResult.cs`, `PrescribeDoseHandler.cs`, `AmendDoseHandler.cs`
- `VitaTrack.Web/Controllers/PrescribedDoseController.cs` (thin, DTO-bound)
- `VitaTrack.Tests/Features/Dosing/PrescribedDoseRepositoryTests.cs`
- `e2e-tests/playwright/tests/prescribed-dose.spec.js`

Lessons from the pilot: handler-per-rule held at slice scale (no over-fragmentation);
value objects paid off; no-MediatR (explicit DI handlers) is sufficient.
