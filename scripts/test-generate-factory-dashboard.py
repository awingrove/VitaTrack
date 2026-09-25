import importlib.util
import io
import re
import sys
import tempfile
import unittest
from contextlib import redirect_stderr
from pathlib import Path

_GENERATOR_PATH = Path(__file__).resolve().with_name("generate-factory-dashboard.py")
_VALID_LEDGER_ENTRY = """  - id: X1
    name: Example
    agent: human
    human_interventions: 0
    guardrail_failures: 0
    fix_commits: 0
    defects_escaped: 0
"""


def _load_generator():
    if not _GENERATOR_PATH.is_file():
        raise ModuleNotFoundError(
            "No module named 'generate_factory_dashboard'",
            name="generate_factory_dashboard",
        )
    spec = importlib.util.spec_from_file_location("generate_factory_dashboard", _GENERATOR_PATH)
    if spec is None or spec.loader is None:
        raise ModuleNotFoundError(
            "No module named 'generate_factory_dashboard'",
            name="generate_factory_dashboard",
        )
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


def _require(module, name: str):
    try:
        return getattr(module, name)
    except AttributeError:
        raise ImportError(
            f"cannot import name '{name}' from 'generate_factory_dashboard'"
        ) from None


_generator = _load_generator()

DashboardData = _require(_generator, "DashboardData")
DashboardError = _require(_generator, "DashboardError")
DebtEntry = _require(_generator, "DebtEntry")
DebtRegister = _require(_generator, "DebtRegister")
DefectEntry = _require(_generator, "DefectEntry")
PlanState = _require(_generator, "PlanState")
check_dashboard = _require(_generator, "check_dashboard")
classify_plan = _require(_generator, "classify_plan")
load_dashboard = _require(_generator, "load_dashboard")
main = _require(_generator, "main")
render_dashboard = _require(_generator, "render_dashboard")
update_dashboard = _require(_generator, "update_dashboard")


def make_valid_root() -> tuple[tempfile.TemporaryDirectory, Path]:
    temporary = tempfile.TemporaryDirectory()
    root = Path(temporary.name)
    (root / "docs/factory").mkdir(parents=True)
    (root / "docs/plans").mkdir(parents=True)
    (root / "docs/superpowers/plans").mkdir(parents=True)
    (root / "shards.yaml").write_text("slices:\n  - id: X1\n    name: Example\n", encoding="utf-8")
    (root / "docs/factory/shard-metrics.yaml").write_text(
        f"shards:\n{_VALID_LEDGER_ENTRY}", encoding="utf-8"
    )
    (root / "docs/factory/technical-debt.md").write_text(
        "# Technical Debt Register\n\n## Open entries\n\n## Closed entries\n\n"
        "## Defect log\n",
        encoding="utf-8",
    )
    (root / "docs/plans/example.md").write_text("# Example\n", encoding="utf-8")
    return temporary, root


def _write_ledger(root: Path, entry: str) -> None:
    (root / "docs/factory/shard-metrics.yaml").write_text(
        f"shards:\n{entry}", encoding="utf-8"
    )


def _replace_ledger_field(entry: str, field: str, value: str) -> str:
    prefix = "  - id:" if field == "id" else f"    {field}:"
    replacement = f"{prefix} {value}"
    return "\n".join(
        replacement if line.startswith(prefix) else line for line in entry.splitlines()
    ) + "\n"


def _write_debt(root: Path, content: str) -> None:
    (root / "docs/factory/technical-debt.md").write_text(content, encoding="utf-8")


def _debt_document(
    open_section: str, closed_section: str, defect_section: str
) -> str:
    return (
        "# Technical Debt Register\n\n"
        f"## Open entries\n\n{open_section}\n\n"
        f"## Closed entries\n\n{closed_section}\n\n"
        f"## Defect log\n\n{defect_section}\n"
    )


