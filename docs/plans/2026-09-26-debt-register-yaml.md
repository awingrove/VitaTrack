# Debt register to machine-checked YAML — TD-014 / DL-004 systemic half

**Branch:** `refactor/debt-register-yaml` (base `main`).
**Executor:** one cheap-model session (`space-bunny-free` or `mimo-flash`), three dispatches.
**Controller:** this session.

**Goal:** replace the prose register `docs/factory/technical-debt.md` with structured
`docs/factory/technical-debt.yaml`, guarded by a new `TechnicalDebtRegisterTests`. Closes
**TD-014** and **DL-004's unresolved half** (an open entry silently dropped by a merge
conflict — nothing machine-checked the file).

**No ADR, no slice boundary, no schema change, no product code.** The register is factory
documentation; nothing in `VitaTrack.Core` / `Web` / `Tests` changes. No
`shards.yaml` entry is required (see *Guards that stay green by construction*).

**No generated Markdown view.** The `.md` is deleted, not regenerated from the YAML. A
generated view is a second artifact that can drift from its source, which is the exact
failure class this change exists to remove.

---

## Current-state facts (verified against the working tree — do not re-derive)

- `docs/factory/technical-debt.md` — **303 lines**, 7 open entries
  (TD-017, TD-012, TD-013, TD-014, TD-015, TD-016, TD-010), **10** closed
  (TD-001…TD-010 plus the closed TD-011), 4 defects (DL-001…DL-004). Section headings,
  in file order: `## Open entries` (`:7`), `## Closed entries` (`:111`), `## Defect log`
  (`:235`).

  > **Correction (2026-09-26, controller).** The original draft of this plan said **11 closed**
  > and gated on `7 11 4`. Both are wrong. `TD-011` is a **duplicate id on `main`**: it names
  > both the open `DailyDose` entry and the closed `Dosage`-retyping entry, introduced together
  > in `f775290`. A third counting method also disagrees: `## Open entries` + `## Closed entries`
  > + `## Defect log` = 7 + 11 + 4 = 22 headings, but there are only **21** distinct entries,
  > because the id is reused. The renumber (`DailyDose` → `TD-017`, plus the matching `AGENTS.md`
  > reference) is commit `55d1aaf` on `test/guardrail-self-verification` and is **not yet on
  > `main`**. It must be cherry-picked onto this branch before Dispatch 1, or the YAML will
  > faithfully migrate a register containing a duplicate id — and Part B's rule 1 (id uniqueness)
  > will then go red on a defect this migration should have carried, not created.
- **Open entries** are `### TD-NNN — title` (`:79` `DEBT_ENTRY_RE`) plus four bullets each:
  `**Where:**`, `**What:**`, `**Interest:**`, `**Paydown:**`. Only `**Interest:**` is parsed
  today (`:80` `INTEREST_BULLET_RE`).
- **Closed entries** are `### TD-NNN — title` plus `**Where:**`/`**What:**`/`**Interest:**`
  and a final `**Closed YYYY-MM-DD:**` bullet. `closed_count` is computed by counting
  `### TD-` lines (`:794`) — no per-entry parse.
- **Defects** are `- **DL-NNN — title** (found <prose>)` with indented sub-bullets
  `**Injection stage:**`, `**Detection stage:**`, `**Systemic gap:**`. DL-002 additionally has
  `**Defect a (step ordering):**` / `**Defect b (wrong current-state claim):**` (`:254-260`);
  DL-003 has `**Second failure, same root:**` (`:276`). Parsed by `:81` `DEFECT_ENTRY_RE`,
  which strips a trailing `**` and joins wrapped title lines (`:771-779`).
- `scripts/generate-factory-dashboard.py` — `_load_debt` at **`:784-796`**;
  `_split_debt_sections` `:711-732`; `_parse_open_debt_entries` `:735-759`;
  `_parse_defect_entries` `:762-781`; regexes at **`:79-81`**;
  `DEBT_SECTION_HEADINGS` `:708`. Dataclasses `DebtEntry` (`:33`, fields `id`/`title`/`interest`),
  `DefectEntry` (`:40`, `id`/`title`), `DebtRegister` (`:46`, `open_entries`/`closed_count`/`defects`)
  — **keep all three unchanged**. Consumers: `:457` (defect count card), `:514` and `:536`
  (links to `technical-debt.md`). `import yaml` already at `:14`; `PyYAML 6.0.1` present.
