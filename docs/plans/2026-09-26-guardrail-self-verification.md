# Guardrail self-verification — sentinels, loud loaders, register integrity

**Branch:** `test/guardrail-self-verification` (base `32a0c75` on `main`).
**Executor:** space-bunny-free. **Controller:** this session.
**Goal:** make the arch guardrails fail when they stop guarding. Three mechanisms,
in descending order of defect-caught-per-line: **sentinels** (assert a known thing is
under the net), **loud loaders** (a missing or emptied manifest key is an error, not a
silent skip), and **register integrity** (the debt register gets the machine check
DL-004 needed).

## Design decisions (pre-decided by the human; do not re-open)

1. **Wildcard `core:` → error, not expand.** `technical-debt.md:48-49` offered both.
   Error is a strict subset of expand-later: a `core:` glob errors today and can be
   implemented later without anyone having relied on the permissive behavior. Expanding
   `VitaTrack.Core/**` would pull every slice's SQL into one slice's `tables` and flood
   the rule with false positives, which trains people to ignore it. **Error.**
2. **A closed entry must carry `Where`, `What`, and `Closed <date>`, but NOT `Interest`.**
   9 of 10 closed entries have `Interest`; `TD-002` (`:172-179`) does not. Requiring it
   would mean rewriting a closed historical entry. The `Closed` label's date **varies**,
   so match `^\- \*\*Closed \d{4}-\d{2}-\d{2}:\*\*` — a fixed-string `"**Closed:**"` check
   fails today. An open entry must carry `Where`, `What`, `Interest`, `Paydown`, and must
   NOT carry a `Closed` bullet.
3. **Sentinels are a derived invariant, not hard-coded names, wherever possible.** A
   named type rots when the type is legitimately split; a derived invariant cannot. Use
   a derived invariant as the primary sentinel and, only where a derived form is
   impossible, name one file.
4. **Split register integrity from register shape** into two test methods, so a shape
   failure never masquerades as a lost-entry failure.
5. **The `EcosystemGuardrailTests` empty `catch` IS a live false negative, and Dispatch 3
   fixes it.** See the correction below — do not describe it as structural-only.

## Current-state facts (verified on this branch — do not re-derive)

- `CrossSliceSqlTests.cs`: entry `:38-39`; `FindRepoRoot` `:154-161`; `LoadSlices`
  `:108-138`; per-slice `id` assert `:121-122`; missing `tables` → **error** `:124-127`;
  `LoadCore` `:140-147`; per-slice loop `:44-61`. Confirmed silent skips:
  - `:50` `if (!File.Exists(fullPath)) continue;` — a `core:` path that no longer exists
  - `:142` `return new()` when the `core:` key is **absent** entirely
  - `:145` `.Where(p => !p.Contains('*'))` — a wildcard pattern is dropped, no error
  Its second test `Sql_Extractors_Parse_Exemplar_Literals_And_Ignore_Prose` (`:79-106`)
  reads a **hard-coded path**, never the manifest, so it pins the extractor, not the wiring.
- `ShardOwnershipTests.cs`: entry `:24-25`; `LoadShards` `:66-108`; missing artifact key →
  `continue` `:89` (silent); zero-match **→ error** `:94-95` (slice artifacts only);
  allowlist load `:103-105` (**no zero-check**); `Expand` `:172-199` returns an empty
  list silently on all three branches; `:113` `if (!File.Exists(path)) return;` disables
  the whole slice-id cross-check when `storymap.yaml` is absent; `:133-138` already pins
  the slice-id set bidirectionally, so **deleting a whole slice entry is caught** —
  emptying a slice's file lists while keeping its id is caught by nothing.
- `EcosystemGuardrailTests.cs:24-45` — the `try` at `:36` wraps both `Assembly.Load`
  (`:38`) **and** the recursion (`:39`), so an `AssertFailedException` raised at `:32-34`
  in any recursee unwinds into the empty `catch` at `:41-43`.
- The duplicate root-locators are:
  `FindRepoRoot` ×5 identical (`StoryMapConsistencyTests.cs:197`, `UiReachabilityTests.cs:149`,
  `CrossSliceSqlTests.cs:154`, `ShardMetricsLedgerTests.cs:109`, `ShardOwnershipTests.cs:208`)
  and `FindSolutionRoot` ×2 identical (`ControllerHygieneTests.cs:36`, `FileSizeTests.cs:107`).
  Only one `.sln` exists, so unifying on the exact `VitaTrack.sln` match is behaviour-identical.
  A shared helper retires the 4 `dir!` null-forgivings at `CrossSliceSqlTests.cs:160`,
  `ShardMetricsLedgerTests.cs:115`, `ShardOwnershipTests.cs:214`, `StoryMapConsistencyTests.cs:204`,
  `UiReachabilityTests.cs:156`.