def _render_with_debt(debt) -> str:
    data = DashboardData(
        shards=[{"id": "X1", "name": "Example"}],
        ledger_by_id={},
        plans=[],
        generated_at="2026-09-24T00:00:00+00:00",
        debt=debt,
    )
    return render_dashboard(data)


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

    def test_real_plan_fenced_child_status_does_not_complete_plan(self):
        plan_path = Path(__file__).resolve().parents[1] / "docs/plans/2026-08-23-list-column-sort.md"
        state = classify_plan(
            "docs/plans/2026-08-23-list-column-sort.md",
            plan_path.read_text(encoding="utf-8"),
        )

        self.assertEqual("incomplete", state.status)
        self.assertEqual("checkboxes", state.evidence)
        self.assertGreater(state.unchecked_boxes, 0)

    def test_indented_status_example_does_not_complete_plan(self):
        state = classify_plan(
            "docs/plans/indented.md",
            "# Indented\n\n    Status: done\n",
        )

        self.assertEqual("needs-review", state.status)
        self.assertEqual("no status, progress, or checkbox evidence", state.evidence)

    def test_nested_mixed_fences_ignore_metadata_and_checkboxes(self):
        state = classify_plan(
            "docs/plans/fenced.md",
            "# Fenced\n\n"
            "````markdown\n"
            "```markdown\n"
            "~~~text\n"
            "Status: done\n"
            "- [x] fenced task\n"
            "- [ ] fenced task\n"
            "~~~\n"
            "Status: done\n"
            "- [x] mixed fenced task\n"
            "````\n\n"
            "Status: pending\n"
            "- [ ] real task\n",
        )

        self.assertEqual("incomplete", state.status)
        self.assertEqual("plan status", state.evidence)
        self.assertEqual(0, state.checked_boxes)
        self.assertEqual(1, state.unchecked_boxes)

    def test_fenced_progress_metadata_is_ignored(self):
        state = classify_plan(
            "docs/plans/fenced-progress.md",
            "# Fenced progress\n",
            "````markdown\n"
            "```markdown\n"
            "~~~text\n"
            "Overall status: plan is complete\n"
            "~~~\n"
            "Status: done\n"
            "- [x] fenced task\n"
            "````\n",
        )

        self.assertEqual("needs-review", state.status)
        self.assertEqual("no status, progress, or checkbox evidence", state.evidence)

    def test_status_progress_matches_only_whole_plan_completion(self):
        cases = (
            ("Status: plan is complete", "complete"),
            ("Status: plan is not complete", "needs-review"),
            ("Status: child task complete", "needs-review"),
            ("Overall status: child task complete", "needs-review"),
        )

        for progress_line, expected_status in cases:
            with self.subTest(progress_line=progress_line):
                state = classify_plan(
                    "docs/plans/progress.md",
                    "# Progress\n",
                    f"{progress_line}\n",
                )

                self.assertEqual(expected_status, state.status)

    def test_negated_status_values_do_not_complete_plan(self):
        for status_line in (
            "Status: Incomplete",
            "Status: Not completed",
            "- Status: Incomplete",
            "- Status: Not completed",
            "- **Status:** Incomplete",
            "- **Status:** Not completed",
        ):
            with self.subTest(status_line=status_line):
                state = classify_plan(
                    "docs/plans/status.md",
                    f"# Status\n\n{status_line}\n",
                )

                self.assertEqual("incomplete", state.status)
                self.assertEqual("plan status", state.evidence)

    def test_negated_whole_plan_progress_does_not_complete_plan(self):
        for progress_line in (
            "Overall status: plan is not complete",
            "Plan status: not completed",
        ):
            with self.subTest(progress_line=progress_line):
                state = classify_plan(
                    "docs/plans/progress.md",
                    "# Progress\n",
                    f"{progress_line}\n",
                )

                self.assertEqual("needs-review", state.status)
                self.assertEqual("no status, progress, or checkbox evidence", state.evidence)

    def test_no_evidence_requires_review(self):
        state = classify_plan("docs/plans/notes.md", "# Notes\n\nNo execution checklist.\n")

        self.assertEqual("needs-review", state.status)
        self.assertEqual("no status, progress, or checkbox evidence", state.evidence)

    def test_all_checked_tasks_are_complete(self):
        state = classify_plan("docs/plans/done.md", "# Done\n\n- [x] one\n- [x] two\n")

        self.assertEqual("complete", state.status)
        self.assertEqual(2, state.checked_boxes)
        self.assertEqual(0, state.unchecked_boxes)

    def test_whole_plan_progress_completion_wins_over_unchecked_parent_task(self):
        state = classify_plan(
            "docs/plans/cutover.md",
            "# Cutover\n\n- [ ] parent cutover task\n",
            "Overall status: plan is complete.",
        )

        self.assertEqual("complete", state.status)
        self.assertEqual("progress", state.evidence)
        self.assertEqual(0, state.checked_boxes)
        self.assertEqual(1, state.unchecked_boxes)
        self.assertTrue(state.stale_checkboxes)

    def test_child_item_progress_does_not_complete_plan_without_checkboxes(self):
        state = classify_plan(
            "docs/plans/rollout.md",
            "# Rollout\n\nNo execution checklist.\n",
            "Nutrients phase shipped; LLM integration pending.",
        )

        self.assertEqual("needs-review", state.status)
        self.assertEqual("no status, progress, or checkbox evidence", state.evidence)