- `scripts/test-generate-factory-dashboard.py` — `make_valid_root` **`:64-80`** writes a
  `technical-debt.md` fixture at `:74-78`; `_write_debt` **`:97-98`**;
  `_debt_document` **`:101-109`**. Tests that write `.md` fixtures: `:627`, `:647`, `:668`,
  `:677`, `:699`, `:750`, `:782`. Two of those test *Markdown-parser* shape and delete with
  the parser: **`test_defect_entries_strip_markers_and_trailing_parenthetical` (`:677`)** and
  **`test_wrapped_defect_title_is_joined_from_continuation_line` (`:699`)**.
- `VitaTrack.ArchitectureTests/` — **12 test classes + `RepoLocator.cs`**, 1341 lines total.
  `RepoLocator.Root()` (30 lines) is the shared repo-root walk; **use it**, do not copy
  `FindRepoRoot`. `ShardMetricsLedgerTests.cs` (108 lines) is the template to follow.
  `YamlDotNet 16.3.0` is already a `PackageReference` — no csproj change.
- Baseline green: `dotnet test VitaTrack.sln -c Release` → **arch 18, unit 234, 0 failed**
  (measured 2026-09-26 on `refactor/debt-register-yaml` at `74826d8`).
  > **Correction (2026-09-26, controller).** The original draft said **unit 218**, measured
  > 2026-09-25 on the dosage branch. The dosage work since landed (#24) and raised the unit
  > count to **234**. Part B adds one test class, so arch goes 18 → 19 (or 20 with the
  > negative-path test as a second method). Part C's dashboard-test edits change the *Python*
  > suite, not the C# counts.
- `ShardOwnershipTests` scanned roots are `VitaTrack.Web/Controllers`, `VitaTrack.Core`,
  `VitaTrack.Web/Views`, `VitaTrack.Web/wwwroot/js`, `VitaTrack.Tests`
  (`ShardOwnershipTests.cs:145-149`) → **a new file under `VitaTrack.ArchitectureTests/`
  needs no manifest change**, and deleting `docs/factory/technical-debt.md` is not a
  scanned path at all.
- TD/DL ids referenced anywhere outside the register: `TD-001`…`TD-017`, `DL-001`…`DL-004`
  (verified by repo-wide grep). All resolve to existing entries today.
- Prose contains `: ` (colon-space) in nearly every field — `**Interest:**`, `Closed
  2026-09-25:`, `**Defect a (step ordering):**`. In a plain YAML scalar that is a **parse
  error**, so every prose field **must** use a folded block scalar `>-`.

---

## Schema

```yaml
# docs/factory/technical-debt.yaml
# Technical debt register. Machine-checked by
# VitaTrack.ArchitectureTests/TechnicalDebtRegisterTests.cs — an entry that loses
# a field, duplicates an id, or points at a moved file is a red build.
# Prose fields use folded block scalars (>-): the text contains ": " throughout,
# which is a parse error in a plain scalar. Indentation is significant — a
# continuation line that loses its two-space indent silently ends the scalar.

open:
  - id: TD-017
    title: >-
      `Supplements.DailyDose` accepts free text no dosage vocabulary can express
    where: >-
      `VitaTrack.Core/Features/Supplements/Supplement.cs:19`,
      `VitaTrack.Web/Views/Supplement/Create.cshtml` (`input#DailyDose`).
    what: >-
      …
    interest: >-
      …
    paydown: >-
      …

closed:
  - id: TD-011
    title: >-
      `SupplementNutrient.Dosage` should become a `Dosage` value object
    closed: 2026-09-25
    note: >-
      …

defects:
  - id: DL-001
    title: >-
      `Money +` silently kept the left operand's currency
    found: 2026-09-24
    injection_stage: >-
      …
    detection_stage: >-
      …
    systemic_gap: >-
      …
    closed_in: new-shard.md
```

### Field rules

| Collection | Required | Optional | Forbidden |
|---|---|---|---|
| `open` | `id`, `title`, `where`, `what`, `interest`, `paydown` | — | `closed`, `note` |
| `closed` | `id`, `title`, `closed` (real date), `note` | `where`, `what`, `interest` | — |
| `defects` | `id`, `title`, `found` (real date), `injection_stage`, `detection_stage`, `systemic_gap` | `closed_in` | — |

**The open/closed asymmetry is intentional.** An open entry states its interest rate because
it drives prioritization; a closed entry records what shipped. The test enforces which side
requires what — do not unify the shapes.

**`closed_in` / `found` format.** `found` is an ISO date (`2026-09-24`). The current
prose says "found Sep 2026"; narrow it to the date the entry is filed and keep the
month-precision prose inside `injection_stage` or `note` if it matters. DL-001 through
DL-004 all get `found` from their existing prose; **do not invent precision the source
does not have** — `2026-09-24` is what DL-001's own entry says ("found Sep 2026 in branch
review; fixed same day", and the closed-entry date for the DL-001 fix is 2026-09-24).

---

## Part A — the YAML register (new file, no behavior change elsewhere)

**A1. Write `docs/factory/technical-debt.yaml`** with all 7 open + **10** closed + 4 defect
entries, prose migrated **verbatim** from the `.md`. Field mapping:

- `### TD-NNN — title` → `id` + `title`
- `- **Where:** …` → `where` (open and closed)
- `- **What:** …` → `what` (open and closed)
- `- **Interest:** …` → `interest` (open and closed)
- `- **Paydown:** …` → `paydown` (open only)
- `- **Closed YYYY-MM-DD:** …` → `closed: YYYY-MM-DD` + `note` (the whole bullet body)
- `- **DL-NNN — title** (found <prose>)` → `id` + `title` + `found`; the `(found …)`
  parenthetical is dropped **only** if its content is preserved in `injection_stage`;
  otherwise fold it into `injection_stage` verbatim.
- Indented `**Injection stage:**` / `**Detection stage:**` / `**Systemic gap:**` → same-named
  fields.
- DL-002's `**Defect a (step ordering):**` and `**Defect b (wrong current-state claim):**`
  are **not** separate fields. Prepend each to `injection_stage` with its own label
  preserved (`Defect a (step ordering): …`), so nothing is lost. Same for DL-003's
  `**Second failure, same root:**` → append to `detection_stage`.
- Add `closed_in` to each defect from the "Closed in …" clause of its `systemic_gap`
  (DL-001 → `new-shard.md`, DL-002 → `design-review.md`, DL-003 → `FACTORY.md`, DL-004 → the
  TD-014 register work). If a defect's `systemic_gap` names no file, omit `closed_in`.

**A2. Wrap the header comment** in the same block-scalar discipline, and state the
field rules above so a future agent adding an entry reads the schema at the point of
edit — not in a deleted file.

**A3. Do not delete `technical-debt.md` in this dispatch.** Part C deletes it, after
`_load_debt` no longer reads it. Deleting earlier makes the dashboard generator red with
no signal about why. (This is the DL-002 defect-a lesson: a step's file change and the
thing gating it land together — here, the generator and the delete.)

**Gate A:** `python3 -c "import yaml,sys; d=yaml.safe_load(open('docs/factory/technical-debt.yaml')); print(len(d['open']), len(d['closed']), len(d['defects']))"`
must print **`7 10 4`** (not `7 11 4` — see the correction at the top). Also assert id
uniqueness before committing:
`python3 -c "import yaml,collections; d=yaml.safe_load(open('docs/factory/technical-debt.yaml')); ids=[e['id'] for k in ('open','closed','defects') for e in d[k]]; dupes=[i for i,c in collections.Counter(ids).items() if c>1]; print('dupes:', dupes or 'NONE')"`
must print `dupes: NONE`. Then diff the migrated prose against the `.md` **by eye** — the block
scalar indentation is not machine-checkable, and a continuation line that lost its indent
silently truncates a field. This review step is mandatory, not optional.

---

## Part B — `TechnicalDebtRegisterTests` (new, ~130 lines)

New file `VitaTrack.ArchitectureTests/TechnicalDebtRegisterTests.cs`. Follow
`ShardMetricsLedgerTests.cs` exactly: `[TestClass]`, `RepoLocator.Root()`, `YamlStream`,
`errors` list accumulated then asserted once, `Scalar(node, key)` helper.

**Five rules, all in one `[TestMethod]`** (one failure list, like the ledger test):

1. **ID uniqueness + format, per collection.** `TD-\d{3}` / `DL-\d{3}`, case-sensitive.
   A duplicate within a collection is an error. **This is the DL-004 detector** — the
   merge conflict produced two entries competing for one id, and a lost entry is caught by
   the uniqueness check failing against the other branch's copy.
2. **No id in both `open` and `closed`.** A paydown that forgets to move the entry goes red.
3. **Required fields present and non-empty**, per the table above. Empty folded scalar
   (`>-` with nothing after it) yields `""` — treat as missing, same as the ledger test's
   `string.IsNullOrEmpty` check.
4. **No forbidden field on the wrong side** (`closed`/`note` on an `open` entry).
5. **`where` path resolution, path-shaped values only.** Extract backtick-delimited spans
   from the `where` scalar. For each span whose text starts with a known project root
   (`VitaTrack.Web/`, `VitaTrack.Core/`, `VitaTrack.Tests/`,
   `VitaTrack.ArchitectureTests/`, `scripts/`, `e2e-tests/`, `docs/`, `.github/`,
   `.opencode/`, `shards.yaml`, `storymap.yaml`), **strip a trailing `:NN` line suffix**,
   then require the file to exist. Anything else is prose and is not validated — TD-010's
   `where` is `"repo settings (branch protection / rulesets), local `gh` auth…, and the soft
   rule added in `FACTORY.md` step 7"`, which is not a path and must not fail.
   Also strip a trailing `**` (closed entries render as `` `path` `` with no marker, but
   be tolerant) and skip spans that are clearly not paths (contain a space before the first
   `/`, or end with `/**`).

**Deliberately NOT asserted:** that ids are contiguous; that the ledger and the register
cross-reference. The register ids are prose-referenced from 20+ locations; asserting those
references resolve is the **self-verifying-docs meta-test** (a separate change, recorded as
TD-018 below), not this one. Do not widen scope to it.

**Negative-path proof.** The factory's own rule (`docs/plans/2026-09-25-dosage-vo-and-layering-guard.md`
Part B3): *a rule never observed red is indistinguishable from a rule that cannot go red.*
`Fakes/` already holds `SneakyDapperService` (added by the TD-006 paydown). **Do not** add
a fake YAML file to `Fakes/` — instead, add one test that asserts the *validator helper*
rejects a deliberately malformed in-memory `YamlMappingNode` (duplicate id, missing
`paydown`, unresolvable `where`). Reuse the same private validation methods the real test
calls, so the negative case exercises the shipped code path, not a copy.