- Register format, verbatim from `technical-debt.md`:
  - section headings, required and **in order**: `## Open entries`, `## Closed entries`,
    `## Defect log` (the Python generator validates this at `:708`, `:719-727`);
  - entries: `### TD-013 — <title>` then flat `- **Label:** text` bullets at column 0,
    continuation lines indented exactly 2 spaces, never a list marker;
  - defect log uses a different shape: `- **DL-001 — <title>** (found …)` with sub-bullets
    indented 2 spaces;
  - the generator's regexes are the de-facto spec: `DEBT_ENTRY_RE` `:79`,
    `INTEREST_BULLET_RE` `:80`, `DEFECT_ENTRY_RE` `:81`.
- Ids are referenced from **`.cs` files too** — `CrossSliceSqlTests.cs:15` carries `TD-005`
  in an XML doc comment. A `.md`-only scan misses it.
- `docs/factory/dashboard.html` is **generated** (`:14` "Generated", `:16` names the command)
  and is a committed artifact, freshness-gated by CI (`ci.yml:41-47`). Its ids are *derived*
  from the register, so scanning it proves the generator ran, not that the register is sound.
  **Exclude it**, exactly as no arch test references `dashboard` today.
- Referenced ids currently all resolve: the only defect present was the `TD-011` duplicate,
  **already fixed** in `55d1aaf` (the `DailyDose` entry is now `TD-017`).

## Task sequence — 3 dispatches, 1 branch

Order is load-bearing: **D1 → D2 → D3**. D1 adds the shared helper the other two need.
The pre-commit hook runs format + the whole arch suite on every commit, so every commit
must leave arch green.

### Dispatch 1 — loud loaders (D1, 4 files, 2 commits)

Turn each silent skip into an error. Every one of these is *wrong is loud, deleted is
silent* — the same class the entry describes.

1. **Commit 1** — `test: fail loudly on a missing or unresolvable core: declaration`:
   - extract `RepoLocator.Root()` into a new `VitaTrack.ArchitectureTests/RepoLocator.cs`
     (walks up from `AppContext.BaseDirectory` until `VitaTrack.sln`; returns non-nullable,
     asserting internally) and delete all **7** private copies, rewriting the 8 call sites.
     Keep `Assert.IsNotNull` calls that become redundant — removing them is a separate change.
   - `CrossSliceSqlTests.LoadCore`: a **missing** `core:` key is an error, not `new()`
     (`:142`). `SHELL` declares `core: []` — present-but-empty, verified at `shards.yaml:218`
     — so distinguish *absent key* from *present-but-empty list*, and do not error on the latter.
   - `CrossSliceSqlTests`: a `core:` path that does not exist is an error (`:50`), not `continue`.
   - `CrossSliceSqlTests.LoadCore`: a wildcard pattern is an **error** (decision 1), replacing
     the silent `.Where(p => !p.Contains('*'))` at `:145`.
2. **Commit 2** — `test: fail loudly on an unresolvable manifest declaration`:
   - `ShardOwnershipTests`: a **missing** artifact key (`:89`) is an error for the keys that
     are mandatory, mirroring the existing missing-`tables` error in `CrossSliceSqlTests`.
     `unit_tests`/`e2e_specs` may legitimately be `[]` (`:220` does this) — same
     absent-vs-empty distinction.
   - `ShardOwnershipTests`: the **allowlist** gets the zero-match error that slice artifacts
     already have (`:103-105` vs `:94-95`).
   - `ShardOwnershipTests:113`: a missing `storymap.yaml` is an **error**, not a silent
     `return` that disables the cross-check.

### Dispatch 2 — sentinels (D2, 3 files, 1 commit)

3. **Commit** — `test: derived sentinels for the two filesystem-scoped guardrails`:
   - `CrossSliceSqlTests`: assert **every slice with non-empty `tables:` has non-empty `core:`**.
     Derived, no hard-coded name. Vacuously satisfied by `RP`/`LLM` (`tables: []`) and correct
     for `SHELL`. This is the sentinel that would have caught TD-006's shape.
   - `ShardOwnershipTests`: assert the **total claimed-file count** is above a floor, AND that
     every slice declaring files claims at least one. A count floor alone is gameable (one
     large slice emptied while a small one absorbs) — the per-slice clause is what makes it
     strict. Pick the floor from the real number today and say what you picked and why.
   - Do **not** add a NetArchTest-style named-type sentinel here; these tests are
     filesystem-scoped and have no type selection to pin.

### Dispatch 3 — register integrity + the swallowed assert (D3, 2 files, 2 commits)

