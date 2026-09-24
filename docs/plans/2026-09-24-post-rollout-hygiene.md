# Post-Rollout Hygiene — Ledger Honesty + Debt Register Follow-ups

**Branch:** `fix/ledger-honesty` (base `c7ec9a5` on `main`).
**Executor:** mimo-2.6-flash-free (cheap). **Controller:** this session.
**Scope:** five findings from a bibliography-alignment review of the factory-v3
rollout session. All edits are pre-decided below — no design judgment required.

## Current-state facts (verified, do not re-derive)

- `docs/factory/shard-metrics.yaml` LLM entry: lines 126–160; fields
  `human_interventions: 0` (line 129), `fix_commits: 0` (line 131); `notes` prose
  contains `zero human` at line 142.
- `docs/factory/metrics.md` definitions: `human_interventions` = "Every stop that
  needed a human: design-review prompts, corrections, unblocks, **review findings
  that forced changes**"; `fix_commits` = "**Commits after the first done claim.**
  Measures verification honesty."
- LLM shard facts: done claim = commit `8bcb1af`; reviewer Important plan-mandated
  finding (SupplementNutrientDto stranded in LlmEnrichment); fix commit `599a112`
  re-homed the DTO to `Features/Nutrients/`. Therefore `human_interventions` and
  `fix_commits` must both be **1**, not 0. `guardrail_failures: 3` and
  `defects_escaped: 0` are correct — leave them.
- `docs/factory/verify-shard.md` — `## Ledger gate` section ends with
  "See `metrics.md`." (line 34).
- `docs/plans/2026-09-21-factory-v3-vertical-slices.md`: line 258
  `Debt candidates recorded: vacuous`; lines 277–279 the "Remaining open items"
  parenthetical containing `TD-008 in \`family-member.spec.js\` vacuous assertion
  candidate` (a phantom reference — TD-008 does not exist in the register yet).
- `docs/factory/technical-debt.md`: open entries = TD-003 only; closed = TD-001,
  TD-002, TD-004, TD-005, TD-006 (updated, closed), TD-007. TD-008/TD-009 absent.
- `e2e-tests/playwright/tests/family-member.spec.js:127`:
  `await expect(page.locator(\`table tbody tr:has-text("TestDose${unique}")\`)).toHaveCount(0);`
  — but the dose created by this test (line 109) carries `DoseInstr${unique}`.
  `TestDose` is never created → the assertion is trivially true forever.
- `VitaTrack.Core/Features/Family/FamilyRepository.cs:17`:
  `const string sql = "SELECT Id, Name, DisplayName, AvatarUrl FROM FamilyMembers";`
  — no `ORDER BY`.
- `VitaTrack.Tests/FamilyRepositoryTests.cs` exists (`SqliteTestBase` pattern,
  `_repo` in `Setup()`), current tests at lines 20, 53, 60, 84.
- Dashboard: `python3 scripts/generate-factory-dashboard.py` writes
  `docs/factory/dashboard.html`; `--check` mode + tests in
  `scripts/test-generate-factory-dashboard.py`.

## Task 1 — Amend LLM ledger to honest values

File: `docs/factory/shard-metrics.yaml` (LLM entry only — no other entry, no
schema/required-field changes).