**Gate B:** `dotnet test VitaTrack.ArchitectureTests -c Release` green, **19 tests** (18 + 1
new class's single method — if the negative-path test is a second method, 20). Report the
actual count.

---

## Part C — dashboard generator reads YAML

**Order is load-bearing: A → B → C.** C rewrites `_load_debt`; B's test reads the YAML
directly. Running C first makes the dashboard red against a register nothing validates yet.

**C1. Rewrite `_load_debt` (`:784-796`)** to `yaml.safe_load` the new file and build the
same `DebtRegister`:

```python
def _load_debt(root: Path) -> DebtRegister:
    path = root / "docs/factory/technical-debt.yaml"
    if not path.is_file():
        raise DashboardError(f"{path}: file not found")
    data = yaml.safe_load(path.read_text(encoding="utf-8")) or {}
    return DebtRegister(
        open_entries=[
            DebtEntry(str(e["id"]), str(e["title"]), str(e.get("interest") or ""))
            for e in data.get("open") or []
        ],
        closed_count=len(data.get("closed") or []),
        defects=[
            DefectEntry(str(d["id"]), str(d["title"]))
            for d in data.get("defects") or []
        ],
    )
```

Note `closed_count` changes from "count `### TD-` lines" to `len(closed)` — same value
(**10**) by construction, and now derived rather than pattern-matched. A malformed entry
should raise `DashboardError` with the id, not a bare `KeyError`.

**C2. Delete** `_split_debt_sections` (`:711-732`), `_parse_open_debt_entries` (`:735-759`),
`_parse_defect_entries` (`:762-781`), `DEBT_SECTION_HEADINGS` (`:708`), and the three
regexes `DEBT_ENTRY_RE` / `INTEREST_BULLET_RE` / `DEFECT_ENTRY_RE` (`:79-81`). Keep
`DebtEntry` / `DefectEntry` / `DebtRegister` (`:33-49`) — the render path at `:353-355`
and `:404-414` is unchanged.

**C3. Repoint the two links** at `:514` and `:536` from `technical-debt.md` to
`technical-debt.yaml`.

**C4. Delete `docs/factory/technical-debt.md`** in this dispatch (A3 deferred it here).

**C5. Update `scripts/test-generate-factory-dashboard.py`:**
- `make_valid_root` `:74-78` → write a minimal valid `.yaml` (`open: []`, `closed: []`,
  `defects: []`).
- `_write_debt` `:97-98` → write `.yaml`; `_debt_document` `:101-109` → build YAML from
  the same three collections.
- Fixture-writing tests at `:627`, `:647`, `:668`, `:750`, `:782` → YAML fixtures.
- **Delete** `test_defect_entries_strip_markers_and_trailing_parenthetical` (`:677`) and
  `test_wrapped_defect_title_is_joined_from_continuation_line` (`:699`) — they test
  Markdown-parsing behavior that no longer exists. Their coverage is subsumed: titles now
  come from a YAML field with no `**` markers to strip and no wrapping to join.
- **Add** three tests: (a) an `open` entry's `interest` reaches the rendered row; (b)
  `closed_count` equals `len(closed)`; (c) a malformed entry (missing `title`) raises
  `DashboardError` naming the id.
- The HTML-escaping test at `:859` keeps working unchanged — it passes a
  `DefectEntry` directly, so it never touches the parser.

**C6. Regenerate** `docs/factory/dashboard.html`:
`python3 scripts/generate-factory-dashboard.py`. **The debt section must render the same
7 open rows, **10** closed, 4 defects as before.** A different count means a migration dropped
an entry — that is a **finding to report**, not something to fix by editing the YAML to
match the old output.

**Gate C:** `python3 scripts/test-generate-factory-dashboard.py` all green;
`python3 scripts/generate-factory-dashboard.py --check` exit 0; `git diff docs/factory/dashboard.html`
shows the debt counts unchanged.

---

## Part D — register edits and docs (controller, not the executor)

Same change, per the post-mortem rule in `AGENTS.md`.

- **`docs/factory/technical-debt.yaml` — move TD-014 to `closed`:**
  - `closed: 2026-09-26`
  - `note:` — state what shipped: the YAML register, the five-rule
    `TechnicalDebtRegisterTests` (including the negative-path proof), the dashboard
    rewire, and that this closes DL-004's systemic half. Record the **accepted
    limitations**: prose references to TD/DL ids in other documents are *not* resolved by
    a test (that is TD-018), and `where` is validated only for path-shaped backtick spans,
    so a prose `where` can still drift. So no later agent reads them as oversights.
- **Add TD-018** (open) — the follow-on this change creates:
  `where`: `docs/factory/technical-debt.yaml`, `VitaTrack.ArchitectureTests/TechnicalDebtRegisterTests.cs`
  · `what`: the register is machine-checked, but nothing verifies that prose *elsewhere*
  still references real ids — the drift class TD-015 and the self-verifying-docs theme in
  `VISION.md:26-44` are unaddressed · `interest`: a deleted or renumbered id in a plan,
  briefing, or `AGENTS.md` reads as a live reference · `paydown`: extend the
  "enforced by" meta-test (TD-014's original stated scope) to resolve every `TD-\d+` /
  `DL-\d+` reference in `docs/`, root `AGENTS.md`, and `docs/factory/*.md` against the
  register. Do **not** fold this into TD-014's close — it is a distinct change with its
  own design questions (which files are in scope; what a reference inside a *closed*
  entry's prose means).
  Ids continue from **TD-017**, so this is the register's only new entry. Do not invent a
  second id for the same follow-on.
- **Root `AGENTS.md`** — the AI Workflow Directives / post-mortem rule and any pointer to
  `docs/factory/technical-debt.md` repoint to `technical-debt.yaml`, and state the field
  schema in one sentence: *an open entry carries `id`/`title`/`where`/`what`/`interest`/`paydown`;
  a closed entry carries `id`/`title`/`closed`/`note`; a defect carries
  `id`/`title`/`found`/`injection_stage`/`detection_stage`/`systemic_gap`. Prose fields use
  folded `>-` scalars; `TechnicalDebtRegisterTests` fails the build on a missing field,
  duplicate id, or a `where` path that no longer resolves.*
- **`FACTORY.md`** — the Quality management bullet naming
  `docs/factory/technical-debt.md` as the technical-debt register repoints to the YAML and
  names the guarding test alongside the ledger's.
- **`docs/factory/metrics.md`** — no metric fields change; add a one-line note that the
  debt register is now guarded by `TechnicalDebtRegisterTests`, parallel to how
  `ShardMetricsLedgerTests` is named for the ledger.
- **`BIBLIOGRAPHY.md`** — no change. The glossary's *cross-slice invariant* and
  *post-mortem rule* rows are unaffected.
- **`docs/sdlc-practices.md` §6** — one clause: the technical-debt register is
  machine-checked, joining the story map, shard manifest, and ledger. Keep it to the
  sentence; this document is an external-facing brief and must not grow.
- **No `storymap.yaml` edit** (no story/task gains a test ref here) and **no ledger entry**
  in `shard-metrics.yaml` — this is factory-infrastructure work, not a shipped slice, the
  same precedent the 2026-09-24 dashboard work set.
- **`docs/factory/dashboard.html`** — regenerate last, after the register edits above, so
  the snapshot is not stale (CI gates this: `ci.yml`, commit `2d786ea`).

---

## Task sequence — four dispatches, one branch

> **Correction (2026-09-26, controller): ordering hazard, found by the Part A executor.**
> Part B rule 5 validates that every `where` path exists. **TD-014's own `where` is
> `docs/factory/technical-debt.md` — the file Part C deletes.** So the original A → B → C → D
> order makes Part B's test go red on the register's own entry. `DL-004.systemic_gap` and
> `TD-014.what` are self-referentially stale for the same reason. Repointing them is a
> *content* edit, which does not belong in Part A ("prose migrated verbatim"). So the
> self-referential fields are corrected in a small **Dispatch 1b** between A and B, and the
> rest of Part D still runs last.
>
> Reordered: **A → 1b → B → C → D**. Dispatch 1b is controller-only, one commit, three fields.

**Dispatch 1 (Part A, 1 file, 1 commit)** — **DONE** (`24fee5c`)
1. Write the YAML, all **21** entries, prose verbatim.
2. Gate: the `python3 -c` load prints `7 10 4`; the uniqueness check prints `dupes: NONE`; prose diffed against the `.md` **by eye**.
3. Commit: `refactor: add machine-checked technical-debt.yaml register (TD-014)`

**Dispatch 1b (controller only, 1 file, 1 commit) — repoint the self-referential fields**

`technical-debt.md` is still present and still authoritative at this point, so these three
edits go in **both** files to keep them identical until Part C deletes the `.md`:

1. `TD-014.where` → `docs/factory/technical-debt.yaml`,
   `VitaTrack.ArchitectureTests/TechnicalDebtRegisterTests.cs` (from `technical-debt.md`)
2. `TD-014.what` — the clause "nothing guards `technical-debt.md`" becomes "...nothing guards
   the register" (it is the entry that Part B is about to close; leaving the filename makes the
   entry false the moment the test lands)
3. `DL-004.systemic_gap` — same: `technical-debt.md` → "the register" in the "no machine check"
   clause. **Do not change `closed_in`,** which is already absent for DL-004.

Gate: the two files still agree on every other field, and
`python3 -c "import yaml; d=yaml.safe_load(open('docs/factory/technical-debt.yaml')); print(len(d['open']), len(d['closed']), len(d['defects']))"`
still prints `7 10 4`.
Commit: `docs: repoint the register's self-referential fields before they go stale`

**Dispatch 2 (Part B, 1 file, 1 commit)**
1. `TechnicalDebtRegisterTests` — five rules + the negative-path test.
2. Gate: `dotnet test VitaTrack.ArchitectureTests -c Release` green; report the count.
3. Commit: `test: guard the debt register — id uniqueness, required fields, where paths (TD-014)`

**Dispatch 3 (Part C, 4 files, 2 commits)**
1. Commit 1 — `refactor: dashboard reads the debt register from YAML`: C1–C3 + C5
   (generator + its tests). `technical-debt.md` still present and now unread.
2. Commit 2 — `refactor: delete the prose debt register`: C4 delete + C6 regenerate.
   The pre-commit hook runs `ShardOwnershipTests` on every commit; the deleted file is not
   a scanned path, so no manifest change is needed — **verify that before committing**
   (DL-002 defect a).

**Dispatch 4 (Part D, controller only)** — register close-out, docs, regenerate. Not
delegated.

---

## Gates (run before claiming done)

1. `dotnet format VitaTrack.sln --verify-no-changes`
2. `dotnet build VitaTrack.sln -c Release`
3. `dotnet test VitaTrack.sln -c Release` — arch + unit. Baseline **arch 18 / unit 234**.
4. `python3 scripts/test-generate-factory-dashboard.py` — all green
5. `python3 scripts/generate-factory-dashboard.py --check` — exit 0
6. `./test-e2e.sh` — **no product code changes and no view/controller/JS changes, so this
   gate is expected to be untouched.** A red e2e is a **finding**: either an e2e spec
   references `technical-debt.md` (grep and re-point), or something outside this change's
   assumption is true. Report; do not loosen an assertion.

---

## Guards that stay green by construction

- `ShardOwnershipTests` — the arch-test project is not a scanned root
  (`ShardOwnershipTests.cs:145-149`); `docs/factory/technical-debt.md` was never a scanned
  path. **No `shards.yaml` change.** Verify before Dispatch 3 commit 2.
- `FileSizeTests` — `TechnicalDebtRegisterTests.cs` is a class in its own file; target
  ~130 lines against the 300-line complete-type trigger. The `|struct` addition (A7 of the
  TD-011 work) already counts `class` here.
- `StoryMapConsistencyTests` — no `storymap.yaml` edit, no spec orphaned.
- `UiReachabilityTests` — scans `Views/` + `Controllers/`; neither changes.
- `CrossSliceSqlTests` — reads only slice `core:` lists; no slice list changes.
- `RepositoryNamingTests`, `ControllerHygieneTests`, `WebLayerDependencyTests`,
  `EcosystemGuardrailTests`, `ServicesLayeringTests` — no product code.

---

## Out of scope

- **Resolving TD/DL references from other documents** (TD-018, the meta-test). Distinct
  change, distinct design questions.
- **A generated Markdown view of the register.** Decided against: a second artifact that
  can drift from its source is the failure class this change removes.
- **`human_interventions` derivation** (the CI-telemetry work scoped separately). TD-014's
  close note must not imply the ledger is auto-derived — it is not.
- **The `--check` freshness gate is already sufficient** for the dashboard; no new gate.
- Restructuring the other three registers (`storymap.yaml`, `shards.yaml`,
  `shard-metrics.yaml`) — they are already machine-checked.
- Splitting `generate-factory-dashboard.py` (840 lines) or its test file (914 lines). Both
  are Python and outside the 300-line C# type trigger, but note the ratio in the
  close-out: the factory's Python tooling is now larger than any single C# type. Candidate
  future debt, not this change.

---

## Escalation

Stop, log the deviation, and make the final message the escalation if:

- Any TD/DL id in the `.md` has no clean mapping to a field under the schema table — the
  migration is lossy somewhere and that is a design question, not an executor's call.
- A prose field cannot be expressed as a folded scalar without escaping that changes its
  meaning (backticks at line start, a line beginning `>`).
- `7 10 4` does not print — an entry was dropped in migration. Restore it; do not adjust
  the expected counts.
- Any id appears twice in the migrated YAML. Part B's rule 1 exists to make this a red
  build; discovering it here means the migration carried `main`'s duplicate `TD-011` rather
  than the renumbered `TD-017`. Confirm cherry-pick `55d1aaf`/`74826d8` is present first.
- The dashboard's debt section renders a **different** count than before the change.
- `TechnicalDebtRegisterTests` goes red on the migrated YAML for a rule the briefing did
  not anticipate — the rule is wrong or the migration is wrong. Report which; **do not**
  relax the rule to go green, and **do not** add a skip.
- `ShardOwnershipTests` requires a manifest change for the new arch-test file or the
  deleted `.md` (contradicts *Guards that stay green by construction* — verify and
  report, do not edit `shards.yaml` to make it go green).
- `dotnet test` arch count is not 18 + 1 (or 18 + 2), or unit count is not 234.
