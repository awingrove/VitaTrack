# Dashboard — Technical Debt Section

**Branch:** `feat/dashboard-technical-debt` (base `37c8a76` on `main`).
**Executor:** mimo-2.6-flash-free (cheap). **Controller:** this session.
**Goal:** the Python-generated factory dashboard gains a technical-debt section fed
by `docs/factory/technical-debt.md` (open entries + defect log), matching how the
dashboard already renders ledger + plans.

## Current-state facts (verified — do not re-derive)

- `scripts/generate-factory-dashboard.py` (657 lines): functional style, dataclasses
  `PlanState` (line 22) and `DashboardData` (line 33: fields `shards`,
  `ledger_by_id`, `plans`, `generated_at`).
- `load_dashboard(root)` (line 210) → `_load_shards` + `_load_ledger` +
  `[_classify_plan_file …]` → returns `DashboardData`.
- `render_dashboard(data)` (line 345): computes `cards` tuple of
  `_summary_card(title, value, detail)` (line 279) calls — current order: Declared
  shards, Ledger records, Missing ledger IDs, Human interventions, Guardrail
  failures, Fix commits, Escaped defects, Cheap-agent records, Recorded spend,
  Incomplete plans, Needs-review plans. Then `parts = […]` HTML list with
  header → cards row → `<section>Shards</section>` → `<section>Plans</section>` →
  close. Header "Sources:" line lists `shards.yaml`, `shard-metrics.yaml`,
  `docs/plans`, `docs/superpowers/plans` links.
- Error style: `DashboardError` (line 17); `read_yaml` raises
  `DashboardError(f"{path}: file not found")` for missing files (line 96).
- All dynamic HTML goes through `html.escape(…, quote=True)` (see
  `display_metric`, line 258) — no inline `<script>`/`<style>` (a test asserts this).
- `docs/factory/technical-debt.md` format (parser contract):
  - Section headings, exact lines: `## Open entries`, `## Closed entries`,
    `## Defect log` (in that order).
  - Entries: `### TD-003 — <title>` followed by flat `- **<Label>:** <text>` bullets;
    labels seen: Where, What, Interest, Paydown, Closed `<date>`. Continuation lines
    of a bullet are indented two spaces.
  - Defect log intro paragraph, then entries like
    `- **DL-003 — <title>** (found Sep 2026 …).` with indented
    `  - **Injection stage:** …` sub-bullets.
  - **Note:** entry ids are not stable (`PR #21` restores TD-006 while others land);
    the parser must NOT assume a specific id set.
- Tests: `scripts/test-generate-factory-dashboard.py` (575 lines) — `unittest`,
  loader helper `_load_generator()` via importlib, classes
  `PlanClassificationTests`, `SourceLoadingTests`, `DashboardRenderingTests`,
  `SnapshotTests`, `CliModeTests`. Runner: `python3 scripts/test-generate-factory-dashboard.py`.
- Snapshot: generator writes `docs/factory/dashboard.html`; `--check` mode +
  `normalized_for_check` (timestamp-insensitive). Tests: 35 currently green.
- `FileSizeTests` scans `*.cs` only — Python file growth is unguarded; stdlib only
  (`re`, `html` already imported).

## Design (pre-decided)

1. **Data** — add dataclasses `DebtEntry(id: str, title: str, interest: str)`,
   `DefectEntry(id: str, title: str)`, `DebtRegister(open_entries: list[DebtEntry],
   closed_count: int, defects: list[DefectEntry])`; add field
   `debt: DebtRegister` to `DashboardData`.
2. **Loader** — `_load_debt(root) -> DebtRegister`, called from `load_dashboard`.
   - Source: `root / "docs/factory/technical-debt.md"`. Missing file →
     `DashboardError(f"{path}: file not found")`.
   - Missing any of the three exact section headings → `DashboardError`.
   - Open: each `### TD-<id> — <title>` line → `DebtEntry`; `interest` = the
     `- **Interest:**` bullet text plus its indented continuation lines, joined with
     a single space; no Interest bullet → `""`.
   - Closed: count of `### TD-` headings in that section.
   - Defects: lines matching `- **DL-<id> — <title>**` → `DefectEntry`; title stops
     at the closing `**`; text after `**` ignored.
3. **Cards** — insert two into the `cards` tuple immediately after
   `Escaped defects`:
   - `_summary_card("Open debt", str(len(data.debt.open_entries)), f"{data.debt.closed_count} closed")`
   - `_summary_card("Defect log entries", str(len(data.debt.defects)))`
4. **Section** — new `<section>` titled `Technical Debt` between the Shards and
   Plans sections:
   - Table 1 columns `(ID, Debt, Interest)` — one row per open entry, all cells
     `html.escape`d; empty → `<tr><td colspan="3" class="text-muted">No open
     technical debt.</td></tr>`.
   - Line under table 1: `{closed_count} closed entries — see
     <a href="technical-debt.md">technical-debt.md</a>`.
   - Table 2 columns `(ID, Defect)` — one row per defect-log entry; empty →
     `<tr><td colspan="2" class="text-muted">No defects logged.</td></tr>`.
   - Column-header tuples as module constants `DEBT_COLUMNS` / `DEFECT_COLUMNS`
     beside `SHARD_COLUMNS` / `PLAN_COLUMNS` (line ~236).
5. **Sources header** — append `<a href="technical-debt.md">technical-debt.md</a>`
   to the Sources line (same `·` separator style). `dashboard.html` lives in
   `docs/factory/`, same directory as the register.
6. **Tests first (TDD)** — append to `scripts/test-generate-factory-dashboard.py`:
   - `DebtLoadingTests`: parses open entries (incl. multi-line Interest
     continuation); counts closed; parses defect id/title (strips `**`, ignores
     trailing parenthetical); missing file → `DashboardError`; missing section
     heading → `DashboardError`; ids are discovered, not hardcoded (fixture with a
     novel id parses).
   - `DebtRenderingTests`: renders section between Shards and Plans; open rows and
     defect rows present with counts; interest/title values escaped (`<script>`
     fixture); cards show open/closed/defect counts; no inline script/style.
   - Build fixtures with `tempfile` + minimal file trees per existing test style.

## Task sequence (single commit)

1. Write the failing tests (6 above).
2. Implement dataclasses + `_load_debt` + `load_dashboard` wiring + render changes.
3. Run `python3 scripts/generate-factory-dashboard.py` to regenerate
   `docs/factory/dashboard.html`.
4. Gate: `python3 scripts/test-generate-factory-dashboard.py` all green, then
   `python3 scripts/generate-factory-dashboard.py --check` exit 0.
5. Commit everything as one commit:
   `feat: technical-debt section in factory dashboard (open entries + defect log)`

## Gates (run before claiming done)

1. `python3 scripts/test-generate-factory-dashboard.py` — all green (35 existing +
   new)
2. `python3 scripts/generate-factory-dashboard.py --check` — exit 0
3. `dotnet format VitaTrack.sln --verify-no-changes`
4. `dotnet test VitaTrack.sln -c Release` — arch 15/15, unit 211/211 (pre-commit
   hook covers these on commit)

No e2e: the dashboard is a generated docs artifact, not an app surface.

## Out of scope

- Register format changes; rendering Where/What/Paydown fields; closed-entry rows;
  CI wiring; new Python dependencies; any C#/views/shards/storymap edits; other
  dashboard sections.

## Escalation

A register format fact above that proves wrong, or a gate red you cannot fix
without breaking out-of-scope → stop, log deviation, report.