1. `human_interventions: 0` → `1`.
2. `fix_commits: 0` → `1`.
3. In `notes`: change `Three commits, three red gate runs, zero human`
   to `Three commits, three red gate runs, one human stop (post-review),`
   (keep the rest of that sentence's red-gate narrative intact), and append at
   the end of the notes block:

   ```
   Reviewer adjudication (2026-09-24): approved with one Important plan-mandated
   finding — SupplementNutrientDto, a Nutrients-owned contract bundled in
   LlmResult.cs, was stranded in Features/LlmEnrichment by the move, baking
   Nutrients -> LlmEnrichment coupling into the layout. Controller re-homed it to
   Features/Nutrients/SupplementNutrientDto.cs (commit 599a112, after the done
   claim), correcting the dependency direction to LLM -> Nutrients; claimed in
   shards.yaml under NT. human_interventions and fix_commits corrected from 0 per
   metrics.md definitions (review finding that forced changes; commit after the
   done claim).
   ```

4. Regenerate the dashboard snapshot: `python3 scripts/generate-factory-dashboard.py`;
   verify `python3 scripts/generate-factory-dashboard.py --check` exits 0 and
   `python3 scripts/test-generate-factory-dashboard.py` passes.

Commit: `fix: correct LLM ledger intervention/fix counts per metrics definitions`
(yaml + `docs/factory/dashboard.html` only).

## Task 2 — Amend verify-shard ledger gate (systemic fix)

File: `docs/factory/verify-shard.md`. In `## Ledger gate`, after "See `metrics.md`."
append:

```
Before merging, reconcile the entry against the review outcome:

- A review finding that forced changes ⇒ `human_interventions ≥ 1`.
- Any commit after the executor's first done claim ⇒ `fix_commits ≥ 1`.
- A plan-mandated Important finding (or any finding that conflicts with the
  plan's text) is the human's decision — surface it; do not self-adjudicate
  (design-before-code gate, `BIBLIOGRAPHY.md` glossary).
```

Commit: `docs: amend verify-shard ledger gate — review reconciliation + human decision rule`

## Task 3 — File + close TD-008/TD-009 with the real fixes

1. `e2e-tests/playwright/tests/family-member.spec.js:127`: change `TestDose` to
   `DoseInstr` (the string this test actually creates).
2. `VitaTrack.Core/Features/Family/FamilyRepository.cs:17`: append ` ORDER BY Name, Id`
   to the `GetAllAsync` SQL (deterministic total order: unique `Id` breaks `Name` ties).
3. New unit test in `VitaTrack.Tests/FamilyRepositoryTests.cs`:
   `GetAll_ReturnsStableNameOrder` — add three members with names in
   non-alphabetical insertion order (e.g. `Zulu…`, `Alpha…`, `Mike…`), call
   `GetAllAsync`, assert names come back in alphabetical (`Name`) order. This fails
   without the `ORDER BY` (insertion order preserved by SQLite).
4. `docs/factory/technical-debt.md` — add to **Closed entries** (register style,
   `### TD-008 — …` / `### TD-009 — …`), each with Where / What / Interest /
   Closed:
   - **TD-008 — vacuous cascade-delete e2e assertion.** Where:
     `family-member.spec.js:127`. What: asserted `TestDose${unique}` gone but the
     test creates `DoseInstr${unique}` → trivially true, cascade regression would
     pass. Interest: family FK-cascade invariant had no real e2e check.
     Closed 2026-09-24: asserts `DoseInstr${unique}`.
   - **TD-009 — `FamilyRepository.GetAllAsync` missing `ORDER BY`.** Where:
     `FamilyRepository.cs:17`. What: nondeterministic row order; list UI and
     relative-position assertions could flake. Interest: order-dependent tests
     pass/fail nondeterministically. Closed 2026-09-24: `ORDER BY Name, Id` +
     `GetAll_ReturnsStableNameOrder`.
5. `docs/plans/2026-09-21-factory-v3-vertical-slices.md` cross-refs:
   - Line ~258: after `FamilyRepository.GetAllAsync`.` append
     ` (filed as TD-008/TD-009 — closed in docs/plans/2026-09-24-post-rollout-hygiene.md)`.
   - Lines ~277–279: replace the parenthetical
     `(TD-002 closed; TD-006 open — layering-rule retarget; TD-008 in \`family-member.spec.js\` vacuous assertion candidate)`
     with
     `(TD-003 + TD-006 open; TD-001/002/004/005/007/008/009 closed — see the register)`.
     Keep the surrounding sentence intact.

Commit: `fix: real cascade-delete assertion + stable family list order (TD-008, TD-009)`

## Gates (run before claiming done)

1. `dotnet format VitaTrack.sln --verify-no-changes`
2. `dotnet build VitaTrack.sln -c Release` — 0 errors/0 warnings
3. `dotnet test VitaTrack.sln -c Release` — arch 15/15, unit 211/211 (one new test)
4. `python3 scripts/generate-factory-dashboard.py --check` + `python3 scripts/test-generate-factory-dashboard.py`
5. Full e2e: `cd e2e-tests/playwright && E2E_PORT=5010 npx playwright test` — 77/77
   (ORDER BY touches a shared page; run the full suite, not just family-member)

Each of the three commits is gated by the pre-commit hook (format + arch/unit).

## Out of scope

- Other ledger entries, `guardrail_failures`, token/cost fields, schema changes.
- `ServicesLayeringTests` retarget (TD-006 stays open).
- Any AGENTS.md / storymap / shards.yaml edits.
- Debug artifacts of any kind.

## Escalation

Red gate you cannot fix without breaking an out-of-scope rule, or a current-state
fact above that turns out wrong → stop, log the deviation in the task report, do
not improvise around it.