class SourceLoadingTests(unittest.TestCase):
    def test_manifest_shape_is_validated(self):
        temporary, root = make_valid_root()
        self.addCleanup(temporary.cleanup)
        (root / "shards.yaml").write_text("slices: invalid\n", encoding="utf-8")

        with self.assertRaises(DashboardError):
            load_dashboard(root)

    def test_ledger_shape_is_validated(self):
        temporary, root = make_valid_root()
        self.addCleanup(temporary.cleanup)
        (root / "docs/factory/shard-metrics.yaml").write_text(
            "shards: invalid\n", encoding="utf-8"
        )

        with self.assertRaises(DashboardError):
            load_dashboard(root)

    def test_duplicate_manifest_id_is_rejected(self):
        temporary, root = make_valid_root()
        self.addCleanup(temporary.cleanup)
        (root / "shards.yaml").write_text(
            "slices:\n  - id: X1\n    name: First\n  - id: X1\n    name: Second\n",
            encoding="utf-8",
        )

        with self.assertRaises(DashboardError):
            load_dashboard(root)

    def test_duplicate_ledger_id_is_rejected(self):
        temporary, root = make_valid_root()
        self.addCleanup(temporary.cleanup)
        _write_ledger(root, _VALID_LEDGER_ENTRY + _VALID_LEDGER_ENTRY)

        with self.assertRaises(DashboardError):
            load_dashboard(root)

    def test_unknown_ledger_id_is_rejected(self):
        temporary, root = make_valid_root()
        self.addCleanup(temporary.cleanup)
        _write_ledger(root, _VALID_LEDGER_ENTRY.replace("id: X1", "id: X2"))

        with self.assertRaises(DashboardError):
            load_dashboard(root)

    def test_every_required_ledger_field_is_required(self):
        required_fields = (
            "id",
            "name",
            "agent",
            "human_interventions",
            "guardrail_failures",
            "fix_commits",
            "defects_escaped",
        )
        for field in required_fields:
            with self.subTest(field=field):
                temporary, root = make_valid_root()
                self.addCleanup(temporary.cleanup)
                prefix = "  - id:" if field == "id" else f"    {field}:"
                entry = "\n".join(
                    line for line in _VALID_LEDGER_ENTRY.splitlines() if not line.startswith(prefix)
                ) + "\n"
                _write_ledger(root, entry)

                with self.assertRaises(DashboardError):
                    load_dashboard(root)

    def test_malformed_ledger_values_are_rejected_as_dashboard_errors(self):
        cases = (
            ("name", "123"),
            ("agent", "[]"),
            ("human_interventions", "-1"),
            ("guardrail_failures", "1.5"),
            ("tokens_input", "-1"),
            ("tokens_output", "1.5"),
            ("cost_usd", ".nan"),
            ("cost_usd", ".inf"),
        )
        for field, value in cases:
            with self.subTest(field=field, value=value):
                temporary, root = make_valid_root()
                self.addCleanup(temporary.cleanup)
                entry = _VALID_LEDGER_ENTRY
                if field.startswith("tokens_") or field == "cost_usd":
                    entry += f"    {field}: {value}\n"
                else:
                    entry = _replace_ledger_field(entry, field, value)
                _write_ledger(root, entry)

                with self.assertRaises(DashboardError):
                    load_dashboard(root)

    def test_both_plan_directories_and_matching_progress_are_loaded(self):
        temporary, root = make_valid_root()
        self.addCleanup(temporary.cleanup)
        (root / "docs/plans/with-progress.md").write_text(
            "# With progress\n\n- [ ] remaining task\n", encoding="utf-8"
        )
        (root / "docs/superpowers/plans/second.md").write_text(
            "# Second\n\n- [x] complete task\n", encoding="utf-8"
        )
        progress_directory = root / ".superpowers/sdd/with-progress"
        progress_directory.mkdir(parents=True)
        (progress_directory / "progress.md").write_text(
            "Overall status: plan is complete.\n", encoding="utf-8"
        )

        data = load_dashboard(root)
        plans = {plan.path: plan for plan in data.plans}

        self.assertIn("docs/plans/with-progress.md", plans)
        self.assertIn("docs/superpowers/plans/second.md", plans)
        self.assertEqual("complete", plans["docs/plans/with-progress.md"].status)
        self.assertEqual("progress", plans["docs/plans/with-progress.md"].evidence)


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

    def test_rendering_escapes_ledger_model_and_notes(self):
        data = DashboardData(
            shards=[{"id": "X1", "name": "Example"}],
            ledger_by_id={
                "X1": {
                    "id": "X1",
                    "name": "Example",
                    "agent": "cheap",
                    "agent_model": "<script>model</script>",
                    "human_interventions": 0,
                    "guardrail_failures": 0,
                    "fix_commits": 0,
                    "defects_escaped": 0,
                    "notes": "<script>notes</script>",
                },
            },
            plans=[],
            generated_at="2026-09-24T00:00:00+00:00",
        )

        html = render_dashboard(data)

        self.assertIn("&lt;script&gt;model&lt;/script&gt;", html)
        self.assertIn("&lt;script&gt;notes&lt;/script&gt;", html)
        self.assertNotIn("<script>model</script>", html)
        self.assertNotIn("<script>notes</script>", html)

    def test_rendering_marks_missing_ledger_and_metric_states(self):
        data = DashboardData(
            shards=[
                {"id": "X1", "name": "Covered"},
                {"id": "X2", "name": "Uncovered"},
            ],
            ledger_by_id={
                "X1": {
                    "id": "X1",
                    "name": "Covered",
                    "agent": "cheap",
                    "human_interventions": 0,
                    "guardrail_failures": 2,
                    "fix_commits": 1,
                    "defects_escaped": 0,
                },
            },
            plans=[],
            generated_at="2026-09-24T00:00:00+00:00",
        )

        html = render_dashboard(data)

        self.assertIn("Missing ledger", html)
        self.assertIn(">—<", html)
        self.assertIn(">0<", html)

    def test_rendering_emits_no_inline_script_or_style(self):
        data = DashboardData(
            shards=[],
            ledger_by_id={},
            plans=[],
            generated_at="2026-09-24T00:00:00+00:00",
        )

        html = render_dashboard(data)

        self.assertNotIn("<script", html)
        self.assertNotIn("<style", html)