4. **Commit 1** — `test: technical-debt register integrity (TD-014)`:
   new `VitaTrack.ArchitectureTests/TechnicalDebtRegisterTests.cs` with **two** test methods
   (decision 4):
   - `Every_Referenced_Debt_Or_Defect_Id_Resolves` — scan `.md` **and** `.cs` files
     (excluding `docs/factory/dashboard.html` as generated, `node_modules`, `bin`, `obj`, and
     `docs/factory/technical-debt.md`'s own definition lines); every `TD-\d+`/`DL-\d+`
     referenced must exist as a definition. A definition file must be excluded or every id
     trivially resolves to itself and the check is vacuous — the same trap as TD-006.
   - `Register_Ids_Are_Unique` — no `TD-`/`DL-` id defined twice. This is the check that
     would have caught the `TD-011` duplicate fixed in `55d1aaf`.
   - Also add a grep-based test for **TD-015**: no live file (excluding plans, `.superpowers/`,
     and the register's own `Where:` citations) may reference the retired
     `VitaTrack.Core.Services` / `VitaTrack.Core/Services`. Four references were removed in
     `f775290`; this stops them coming back.
5. **Commit 2** — `fix: EcosystemGuardrailTests cannot swallow its own assertion`:
   refactor `AssertNoTransitiveDependency` to **collect** violations into a `List<string>` and
   assert once at the top, so an assertion can never be swallowed by construction. Then pin the
   behaviour with a two-line test: the collector is **non-empty** for a banned name reached
   through the graph, and **empty** for a name no assembly has.

## Deliberately NOT claimed / corrected

- **CORRECTION (2026-09-26, controller): the empty `catch` IS a live false negative.** An
  earlier version of this briefing claimed otherwise, on the evidence of a probe rooted at the
  **arch-test assembly** — which does carry `Dapper` as a *direct* reference, so the depth-0
  assert fires. That conclusion was generalized from one of three roots and does not transfer.
  The two roots the shipped tests actually use are `VitaTrack.Core` (`:15`) and
  `VitaTrack.Web` (`:21`). **`VitaTrack.Web` has no direct `Dapper` reference**, so
  `Web → Core → Dapper` sits at depth 1, where the recursee's `AssertFailedException` unwinds
  into the empty `catch` and is discarded. Controller-verified output:

      direct-dapper=False  refs=35
      shipped walker: RETURNED NORMALLY   (violation swallowed)
      collector:         1 violation — "Assembly VitaTrack.Core references banned Dapper"

  So the rule named "transitive" is defeated by its own recursion on the one root where the
  transitive half matters. Dispatch 3 fixes it and the commit message must say so.
- **No per-type span rewrite in `FileSizeTests` (TD-016).** A correct implementation needs a
  brace-matching scanner that skips strings, verbatim and raw string literals (`"""` is used
  in `CsvImportServiceTests.cs:20-24`), char literals, comments, and regex literals, and
  handles primary-constructor parameter lists (`NutrientAggregationVisitor.cs:15`). That is a
  separate change with its own review. Record the phantom-comment keys found
  (`VitaTrack.Tests.it` from the words "class it" in a doc comment) as a new register entry.
- **No `selected >= 0`-style sentinel on `EcosystemGuardrailTests`** beyond commit 2 of D3.

## Out of scope

- `docs/factory/dashboard.html` content changes beyond regeneration if the register changes.
- `Scalar(YamlMappingNode, string)` extraction (duplicated 4×; a 5th copy is acceptable here —
  note it as a register entry rather than refactoring in this change).
- The `StoryMapConsistencyTests.cs:88-89` skip (a story with no `tests:` key).
- `ShardOwnershipTests.cs:156` and `:165` (scan-root missing, excluded dirs) — already backstopped.
- Renumbering any other register id.

## Gates (run before claiming done)

1. `dotnet format VitaTrack.sln --verify-no-changes`
2. `dotnet build VitaTrack.sln -c Release`
3. `dotnet test VitaTrack.sln` — baseline on this branch: **arch 18 / unit 234**. D1 makes some
   skips fatal, so report the arch count change and account for every test added.
4. `python3 scripts/test-generate-factory-dashboard.py` — all green
5. `python3 scripts/generate-factory-dashboard.py --check` — exit 0 (only if the register changed)
6. `./test-e2e.sh` — expected green with no e2e change; run it anyway, because D1 can turn a
   silently-skipped manifest path into a hard failure and I want that observed, not assumed.

## Escalation

Stop, leave the tree clean, and make the final message the escalation if:

- Making a skip fatal turns out to break a **current** declaration — i.e. the repo has a
  manifest entry that is already wrong today and only survived because it was silent. That
  is a finding, not an obstacle: report the exact entry and its intended value, and do not
  paper over it by re-adding a `continue`.
- The `RepoLocator` extraction turns out to need a signature or behavior change beyond
  returning a non-nullable root.
- A register test fails on a **dangling** id reference. Report the file and line. Do not
  "fix" it by deleting the reference — find out which side is wrong.
- The `every slice with non-empty tables: has non-empty core:` invariant does not hold for
  some slice today. Report which, and whether it is a manifest error or the invariant being wrong.
- You believe a current-state fact above is wrong. Verify against the code and report.
