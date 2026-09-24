# Factory Dashboard Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Generate a committed, directly openable HTML dashboard showing factory shard metrics and plans that are incomplete or need review.

**Architecture:** A Python 3 generator reads `shards.yaml`, `docs/factory/shard-metrics.yaml`, all Markdown plans under `docs/plans` and `docs/superpowers/plans`, and matching `.superpowers/sdd/*/progress.md` evidence. It normalizes those sources, renders Bootstrap-only HTML to `docs/factory/dashboard.html`, and exposes `--check` without changing the ASP.NET application.

**Tech Stack:** Python 3, PyYAML, `unittest`, standard-library `html`, `argparse`, `dataclasses`, `pathlib`, `datetime`, and Bootstrap 5 CDN stylesheet.

## Global Constraints

- Dashboard is a repository artifact; do not add an ASP.NET route, controller, view, database table, or runtime dependency.
- Source-of-truth precedence is explicit plan status, whole-plan progress evidence, then checkboxes; child-item completion does not complete a parent plan.
- Missing ledger entries remain `Missing ledger`; optional metrics remain `—`; explicit zero remains `0`.
- Render only after all source parsing and validation succeeds; errors must leave the existing dashboard unchanged.
- HTML-escape every dynamic value; use no inline JavaScript or custom CSS.
- `--check` must ignore only the volatile generated timestamp when comparing snapshots.
- Test with Python `unittest`; run `dotnet test` and `./format-check.sh` before claiming completion.
- Do not commit unless explicitly requested by the user.

---

### Task 1: Add failing generator tests and fixtures

**Files:**
- Create: `scripts/test-generate-factory-dashboard.py`
- Test target: `scripts/generate-factory-dashboard.py`

**Interfaces:**
- Consumes: planned `PlanState`, `classify_plan`, `render_dashboard`, `DashboardData`, and `DashboardError` from the generator.
- Produces: executable tests that lock status precedence, escaping, and CLI output behavior before implementation.

- [ ] **Step 1: Write test module with plan-status cases**

Create `scripts/test-generate-factory-dashboard.py` with `unittest`, import the generator by module name, and define these tests:

```python
import unittest

from generate_factory_dashboard import DashboardData, PlanState, classify_plan, render_dashboard


class PlanClassificationTests(unittest.TestCase):
    def test_explicit_status_wins_over_stale_unchecked_boxes(self):
        state = classify_plan(
            "docs/plans/example.md",
            "# Example\n\n- **Status:** Shipped (2026-09-24)\n\n- [x] shipped task\n- [ ] stale task\n",
        )

        self.assertEqual("complete", state.status)
        self.assertEqual("plan status", state.evidence)
        self.assertEqual(1, state.checked_boxes)
        self.assertEqual(1, state.unchecked_boxes)
        self.assertTrue(state.stale_checkboxes)

    def test_child_progress_does_not_complete_parent_plan(self):
        state = classify_plan(
            "docs/plans/rollout.md",
            "# Rollout\n\n- [x] Nutrients shipped\n- [ ] LLM remains\n",
            "Nutrients shipped; parent rollout remains open.",
        )

        self.assertEqual("incomplete", state.status)
        self.assertEqual("checkboxes", state.evidence)

    def test_no_evidence_requires_review(self):
        state = classify_plan("docs/plans/notes.md", "# Notes\n\nNo execution checklist.\n")

        self.assertEqual("needs-review", state.status)
        self.assertEqual("no status, progress, or checkbox evidence", state.evidence)

    def test_all_checked_tasks_are_complete(self):
        state = classify_plan("docs/plans/done.md", "# Done\n\n- [x] one\n- [x] two\n")

        self.assertEqual("complete", state.status)
        self.assertEqual(2, state.checked_boxes)
        self.assertEqual(0, state.unchecked_boxes)


class DashboardRenderingTests(unittest.TestCase):
    def test_rendering_escapes_dynamic_values(self):
        data = DashboardData(
            shards=[{"id": "X1", "name": "A & B"}],
            ledger_by_id={},
            plans=[PlanState("docs/plans/<unsafe>.md", "<script>alert(1)</script>", "needs-review", "no evidence", 0, 0, False)],
            generated_at="2026-09-24T00:00:00+00:00",
        )

        html = render_dashboard(data)

        self.assertIn("A &amp; B", html)
        self.assertIn("&lt;script&gt;alert(1)&lt;/script&gt;", html)
        self.assertNotIn("<script>alert(1)</script>", html)
```

