# Family (MF) slice conversion — task briefing for the executing agent

> **Purpose:** this is the complete, self-contained brief for the agent session that
> converts the Family Members slice per the factory-v3 rollout backlog. The design
> decisions below are PRE-DECIDED and signed off by the developer (launching a session
> against this doc is the sign-off). The executing agent does mechanical work and keeps
> gates green; it does not invent architecture.
>
> **Target agent class:** cheap (≤ $1/1M tokens, e.g. MiMo-2.6-Flash). Record the ledger
> entry honestly; a stopped-and-asked session with truthful numbers is a better outcome
> than a green one with fabricated numbers. This is the **fifth** ledger entry — the
> ledger ratchet (a missing entry fails `verify-shard`) is active.

## Step 0 — read these, in order, before touching anything

1. `AGENTS.md` (repo root) — house rules, FK delete order, commit conventions
2. `docs/adr/0006-vertical-slice-architecture.md` — the slice model + cross-slice invariant
3. `docs/factory/new-shard.md` — the recipe being followed (incl. hard invariants)
4. `docs/factory/verify-shard.md` — the acceptance gate to run
5. `VitaTrack.Core/Features/Supplements/` — the most recent exemplar slice (note its
   one-record-per-file style); also skim `Features/Dosing/` for the request-DTO pattern
6. `docs/plans/2026-09-20-dosing-slice-pilot.md` lesson #6 — DTO binding swaps `@model`
   with identical field names; `asp-for` bindings need no edits
7. `shards.yaml` + `storymap.yaml` — the ownership index to edit

Branch: create `feature/family-slice` from `feature/factory-v3-vertical-slices`.
Never commit with `--no-verify`.

## Pre-decided design (do not relitigate; escalate if you believe it is wrong)

