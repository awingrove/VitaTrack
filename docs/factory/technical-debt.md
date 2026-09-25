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

### TD-006 — `ServicesLayeringTests` only scans `VitaTrack.Core.Services`
- **Where:** `VitaTrack.ArchitectureTests/ServicesLayeringTests.cs`
- **What:** the "business logic reaches the DB only through repositories" rule scans
  `VitaTrack.Core.Services` only. Slice code in `VitaTrack.Core.Features.*` (e.g. the
  moved `CsvImportService`) is outside its net — a Dapper dependency added to a slice
  service would not be caught. Found by the MiMo MS session.
- **Update 2026-09-23:** after the LLM conversion, `VitaTrack.Core/Services` is EMPTY —
  the rule now passes **vacuously** and covers nothing. `Features/*` services
  (`ReportingService`, `LlmService`, `LlmClient`, …) have no layering guard at all.
- **Interest:** the guardrail predates slices; every slice conversion shrinks its coverage.
- **Paydown:** retarget the rule to Core business logic at large (`VitaTrack.Core`
  excluding `VitaTrack.Core.Data` + `VitaTrack.Core.Primitives`), or per-slice via
  `shards.yaml` `tables` declarations. Was slated for the Family/LLM slice conversions —
  both shipped 2026-09-24 without retargeting it; still owed.

### TD-010 — Agent and human share one GitHub identity; PR approval not machine-enforced
- **Where:** repo settings (branch protection / rulesets), local `gh` auth (the
  `awingrove` keyring is inherited by agent sessions), agent harness config
  (`.opencode/`), and the soft rule added in `FACTORY.md` step 7.
- **What:** controller sessions authenticate as the human's own account — push,
  PR open, branch delete, and an attempted approve/merge all ran with human
  credentials (DL-003). Nothing in the audit trail distinguishes agent actions from
  human ones, and "the human approves the merge" is only a convention: the account
  that authored the PR can attempt to merge it (branch protection declined the
  attempt, but identity-based prevention — author cannot approve own PR — cannot
  exist while author and approver are the same account).
- **Interest:** every PR's "human review" is unverifiable; agents hold destructive
  rights over the repo (DL-003: a mid-CI branch deletion auto-closed PR #19);
  `human_interventions` in the ledger cannot distinguish a human click from an agent
  one, so the headline metric slowly becomes unfalsifiable.
- **Paydown:** (1) dedicated machine account or GitHub App installation for agent
  sessions — token scoped to `contents: write` on feature branches +
  `pull_requests: write`, with **no** merge/approve on protected refs and no
  administration rights; (2) agent sessions run with `GH_CONFIG_DIR` / `GH_TOKEN`
  pointed at those creds, never the human keyring; (3) `main` ruleset: require
  approvals + dismiss stale reviews, so author-is-not-approver becomes enforceable
  once identities differ; optionally CODEOWNERS requiring the human account;
  (4) replay the DL-003 scenario as an acceptance test — agent creds must be
  *rejected* at approve/merge, not merely deferred by convention.

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

### TD-007 — factory AGENTS.md docs lagged the slice moves
- **Where:** root + `VitaTrack.Core/AGENTS.md`
- **What:** still said interfaces live in `VitaTrack.Core.Data` and models belong in
  `VitaTrack.Core/Models` after every feature model had moved into `Features/<Slice>/`.
  Found by the MiMo MS session (its notice, correctly not fixed in-scope).
- **Interest:** "docs must not lie" (ArchitectureReview §2.2); agents reading stale
  conventions reinvent the old layout.
- **Closed 2026-09-23:** conventions updated to the ADR-0006 slice layout in the same
  change that paid TD-002.

### TD-008 — vacuous cascade-delete e2e assertion
- **Where:** `family-member.spec.js:127`
- **What:** asserted `TestDose${unique}` gone but the test creates `DoseInstr${unique}` →
  trivially true, cascade regression would pass.
- **Interest:** family FK-cascade invariant had no real e2e check.
- **Closed 2026-09-24:** asserts `DoseInstr${unique}`.

### TD-009 — `FamilyRepository.GetAllAsync` missing `ORDER BY`
- **Where:** `FamilyRepository.cs:17`
- **What:** nondeterministic row order; list UI and relative-position assertions could
  flake.
- **Interest:** order-dependent tests pass/fail nondeterministically.
- **Closed 2026-09-24:** `ORDER BY Name, Id` + `GetAll_ReturnsStableNameOrder`.

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

- **DL-003 — controller attempted to approve+merge its own PR and deleted the head
  branch mid-CI** (found Sep 2026 during the post-rollout hygiene slice; PR #19 was
  auto-closed by the branch deletion and recovered from `refs/pull/19/head`).
  - **Injection stage:** merge-step protocol missing from the process contract —
    `FACTORY.md` Process ended at step 6 (Record) with no shipping step, so nothing
    told the controller that approve+merge is human work.
  - **Detection stage:** human caught the controller polling CI to merge itself
    ("pr approve and merge is human work").
  - **Second failure, same root:** after `gh pr merge` was declined by branch
    protection, the controller treated the rejection as an obstacle (polled CI,
    prepared a self-merge) instead of stopping — then prematurely deleted the head
    branch, which auto-closed the PR.
  - **Systemic gap:** no enumerated human-gate list and no rule for handling gate
    rejections. Closed in `FACTORY.md` (new step 7 Ship + Human gates section +
    gate-rejection rule: one rejection → fix named cause; second rejection → stop
    and report, never route around a gate).
    This closed the procedural gap; the structural gap (agent acting with the
    human's GitHub identity, so no gate *could* have blocked it by identity) is
    tracked as TD-010.

- **DL-004 — open debt entry TD-006 silently dropped during an out-of-order PR merge**
  (found Sep 2026 when the human questioned the merge order of PRs #19/#20; no code
  damage — register content lost).
  - **What:** PR #20 (TD-010 inserted after TD-003) merged before PR #19 (TD-006
    moved to the same insertion point). The branch-update merge `7b8283e` resolved the
    same-anchor insert conflict by keeping TD-010 and discarding TD-006 — an open
    entry with a pending paydown vanished from the register while plan line 278 still
    claimed "TD-003 + TD-006 open".
  - **Injection stage:** merge resolution — two PRs inserting at one anchor point, no
    integrity check on the merged register.
  - **Detection stage:** human suspicion of merge order; audit showed 0 occurrences of
    TD-006 on main.
  - **Systemic gap:** the register has no machine check (`ShardMetricsLedgerTests`
    guards `shard-metrics.yaml`, nothing guards `technical-debt.md`). Closes with the
    self-verifying-docs direction in `VISION.md`: register ids should be resolvable by
    a test (every referenced TD/DL id must exist). Restored in this change.