Use the exact status strings `complete`, `incomplete`, and `needs-review`; the renderer maps them to display labels.

- [ ] **Step 2: Add tests for source validation and snapshot checking**

Add tests using `tempfile.TemporaryDirectory` and this concrete fixture helper:

```python
import tempfile
from pathlib import Path

from generate_factory_dashboard import (
    DashboardError,
    check_dashboard,
    load_dashboard,
    render_dashboard,
    update_dashboard,
)


def make_valid_root() -> tuple[tempfile.TemporaryDirectory, Path]:
    temporary = tempfile.TemporaryDirectory()
    root = Path(temporary.name)
    (root / "docs/factory").mkdir(parents=True)
    (root / "docs/plans").mkdir(parents=True)
    (root / "docs/superpowers/plans").mkdir(parents=True)
    (root / "shards.yaml").write_text("slices:\n  - id: X1\n    name: Example\n", encoding="utf-8")
    (root / "docs/factory/shard-metrics.yaml").write_text(
        "shards:\n  - id: X1\n    name: Example\n    agent: human\n"
        "    human_interventions: 0\n    guardrail_failures: 0\n"
        "    fix_commits: 0\n    defects_escaped: 0\n",
        encoding="utf-8",
    )
    (root / "docs/plans/example.md").write_text("# Example\n", encoding="utf-8")
    return temporary, root


class SnapshotTests(unittest.TestCase):
    def test_check_reports_stale_snapshot(self):
        temporary, root = make_valid_root()
        self.addCleanup(temporary.cleanup)
        output = root / "docs/factory/dashboard.html"
        output.write_text("<html>stale</html>", encoding="utf-8")

        self.assertEqual(1, check_dashboard(root))
```

Add `test_check_accepts_current_snapshot`, which calls `update_dashboard(root)` and asserts `check_dashboard(root) == 0`. Add `test_malformed_input_does_not_replace_snapshot`, which writes `slices: [` to `shards.yaml`, records existing output text, calls `update_dashboard(root)` inside `assertRaises(DashboardError)`, and asserts the output text is unchanged.

- [ ] **Step 3: Run tests to verify they fail for missing implementation**

Run:

```bash
python3 scripts/test-generate-factory-dashboard.py
```

Expected: failure with `ModuleNotFoundError` for `generate_factory_dashboard` or missing planned symbols.

- [ ] **Step 4: Review test-only diff**

Run:

```bash
git diff --check -- scripts/test-generate-factory-dashboard.py
git status --short
```

Expected: only the planned test file is new; no dashboard output has been created.

---

### Task 2: Implement source loading and plan classification

**Files:**
- Create: `scripts/generate-factory-dashboard.py`
- Test: `scripts/test-generate-factory-dashboard.py`

**Interfaces:**
- Consumes: repository-relative source files and PyYAML `safe_load`.
- Produces: `DashboardError`, `PlanState`, `DashboardData`, `load_dashboard(root: Path) -> DashboardData`, and `classify_plan(path: str, text: str, progress_text: str = "") -> PlanState`.

- [ ] **Step 1: Add data types and safe YAML loading**

Start the module with:

```python
from __future__ import annotations

import argparse
import html
import re
import sys
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

import yaml


class DashboardError(RuntimeError):
    pass


@dataclass(frozen=True)
class PlanState:
    path: str
    title: str
    status: str
    evidence: str
    checked_boxes: int
    unchecked_boxes: int
    stale_checkboxes: bool


@dataclass(frozen=True)
class DashboardData:
    shards: list[dict[str, Any]]
    ledger_by_id: dict[str, dict[str, Any]]
    plans: list[PlanState]
    generated_at: str
```

Implement `read_yaml(path: Path) -> dict[str, Any]` so missing files, YAML parser errors, non-mapping roots, and unexpected top-level shapes raise `DashboardError` with the source path in the message. Never catch and hide source errors.

- [ ] **Step 2: Implement `classify_plan` with the approved precedence**