class SnapshotTests(unittest.TestCase):
    def test_check_reports_stale_snapshot(self):
        temporary, root = make_valid_root()
        self.addCleanup(temporary.cleanup)
        output = root / "docs/factory/dashboard.html"
        output.write_text("<html>stale</html>", encoding="utf-8")

        self.assertEqual(1, check_dashboard(root))

    def test_check_accepts_current_snapshot(self):
        temporary, root = make_valid_root()
        self.addCleanup(temporary.cleanup)

        update_dashboard(root)

        self.assertEqual(0, check_dashboard(root))

    def test_malformed_input_does_not_replace_snapshot(self):
        temporary, root = make_valid_root()
        self.addCleanup(temporary.cleanup)
        output = root / "docs/factory/dashboard.html"
        update_dashboard(root)
        existing = output.read_text(encoding="utf-8")
        (root / "shards.yaml").write_text("slices: [", encoding="utf-8")

        with self.assertRaises(DashboardError):
            update_dashboard(root)

        self.assertEqual(existing, output.read_text(encoding="utf-8"))

    def test_check_ignores_timestamp_only_difference(self):
        temporary, root = make_valid_root()
        self.addCleanup(temporary.cleanup)
        update_dashboard(root)
        output = root / "docs/factory/dashboard.html"
        document = output.read_text(encoding="utf-8")
        shifted = re.sub(
            r'(<time id="generated-at" datetime=")[^"]+(">)[^<]+(</time>)',
            r"\g<1>2099-12-31T23:59:59+00:00\g<2>2099-12-31 23:59 UTC\g<3>",
            document,
            count=1,
        )
        self.assertNotEqual(document, shifted)
        output.write_text(shifted, encoding="utf-8")

        self.assertEqual(0, check_dashboard(root))

    def test_check_does_not_ignore_other_time_elements(self):
        temporary, root = make_valid_root()
        self.addCleanup(temporary.cleanup)
        update_dashboard(root)
        output = root / "docs/factory/dashboard.html"
        document = output.read_text(encoding="utf-8")
        output.write_text(
            document.replace(
                "</body>",
                '<time datetime="2000-01-01T00:00:00+00:00">2000-01-01</time></body>',
            ),
            encoding="utf-8",
        )

        self.assertEqual(1, check_dashboard(root))