**Slice:** id `MF` (already exists), name stays "Family Members". Files move from
`VitaTrack.Core/Data` + `VitaTrack.Core/Models` into `VitaTrack.Core/Features/Family/`,
and the controller stops binding the entity from forms (§2.6 / pilot lesson #6).

**File moves** (git mv, then namespace `VitaTrack.Core.Features.Family`):
- `VitaTrack.Core/Data/IFamilyRepository.cs` → `VitaTrack.Core/Features/Family/`
- `VitaTrack.Core/Data/FamilyRepository.cs`  → `VitaTrack.Core/Features/Family/`
- `VitaTrack.Core/Models/FamilyMember.cs`    → `VitaTrack.Core/Features/Family/`

`FamilyMember` is referenced by Dosing (`PrescribedDose` display), Reporting, DbInit
(seed) and views — add `using VitaTrack.Core.Features.Family;` where needed. The
MF repository's cross-slice delete already routes via
`IPrescribedDoseRepository.DeleteByFamilyMemberIdsAsync` (TD-005, closed) — do not touch
that routing; namespace edits only.

**Request DTOs — end entity binding at `FamilyController` (pilot lesson #6):**
- `VitaTrack.Core/Features/Family/CreateFamilyMemberRequest.cs`:

      public class CreateFamilyMemberRequest
      {
          [Required]
          [StringLength(200)]
          public string Name { get; set; } = string.Empty;

          [Required]
          [StringLength(200)]
          public string DisplayName { get; set; } = string.Empty;

          [Url]
          [StringLength(500)]
          public string? AvatarUrl { get; set; }
      }

- `VitaTrack.Core/Features/Family/EditFamilyMemberRequest.cs`: same three fields plus
  `public int Id { get; set; }` with the same annotations.
- `FamilyController.Create`/`Edit` POST actions bind the DTOs and construct
  `FamilyMember` inline (three properties — thin mapping, no handler: these are CRUD
  passthroughs with no rules beyond the annotations; the pilot keeps handlers for
  rule-carriers only). GET actions keep returning the entity.
- `Views/Family/Create.cshtml` and `Views/Family/Edit.cshtml`: swap `@model` to the
  corresponding request DTO (field names identical → `asp-for` untouched). `Index.cshtml`
  keeps `@model` entity. Keep the existing `@using VitaTrack.Core.Models` lines if
  present; `_ViewImports.cshtml` gains `@using VitaTrack.Core.Features.Family`.
- Validation behavior must be identical: the annotations move verbatim, nothing added
  or removed.

**OUT OF SCOPE (explicitly deferred):** no handlers for Create/Edit (pure CRUD
passthrough); no ORDER BY changes to `GetAllAsync` (pre-existing, note as debt
candidate in your report); no avatar upload or new fields; LLM slice; any
`PrescribedDose`/`Reporting` changes beyond namespace usings.

## Execution order (commit per green step; tick as you go)

- [ ] 1. Branch + read docs. Commit nothing yet.
- [ ] 2. git mv the three files; namespace + using updates across the solution
        (incl. `_ViewImports.cshtml`, DbInit, Dosing/Reporting consumers); build + full
        `dotnet test` green. Commit (`refactor:`).
- [ ] 3. Request DTOs + controller binding swap + the two view `@model` swaps. Full unit
        suite + `family-member.spec.js` green. Commit (`feat:`).
- [ ] 4. `shards.yaml` MF path surgery (core paths; controller/views/tests/e2e
        unchanged). Arch tests green (ShardOwnership is the proof). Commit (`docs:`).
- [ ] 5. Ledger entry + full final gate (incl. FULL e2e suite once) + push.

## The gate (run after every step; all must be green)

    dotnet format VitaTrack.sln --verify-no-changes
    dotnet build VitaTrack.sln -c Release
    dotnet test VitaTrack.sln -c Release          # 15 arch + ~210+ unit
    cd e2e-tests/playwright && npx playwright test tests/family-member.spec.js

## Hard rules

- NEVER weaken a guardrail, test, or threshold to make it pass. If
  ShardOwnershipTests/FileSizeTests/StoryMapConsistencyTests fail, fix the slice or
  the manifest — not the test. Editing a test's *assertions* is forbidden; `using`
  line updates after file moves are expected.
- NEVER `git commit --no-verify`. NEVER push to `main`. No PR — push the branch only.
- No hardcoded DB ids in e2e; dynamic assertions only (parallel workers share one DB).
- Keep every complete type under 300 lines (partials count).
- No new abstractions, interfaces, or config beyond the pre-decided list.
- Do NOT run the full e2e suite more than the final once.

## Escalation (STOP and ask the human; record it later as an intervention)

- A guardrail test seems wrong, not just failing.
- A behavior change beyond "identical output, new home" appears necessary (including
  any e2e spec edit or validation-rule change).
- The DTO binding swap breaks a view binding that the pilot's "identical field names"
  lesson says should just work.
- Any decision that would need a new ADR or a different slice boundary.
- You are stuck after two failed attempts at the same fix.

When you stop: state what you were doing, the two attempts, and the exact question.

## Done = all of these

- Gates green (format, build, 15 arch incl. ShardOwnership + StoryMapConsistency +
  CrossSliceSqlTests, full unit suite, `family-member.spec.js` — plus the FULL e2e
  suite once at the end).
- `shards.yaml` updated; no orphan, no double-claim.
- Ledger entry in `docs/factory/shard-metrics.yaml` (fifth entry):

      - id: MF
        name: Family Members
        agent: cheap
        human_interventions: <actual count of stops>
        guardrail_failures: <count of red gate runs before final green>
        fix_commits: <commits after your first "done" claim>
        defects_escaped: 0

  Plus the usage fields documented in `metrics.md` (`agent_model`, `tokens_input`,
  `tokens_output`, `tokens_reasoning`, `tokens_cache_read`, `cost_usd`) extracted from
  your own session — `opencode session list` → `opencode export <sessionID>`, use the
  session-level `info` block; record honest numbers, `0` on free tier is real.
  Derive the counts from your actual session history, do not estimate them.
- Branch pushed. Report: commits, final gate output, interventions (with reasons),
  and anything you noticed but did not touch (goes to the debt register).