Use anchored expressions for an explicit plan status and whole-plan progress evidence:

```python
STATUS_RE = re.compile(r"(?im)^\s*(?:[-*]\s*)?(?:\*\*)?status(?:\*\*)?\s*:\s*(.+?)\s*$")
PROGRESS_RE = re.compile(r"(?im)^\s*(?:[-*]\s*)?(?:\*\*)?(?:overall\s+status|plan\s+status|status)(?:\*\*)?\s*:\s*(?:plan\s+)?(?:is\s+)?(complete|completed|done|shipped)\b")
CHECKED_RE = re.compile(r"(?m)^\s*- \[x\]\s+", re.IGNORECASE)
UNCHECKED_RE = re.compile(r"(?m)^\s*- \[ \]\s+")
```

Extract the first Markdown H1 as title, falling back to `Path(path).stem`. Treat explicit status containing `done`, `complete`, `completed`, or `shipped` as complete. Treat progress evidence as complete only when the line says the plan itself is complete/shipped; do not match child-item lines. If no completion evidence exists, all checked boxes means complete, any unchecked box means incomplete, and no checkboxes means needs-review. Set `stale_checkboxes` only when explicit completion evidence coexists with unchecked boxes.

- [ ] **Step 3: Implement manifest, ledger, and plan discovery**

Implement `load_dashboard(root: Path) -> DashboardData` to:

1. Load `shards.yaml` and validate that `slices` is a list of mappings with unique string IDs and names.
2. Load `docs/factory/shard-metrics.yaml` and validate that `shards` is a list of mappings with unique IDs.
3. Raise `DashboardError` when a ledger ID is not declared in `shards.yaml` or when a ledger entry lacks the required metrics fields.
4. Recursively collect `*.md` files from `docs/plans` and `docs/superpowers/plans`, sorted by repository-relative path.
5. For each plan, look for `.superpowers/sdd/<plan-stem>/progress.md`; pass its text as progress evidence when it exists.
6. Return declared shard mappings, a ledger dictionary, classified plans, and the current UTC timestamp in ISO-8601 format.

Treat absent optional metric values as `None`; preserve explicit numeric zero.

- [ ] **Step 4: Run focused tests and fix classification failures**

Run:

```bash
python3 scripts/test-generate-factory-dashboard.py
```

Expected: classification and loading tests pass; renderer tests still fail until Task 3.

- [ ] **Step 5: Review source boundary**

Run:

```bash
python3 -m py_compile scripts/generate-factory-dashboard.py
git diff --check -- scripts/generate-factory-dashboard.py
```

Expected: compilation succeeds and no whitespace errors exist.

---

### Task 3: Render Bootstrap dashboard and expose CLI check mode

**Files:**
- Modify: `scripts/generate-factory-dashboard.py`
- Test: `scripts/test-generate-factory-dashboard.py`

**Interfaces:**
- Consumes: `DashboardData` and source-relative links from `docs/factory/dashboard.html`.
- Produces: `render_dashboard(data: DashboardData) -> str`, `generate_dashboard(root: Path) -> str`, `update_dashboard(root: Path) -> int`, `check_dashboard(root: Path) -> int`, and a CLI accepting `--check`.

- [ ] **Step 1: Add HTML rendering helpers**

Implement small helpers that call `html.escape(value, quote=True)` for every dynamic value:

```python
def display_metric(value: Any) -> str:
    return "—" if value is None else html.escape(str(value), quote=True)


def format_cost(value: Any) -> str:
    return "—" if value is None else f"${float(value):.2f}"
```

Implement `render_dashboard(data: DashboardData) -> str` as a complete HTML document with:

- `<meta charset="utf-8">`, responsive viewport metadata, and Bootstrap 5.3 CDN stylesheet.
- Header with title, generated timestamp, source links to `../../shards.yaml`, `shard-metrics.yaml`, and the plan directories, and command `python3 scripts/generate-factory-dashboard.py`.
- Summary cards for declared shards, ledger records, missing ledger IDs, human interventions, guardrail failures, recorded spend, and incomplete/review plan counts.
- Shard table covering all declared shards; rows without a ledger record display `Missing ledger` and `—` metrics.
- Shard model, cost, and notes rendered from ledger values, with notes in native `<details>`.
- Plan queue sorted by status order (`incomplete`, `needs-review`, `complete`) and then path; each row includes title, status badge, evidence, checkbox counts, stale-checkbox text, and a relative source link.