class CliModeTests(unittest.TestCase):
    def test_default_mode_writes_dashboard_and_returns_zero(self):
        temporary, root = make_valid_root()
        self.addCleanup(temporary.cleanup)

        self.assertEqual(0, main([], root=root))
        self.assertTrue((root / "docs/factory/dashboard.html").is_file())

    def test_check_mode_returns_zero_for_current_snapshot(self):
        temporary, root = make_valid_root()
        self.addCleanup(temporary.cleanup)
        self.assertEqual(0, main([], root=root))

        self.assertEqual(0, main(["--check"], root=root))

    def test_check_mode_returns_one_for_stale_snapshot(self):
        temporary, root = make_valid_root()
        self.addCleanup(temporary.cleanup)
        (root / "docs/factory/dashboard.html").write_text(
            "<html>stale</html>", encoding="utf-8"
        )

        self.assertEqual(1, main(["--check"], root=root))

    def test_dashboard_error_prints_to_stderr_and_returns_one(self):
        temporary, root = make_valid_root()
        self.addCleanup(temporary.cleanup)
        (root / "shards.yaml").write_text("slices: [", encoding="utf-8")
        stderr = io.StringIO()

        with redirect_stderr(stderr):
            result = main([], root=root)

        self.assertEqual(1, result)
        self.assertIn("invalid YAML", stderr.getvalue())

    def test_dashboard_error_returns_one_in_check_mode(self):
        temporary, root = make_valid_root()
        self.addCleanup(temporary.cleanup)
        (root / "shards.yaml").write_text("slices: [", encoding="utf-8")
        stderr = io.StringIO()

        with redirect_stderr(stderr):
            result = main(["--check"], root=root)

        self.assertEqual(1, result)
        self.assertIn("invalid YAML", stderr.getvalue())


