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


STATUS_RE = re.compile(r"(?im)^\s*(?:[-*]\s*)?(?:\*\*)?status(?:\*\*)?\s*:\s*(.+?)\s*$")
PROGRESS_RE = re.compile(
    r"(?im)^\s*(?:[-*]\s*)?(?:\*\*)?(?:overall\s+status|plan\s+status|status)(?:\*\*)?\s*:\s*(?:plan\s+)?(?:is\s+)?(complete|completed|done|shipped)\b"
)
CHECKED_RE = re.compile(r"(?m)^\s*- \[x\]\s+", re.IGNORECASE)
UNCHECKED_RE = re.compile(r"(?m)^\s*- \[ \]\s+")
H1_RE = re.compile(r"(?m)^#\s+(.+?)\s*$")

COMPLETION_WORDS = ("done", "complete", "completed", "shipped")
REQUIRED_LEDGER_FIELDS = (
    "id",
    "name",
    "agent",
    "human_interventions",
    "guardrail_failures",
    "fix_commits",
    "defects_escaped",
)
OPTIONAL_METRIC_FIELDS = (
    "agent_model",
    "tokens_input",
    "tokens_output",
    "tokens_reasoning",
    "tokens_cache_read",
    "cost_usd",
)
PLAN_DIRECTORIES = ("docs/plans", "docs/superpowers/plans")

NO_EVIDENCE = "no status, progress, or checkbox evidence"


def read_yaml(path: Path) -> dict[str, Any]:
    if not path.is_file():
        raise DashboardError(f"{path}: file not found")
    try:
        loaded = yaml.safe_load(path.read_text(encoding="utf-8"))
    except yaml.YAMLError as exc:
        raise DashboardError(f"{path}: invalid YAML: {exc}") from exc
    if not isinstance(loaded, dict):
        raise DashboardError(f"{path}: expected a mapping at the top level")
    return loaded


def classify_plan(path: str, text: str, progress_text: str = "") -> PlanState:
    title_match = H1_RE.search(text)
    title = title_match.group(1).strip() if title_match else Path(path).stem
    checked_boxes = len(CHECKED_RE.findall(text))
    unchecked_boxes = len(UNCHECKED_RE.findall(text))

    status_match = STATUS_RE.search(text)
    status_complete = bool(status_match) and any(
        word in status_match.group(1).lower() for word in COMPLETION_WORDS
    )
    progress_complete = bool(PROGRESS_RE.search(text)) or bool(
        PROGRESS_RE.search(progress_text)
    )

    if status_complete:
        status, evidence = "complete", "plan status"
    elif progress_complete:
        status, evidence = "complete", "progress"
    elif unchecked_boxes:
        status, evidence = "incomplete", "checkboxes"
    elif checked_boxes:
        status, evidence = "complete", "checkboxes"
    else:
        status, evidence = "needs-review", NO_EVIDENCE

    stale_checkboxes = (status_complete or progress_complete) and unchecked_boxes > 0
    return PlanState(
        path=path,
        title=title,
        status=status,
        evidence=evidence,
        checked_boxes=checked_boxes,
        unchecked_boxes=unchecked_boxes,
        stale_checkboxes=stale_checkboxes,
    )


def load_dashboard(root: Path) -> DashboardData:
    shards = _load_shards(root)
    shard_ids = {shard["id"] for shard in shards}
    ledger_by_id = _load_ledger(root, shard_ids)
    plans = [_classify_plan_file(root, plan) for plan in _collect_plan_paths(root)]
    generated_at = datetime.now(timezone.utc).isoformat()
    return DashboardData(
        shards=shards,
        ledger_by_id=ledger_by_id,
        plans=plans,
        generated_at=generated_at,
    )


def render_dashboard(data: DashboardData) -> str:
    raise NotImplementedError("rendering lands in Task 3")


def generate_dashboard(root: Path) -> str:
    raise NotImplementedError("generation lands in Task 3")


def update_dashboard(root: Path) -> None:
    raise NotImplementedError("snapshot updates land in Task 3")


def check_dashboard(root: Path) -> int:
    raise NotImplementedError("check mode lands in Task 3")


def _mapping_list(
    document: dict[str, Any], key: str, source: Path
) -> list[dict[str, Any]]:
    value = document.get(key)
    if not isinstance(value, list) or not all(
        isinstance(item, dict) for item in value
    ):
        raise DashboardError(f"{source}: '{key}' must be a list of mappings")
    return value


def _load_shards(root: Path) -> list[dict[str, Any]]:
    source = root / "shards.yaml"
    slices = _mapping_list(read_yaml(source), "slices", source)
    seen_ids: set[str] = set()
    seen_names: set[str] = set()
    for shard in slices:
        shard_id = shard.get("id")
        name = shard.get("name")
        if not isinstance(shard_id, str) or not shard_id:
            raise DashboardError(f"{source}: every slice needs a non-empty string 'id'")
        if not isinstance(name, str) or not name:
            raise DashboardError(
                f"{source}: slice '{shard_id}' needs a non-empty string 'name'"
            )
        if shard_id in seen_ids:
            raise DashboardError(f"{source}: duplicate slice id '{shard_id}'")
        if name in seen_names:
            raise DashboardError(f"{source}: duplicate slice name '{name}'")
        seen_ids.add(shard_id)
        seen_names.add(name)
    return slices


def _load_ledger(
    root: Path, shard_ids: set[str]
) -> dict[str, dict[str, Any]]:
    source = root / "docs/factory/shard-metrics.yaml"
    entries = _mapping_list(read_yaml(source), "shards", source)
    ledger: dict[str, dict[str, Any]] = {}
    for raw_entry in entries:
        entry = dict(raw_entry)
        entry_id = entry.get("id")
        if not isinstance(entry_id, str) or not entry_id:
            raise DashboardError(
                f"{source}: every ledger entry needs a non-empty string 'id'"
            )
        if entry_id in ledger:
            raise DashboardError(f"{source}: duplicate ledger id '{entry_id}'")
        if entry_id not in shard_ids:
            raise DashboardError(
                f"{source}: ledger id '{entry_id}' is not declared in shards.yaml"
            )
        missing = [
            field for field in REQUIRED_LEDGER_FIELDS if field not in entry
        ]
        if missing:
            raise DashboardError(
                f"{source}: ledger entry '{entry_id}' missing required field(s): "
                + ", ".join(missing)
            )
        for field in OPTIONAL_METRIC_FIELDS:
            entry.setdefault(field, None)
        ledger[entry_id] = entry
    return ledger


def _collect_plan_paths(root: Path) -> list[Path]:
    plans: list[Path] = []
    for directory in PLAN_DIRECTORIES:
        base = root / directory
        if base.is_dir():
            plans.extend(base.rglob("*.md"))
    return sorted(plans, key=lambda plan: plan.relative_to(root).as_posix())


def _classify_plan_file(root: Path, plan_path: Path) -> PlanState:
    text = plan_path.read_text(encoding="utf-8")
    relative = plan_path.relative_to(root).as_posix()
    progress_path = root / ".superpowers" / "sdd" / plan_path.stem / "progress.md"
    progress_text = (
        progress_path.read_text(encoding="utf-8") if progress_path.is_file() else ""
    )
    return classify_plan(relative, text, progress_text)