Do not add inline `<script>` or custom `<style>` elements. Use Bootstrap classes and semantic table/card markup only.

- [ ] **Step 2: Implement safe output and normalized comparison**

Implement:

```python
GENERATED_AT_RE = re.compile(
    r'(<time datetime=")[^"]+(">)[^<]+(</time>)'
)


def normalized_for_check(document: str) -> str:
    return GENERATED_AT_RE.sub(r"\1normalized\2normalized\3", document)


def write_dashboard(root: Path, document: str) -> None:
    output = root / "docs/factory/dashboard.html"
    output.write_text(document, encoding="utf-8")


def generate_dashboard(root: Path) -> str:
    return render_dashboard(load_dashboard(root))


def update_dashboard(root: Path) -> int:
    write_dashboard(root, generate_dashboard(root))
    return 0


def check_dashboard(root: Path) -> int:
    output = root / "docs/factory/dashboard.html"
    document = generate_dashboard(root)
    if not output.exists() or normalized_for_check(output.read_text(encoding="utf-8")) != normalized_for_check(document):
        return 1
    return 0
```

Build the complete document before calling `write_dashboard`; do not open or truncate output before parsing succeeds.

- [ ] **Step 3: Add argparse entry point and test output modes**

Implement `main(argv: list[str] | None = None) -> int` with `--check`; default mode calls `update_dashboard`, check mode returns `check_dashboard`, and `if __name__ == "__main__":` raises `SystemExit(main())`. Catch `DashboardError` at the CLI boundary, print the error to stderr, and return 1. Keep `load_dashboard` exceptions available to direct tests.

- [ ] **Step 4: Run focused tests and generator smoke test**

Run:

```bash
python3 scripts/test-generate-factory-dashboard.py
python3 scripts/generate-factory-dashboard.py
python3 scripts/generate-factory-dashboard.py --check
```

Expected: tests pass; generation creates `docs/factory/dashboard.html`; check exits 0.

- [ ] **Step 5: Inspect generated HTML for raw dynamic values**

Run:

```bash
python3 -c 'from pathlib import Path; text=Path("docs/factory/dashboard.html").read_text(); assert "<script>alert" not in text; assert "Missing ledger" in text'
```

Expected: command exits 0.

---

### Task 4: Document usage, generate committed snapshot, and run full verification

**Files:**
- Modify: `docs/factory/metrics.md`
- Create/update: `docs/factory/dashboard.html`

**Interfaces:**
- Consumes: generator CLI and current repository sources.
- Produces: documented regeneration workflow and committed current snapshot.

- [ ] **Step 1: Add usage documentation**

Append a short section to `docs/factory/metrics.md` with exact commands:

```bash
python3 scripts/generate-factory-dashboard.py
python3 scripts/generate-factory-dashboard.py --check
python3 scripts/test-generate-factory-dashboard.py
```

Document that the page is generated from the manifest, ledger, and plan Markdown; that missing ledger records and needs-review plans are intentional states; and that PyYAML must be available in the Python 3 environment.

- [ ] **Step 2: Regenerate the snapshot**

Run:

```bash
python3 scripts/generate-factory-dashboard.py
```

Expected: `docs/factory/dashboard.html` reflects current `shards.yaml`, `docs/factory/shard-metrics.yaml`, and all plan files.

- [ ] **Step 3: Verify snapshot freshness and tests**

Run:

```bash
python3 scripts/generate-factory-dashboard.py --check
python3 scripts/test-generate-factory-dashboard.py
```

Expected: both commands exit 0.

- [ ] **Step 4: Run repository gates**

Run:

```bash
dotnet test
./format-check.sh
```

Expected: all tests pass and formatting verification reports no changes. If Python 3 or PyYAML is unavailable, stop and report the missing prerequisite rather than weakening the generator contract.

- [ ] **Step 5: Review final diff**

Run:

```bash
git status --short
git diff --check
git diff --stat
```

Expected: only the generator, its tests, dashboard snapshot, metrics documentation, and approved design/plan documents are changed. Do not commit unless explicitly requested.