class DebtLoadingTests(unittest.TestCase):
    def test_open_entries_parse_with_multiline_interest(self):
        temporary, root = make_valid_root()
        self.addCleanup(temporary.cleanup)
        _write_debt(
            root,
            _debt_document(
                open_section=(
                    "### TD-777 — `Alpha` open item\n"
                    "- **Where:** `path/one.cs`\n"
                    "  second where line\n"
                    "- **Interest:** first interest line\n"
                    "  wrapped interest line\n"
                    "- **Paydown:** fix it later"
                ),
                closed_section=(
                    "### TD-700 — Closed item\n"
                    "- **Closed 2026-09-01:** shipped."
                ),
                defect_section="Escaped defects go here.",
            ),
        )

        data = load_dashboard(root)

        self.assertEqual(1, len(data.debt.open_entries))
        entry = data.debt.open_entries[0]
        self.assertEqual("TD-777", entry.id)
        self.assertEqual("`Alpha` open item", entry.title)
        self.assertEqual("first interest line wrapped interest line", entry.interest)

    def test_open_entry_without_interest_bullet_has_empty_interest(self):
        temporary, root = make_valid_root()
        self.addCleanup(temporary.cleanup)
        _write_debt(
            root,
            _debt_document(
                open_section="### TD-778 — No interest line\n- **Where:** `path`",
                closed_section="",
                defect_section="",
            ),
        )

        data = load_dashboard(root)

        self.assertEqual("", data.debt.open_entries[0].interest)

    def test_closed_entries_are_counted(self):
        temporary, root = make_valid_root()
        self.addCleanup(temporary.cleanup)
        _write_debt(
            root,
            _debt_document(
                open_section="### TD-777 — Only open entry",
                closed_section=(
                    "### TD-700 — First closed\n"
                    "- **Closed 2026-09-01:** shipped.\n\n"
                    "### TD-701 — Second closed\n"
                    "- **Closed 2026-09-02:** shipped."
                ),
                defect_section="",
            ),
        )

        data = load_dashboard(root)

        self.assertEqual(2, data.debt.closed_count)
        self.assertEqual(1, len(data.debt.open_entries))

    def test_defect_entries_strip_markers_and_trailing_parenthetical(self):
        temporary, root = make_valid_root()
        self.addCleanup(temporary.cleanup)
        _write_debt(
            root,
            _debt_document(
                open_section="",
                closed_section="",
                defect_section=(
                    "Escaped defects are recorded here.\n\n"
                    "- **DL-42 — Defect title** (found Sep 2026 in review).\n"
                    "  - **Injection stage:** design."
                ),
            ),
        )

        data = load_dashboard(root)

        self.assertEqual(1, len(data.debt.defects))
        self.assertEqual("DL-42", data.debt.defects[0].id)
        self.assertEqual("Defect title", data.debt.defects[0].title)

    def test_wrapped_defect_title_is_joined_from_continuation_line(self):
        temporary, root = make_valid_root()
        self.addCleanup(temporary.cleanup)
        _write_debt(
            root,
            _debt_document(
                open_section="",
                closed_section="",
                defect_section=(
                    "- **DL-201 — title starts on the first line\n"
                    "  and finishes on the next** (found Sep 2026 during review).\n"
                    "  - **Injection stage:** design."
                ),
            ),
        )

        data = load_dashboard(root)

        self.assertEqual(1, len(data.debt.defects))
        self.assertEqual("DL-201", data.debt.defects[0].id)
        self.assertEqual(
            "title starts on the first line and finishes on the next",
            data.debt.defects[0].title,
        )

    def test_missing_register_file_raises_dashboard_error(self):
        temporary, root = make_valid_root()
        self.addCleanup(temporary.cleanup)
        (root / "docs/factory/technical-debt.md").unlink()

        with self.assertRaises(DashboardError):
            load_dashboard(root)

    def test_missing_section_heading_raises_dashboard_error(self):
        temporary, root = make_valid_root()
        self.addCleanup(temporary.cleanup)
        _write_debt(
            root,
            "# Technical Debt Register\n\n## Open entries\n\n## Closed entries\n",
        )

        with self.assertRaises(DashboardError):
            load_dashboard(root)

    def test_entry_ids_are_discovered_not_hardcoded(self):
        temporary, root = make_valid_root()
        self.addCleanup(temporary.cleanup)
        _write_debt(
            root,
            _debt_document(
                open_section="### TD-4242 — Novel open id",
                closed_section="### TD-0007 — Novel closed id",
                defect_section="- **DL-31337 — Novel defect id** (found Sep 2026).",
            ),
        )

        data = load_dashboard(root)

        self.assertEqual(["TD-4242"], [e.id for e in data.debt.open_entries])
        self.assertEqual(1, data.debt.closed_count)
        self.assertEqual(["DL-31337"], [e.id for e in data.debt.defects])


