import importlib.util
import re
import sys
import tempfile
import unittest
from pathlib import Path

_GENERATOR_PATH = Path(__file__).resolve().with_name("generate-factory-dashboard.py")


def _load_generator():
    """Load scripts/generate-factory-dashboard.py under its underscore module name."""
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
PlanState = _require(_generator, "PlanState")
check_dashboard = _require(_generator, "check_dashboard")
classify_plan = _require(_generator, "classify_plan")
load_dashboard = _require(_generator, "load_dashboard")
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
        "shards:\n  - id: X1\n    name: Example\n    agent: human\n"
        "    human_interventions: 0\n    guardrail_failures: 0\n"
        "    fix_commits: 0\n    defects_escaped: 0\n",
        encoding="utf-8",
    )
    (root / "docs/plans/example.md").write_text("# Example\n", encoding="utf-8")
    return temporary, root


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
            r'(<time datetime=")[^"]+(">)[^<]+(</time>)',
            r"\g<1>2099-12-31T23:59:59+00:00\g<2>2099-12-31 23:59 UTC\g<3>",
            document,
            count=1,
        )
        self.assertNotEqual(document, shifted)
        output.write_text(shifted, encoding="utf-8")

        self.assertEqual(0, check_dashboard(root))


if __name__ == "__main__":
    unittest.main()