class DebtRenderingTests(unittest.TestCase):
    def test_debt_section_sits_between_shards_and_plans(self):
        document = _render_with_debt(
            DebtRegister(
                open_entries=[DebtEntry("TD-001", "Debt one", "interest one")],
                closed_count=1,
                defects=[DefectEntry("DL-001", "Defect one")],
            )
        )

        self.assertLess(document.index(">Shards</h2>"), document.index(">Technical Debt</h2>"))
        self.assertLess(document.index(">Technical Debt</h2>"), document.index(">Plans</h2>"))

    def test_open_and_defect_rows_render_with_counts(self):
        document = _render_with_debt(
            DebtRegister(
                open_entries=[
                    DebtEntry("TD-001", "First debt", "first interest"),
                    DebtEntry("TD-002", "Second debt", ""),
                ],
                closed_count=3,
                defects=[DefectEntry("DL-001", "First defect")],
            )
        )

        self.assertIn("First debt", document)
        self.assertIn("first interest", document)
        self.assertIn("Second debt", document)
        self.assertIn("First defect", document)
        self.assertEqual(2, document.count("<td>TD-"))
        self.assertEqual(1, document.count("<td>DL-"))
        self.assertIn("3 closed entries", document)
        self.assertIn('<a href="technical-debt.md">technical-debt.md</a>', document)

    def test_debt_values_are_escaped(self):
        document = _render_with_debt(
            DebtRegister(
                open_entries=[
                    DebtEntry(
                        "TD-001",
                        "<script>alert(1)</script>",
                        "<script>interest</script>",
                    )
                ],
                closed_count=0,
                defects=[DefectEntry("DL-001", "<script>defect</script>")],
            )
        )

        self.assertIn("&lt;script&gt;alert(1)&lt;/script&gt;", document)
        self.assertIn("&lt;script&gt;interest&lt;/script&gt;", document)
        self.assertIn("&lt;script&gt;defect&lt;/script&gt;", document)
        self.assertNotIn("<script>alert(1)</script>", document)

    def test_summary_cards_show_debt_counts(self):
        document = _render_with_debt(
            DebtRegister(
                open_entries=[
                    DebtEntry(f"TD-{number:03d}", f"Debt {number}", "")
                    for number in range(4)
                ],
                closed_count=1,
                defects=[DefectEntry("DL-001", "One"), DefectEntry("DL-002", "Two")],
            )
        )

        self.assertIn(">Open debt</p>", document)
        self.assertIn('class="card-title fs-4 mb-1">4</p>', document)
        self.assertIn(">1 closed</p>", document)
        self.assertIn(">Defect log entries</p>", document)
        self.assertIn('class="card-title fs-4 mb-1">2</p>', document)

    def test_empty_debt_state_renders_placeholder_rows(self):
        document = _render_with_debt(DebtRegister(open_entries=[], closed_count=0, defects=[]))

        self.assertIn(
            '<tr><td colspan="3" class="text-muted">No open technical debt.</td></tr>',
            document,
        )
        self.assertIn(
            '<tr><td colspan="2" class="text-muted">No defects logged.</td></tr>',
            document,
        )

    def test_debt_rendering_emits_no_inline_script_or_style(self):
        document = _render_with_debt(
            DebtRegister(
                open_entries=[
                    DebtEntry("TD-001", "<style>body{}</style>", "<script>x</script>")
                ],
                closed_count=0,
                defects=[DefectEntry("DL-001", "<script>y</script>")],
            )
        )

        self.assertNotIn("<script", document)
        self.assertNotIn("<style", document)


if __name__ == "__main__":
    unittest.main()
