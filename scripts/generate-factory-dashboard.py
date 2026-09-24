from __future__ import annotations

import argparse
import html
import math
import os
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


STATUS_RE = re.compile(
    r"^(?:\*\*)?status(?:\*\*:|:\*\*|:)\s*(.+?)\s*$", re.IGNORECASE
)
PROGRESS_RE = re.compile(
    r"^(?:\*\*)?(?:overall\s+status|plan\s+status|status)(?:\*\*:|:\*\*|:)\s*(.+?)\s*$",
    re.IGNORECASE,
)
COMPLETION_RE = re.compile(r"\b(done|complete|completed|shipped)\b", re.IGNORECASE)
NEGATIVE_STATUS_RE = re.compile(
    r"\b(?:not|incomplete|pending|blocked|open|unfinished|in\s+progress)\b",
    re.IGNORECASE,
)
CHILD_PROGRESS_RE = re.compile(
    r"\b(?:child|item|task|phase|story|subtask)\b", re.IGNORECASE
)
CHECKED_RE = re.compile(r"(?m)^\s*- \[x\]\s+", re.IGNORECASE)
UNCHECKED_RE = re.compile(r"(?m)^\s*- \[ \]\s+")
H1_RE = re.compile(r"(?m)^#\s+(.+?)\s*$")

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
    "notes",
)
REQUIRED_INTEGER_FIELDS = (
    "human_interventions",
    "guardrail_failures",
    "fix_commits",
    "defects_escaped",
)
OPTIONAL_INTEGER_FIELDS = (
    "tokens_input",
    "tokens_output",
    "tokens_reasoning",
    "tokens_cache_read",
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


def _fence_marker(line: str) -> tuple[str, int] | None:
    stripped = line.lstrip()
    for marker in ("`", "~"):
        if stripped.startswith(marker * 3):
            length = len(stripped) - len(stripped.lstrip(marker))
            return marker, length
    return None


def _is_fence_close(
    line: str, fence_marker: str, fence_length: int
) -> bool:
    stripped = line.lstrip()
    if not stripped.startswith(fence_marker * fence_length):
        return False
    length = len(stripped) - len(stripped.lstrip(fence_marker))
    return length >= fence_length and not stripped[length:].strip()


def _outside_fence_lines(text: str):
    fence: tuple[str, int] | None = None
    for line in text.splitlines():
        stripped = line.lstrip()
        if fence is not None:
            if _is_fence_close(stripped, *fence):
                fence = None
            continue
        marker = _fence_marker(stripped)
        if marker is not None:
            fence = marker
            continue
        yield line


def _metadata_lines(text: str):
    for line in _outside_fence_lines(text):
        if line == line.lstrip():
            yield line


def _metadata_value(text: str, pattern: re.Pattern[str]) -> str | None:
    for line in _metadata_lines(text):
        if line.startswith("- ") or line.startswith("* "):
            line = line[2:]
        match = pattern.fullmatch(line)
        if match:
            return match.group(1).strip()
    return None


def _is_negative_status(value: str) -> bool:
    return bool(NEGATIVE_STATUS_RE.search(value))


def _is_positive_completion(value: str) -> bool:
    return not _is_negative_status(value) and bool(COMPLETION_RE.search(value))


def _is_positive_progress(value: str) -> bool:
    return not CHILD_PROGRESS_RE.search(value) and _is_positive_completion(value)


def classify_plan(path: str, text: str, progress_text: str = "") -> PlanState:
    title_match = H1_RE.search(text)
    title = title_match.group(1).strip() if title_match else Path(path).stem
    evidence_text = "\n".join(_outside_fence_lines(text))
    checked_boxes = len(CHECKED_RE.findall(evidence_text))
    unchecked_boxes = len(UNCHECKED_RE.findall(evidence_text))

    status_value = _metadata_value(text, STATUS_RE)
    status_complete = status_value is not None and _is_positive_completion(status_value)
    status_incomplete = status_value is not None and _is_negative_status(status_value)
    progress_value = _metadata_value(text, PROGRESS_RE) or _metadata_value(
        progress_text, PROGRESS_RE
    )
    progress_complete = progress_value is not None and _is_positive_progress(
        progress_value
    )

    if status_complete:
        status, evidence = "complete", "plan status"
    elif status_incomplete:
        status, evidence = "incomplete", "plan status"
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


GENERATED_AT_RE = re.compile(
    r'(<time id="generated-at" datetime=")[^"]+(">)[^<]+(</time>)'
)

STATUS_ORDER = {"incomplete": 0, "needs-review": 1, "complete": 2}
STATUS_BADGES = {
    "complete": ("bg-success", "Complete"),
    "incomplete": ("bg-warning text-dark", "Incomplete"),
    "needs-review": ("bg-secondary", "Needs review"),
}
PLAN_PATH_PREFIX = "docs/"

SHARD_COLUMNS = (
    "Shard ID",
    "Name",
    "Ledger",
    "Agent",
    "Model",
    "Interventions",
    "Guardrail failures",
    "Fix commits",
    "Escaped defects",
    "Cost",
    "Notes",
)
PLAN_COLUMNS = (
    "Plan",
    "Status",
    "Evidence",
    "Checkboxes",
    "Stale evidence",
)


def display_metric(value: Any) -> str:
    return "—" if value is None else html.escape(str(value), quote=True)


def format_cost(value: Any) -> str:
    return "—" if value is None else f"${float(value):.2f}"


def _status_badge(status: str) -> str:
    css_class, label = STATUS_BADGES.get(status, ("bg-secondary", status))
    return f'<span class="badge {css_class}">{html.escape(label, quote=True)}</span>'


def _plan_href(path: str) -> str:
    if path.startswith(PLAN_PATH_PREFIX):
        relative = "../" + path[len(PLAN_PATH_PREFIX):]
    else:
        relative = "../../" + path
    return html.escape(relative, quote=True)


def _summary_card(title: str, value: str, detail: str = "") -> str:
    detail_html = ""
    if detail:
        detail_html = (
            f'<p class="card-text small text-muted mb-0">'
            f"{html.escape(detail, quote=True)}</p>"
        )
    return (
        '<div class="col-6 col-md-4 col-xl-3">'
        '<div class="card h-100"><div class="card-body py-3">'
        f'<p class="card-text small text-muted mb-1">'
        f"{html.escape(title, quote=True)}</p>"
        f'<p class="card-title fs-4 mb-1">{html.escape(value, quote=True)}</p>'
        f"{detail_html}"
        "</div></div></div>"
    )


def _notes_cell(notes: Any) -> str:
    if notes is None or not str(notes).strip():
        return "—"
    return (
        "<details><summary>Notes</summary>"
        f'<p class="mb-0">{display_metric(notes)}</p></details>'
    )


def _shard_row(shard: dict[str, Any], entry: dict[str, Any] | None) -> str:
    cells = [
        f"<td>{display_metric(shard.get('id'))}</td>",
        f"<td>{display_metric(shard.get('name'))}</td>",
    ]
    if entry is None:
        cells.append('<td><span class="badge bg-danger">Missing ledger</span></td>')
        cells.extend("<td>—</td>" for _ in range(8))
    else:
        cells.append('<td><span class="badge bg-success">Recorded</span></td>')
        cells.append(f"<td>{display_metric(entry.get('agent'))}</td>")
        cells.append(f"<td>{display_metric(entry.get('agent_model'))}</td>")
        cells.append(f"<td>{display_metric(entry.get('human_interventions'))}</td>")
        cells.append(f"<td>{display_metric(entry.get('guardrail_failures'))}</td>")
        cells.append(f"<td>{display_metric(entry.get('fix_commits'))}</td>")
        cells.append(f"<td>{display_metric(entry.get('defects_escaped'))}</td>")
        cells.append(f"<td>{format_cost(entry.get('cost_usd'))}</td>")
        cells.append(f"<td>{_notes_cell(entry.get('notes'))}</td>")
    return "<tr>" + "".join(cells) + "</tr>"


def _plan_row(plan: PlanState) -> str:
    stale_text = (
        f"{plan.unchecked_boxes} unchecked after completion evidence"
        if plan.stale_checkboxes
        else "—"
    )
    return (
        "<tr>"
        f'<td><a href="{_plan_href(plan.path)}">{display_metric(plan.title)}</a>'
        f'<div class="small text-muted">{display_metric(plan.path)}</div></td>'
        f"<td>{_status_badge(plan.status)}</td>"
        f"<td>{display_metric(plan.evidence)}</td>"
        f"<td>{plan.checked_boxes} checked / {plan.unchecked_boxes} unchecked</td>"
        f"<td>{display_metric(stale_text)}</td>"
        "</tr>"
    )


def render_dashboard(data: DashboardData) -> str:
    ledger = data.ledger_by_id
    missing_ids = [shard["id"] for shard in data.shards if shard["id"] not in ledger]
    covered_count = len(data.shards) - len(missing_ids)
    interventions = sum(
        int(ledger[shard["id"]].get("human_interventions") or 0)
        for shard in data.shards
        if shard["id"] in ledger
    )
    guardrail_failures = sum(
        int(ledger[shard["id"]].get("guardrail_failures") or 0)
        for shard in data.shards
        if shard["id"] in ledger
    )
    fix_commits = sum(
        int(ledger[shard["id"]].get("fix_commits") or 0)
        for shard in data.shards
        if shard["id"] in ledger
    )
    escaped_defects = sum(
        int(ledger[shard["id"]].get("defects_escaped") or 0)
        for shard in data.shards
        if shard["id"] in ledger
    )
    cheap_records = sum(
        1 for entry in ledger.values() if entry.get("agent") == "cheap"
    )
    cost_values = [
        float(entry["cost_usd"])
        for entry in ledger.values()
        if entry.get("cost_usd") is not None
    ]
    recorded_spend = format_cost(sum(cost_values)) if cost_values else "—"
    incomplete_plans = sum(1 for plan in data.plans if plan.status == "incomplete")
    review_plans = sum(1 for plan in data.plans if plan.status == "needs-review")
    ordered_plans = sorted(
        data.plans,
        key=lambda plan: (STATUS_ORDER.get(plan.status, len(STATUS_ORDER)), plan.path),
    )
    timestamp = display_metric(data.generated_at)

    cards = (
        _summary_card("Declared shards", str(len(data.shards)))
        + _summary_card(
            "Ledger records", str(covered_count), f"of {len(data.shards)} declared"
        )
        + _summary_card(
            "Missing ledger IDs", str(len(missing_ids)), ", ".join(missing_ids) or "none"
        )
        + _summary_card("Human interventions", str(interventions))
        + _summary_card("Guardrail failures", str(guardrail_failures))
        + _summary_card("Fix commits", str(fix_commits))
        + _summary_card("Escaped defects", str(escaped_defects))
        + _summary_card("Cheap-agent records", str(cheap_records))
        + _summary_card("Recorded spend", recorded_spend)
        + _summary_card("Incomplete plans", str(incomplete_plans))
        + _summary_card("Needs-review plans", str(review_plans))
    )

    shard_head = "".join(
        f'<th scope="col">{html.escape(column, quote=True)}</th>'
        for column in SHARD_COLUMNS
    )
    plan_head = "".join(
        f'<th scope="col">{html.escape(column, quote=True)}</th>'
        for column in PLAN_COLUMNS
    )
    shard_rows = "".join(
        _shard_row(shard, ledger.get(shard["id"])) for shard in data.shards
    ) or '<tr><td colspan="11" class="text-muted">No shards declared.</td></tr>'
    plan_rows = "".join(_plan_row(plan) for plan in ordered_plans) or (
        '<tr><td colspan="5" class="text-muted">No plan files found.</td></tr>'
    )

    parts = [
        "<!DOCTYPE html>",
        '<html lang="en">',
        "<head>",
        '<meta charset="utf-8">',
        '<meta name="viewport" content="width=device-width, initial-scale=1">',
        "<title>VitaTrack Factory Dashboard</title>",
        '<link href="https://cdn.jsdelivr.net/npm/bootstrap@5.3.3/dist/css/'
        'bootstrap.min.css" rel="stylesheet">',
        "</head>",
        '<body class="bg-light">',
        '<div class="container py-4">',
        '<header class="mb-4">',
        '<h1 class="h3 mb-1">VitaTrack Factory Dashboard</h1>',
        f'<p class="text-muted mb-2">Generated '
        f'<time id="generated-at" datetime="{timestamp}">{timestamp}</time></p>',
        '<p class="mb-1">Sources: '
        '<a href="../../shards.yaml">shards.yaml</a> · '
        '<a href="shard-metrics.yaml">shard-metrics.yaml</a> · '
        '<a href="../plans/">docs/plans</a> · '
        '<a href="../superpowers/plans/">docs/superpowers/plans</a></p>',
        '<p class="mb-0"><code>python3 scripts/generate-factory-dashboard.py</code></p>',
        "</header>",
        f'<div class="row g-3 mb-4">{cards}</div>',
        '<section class="mb-4">',
        '<h2 class="h5 mb-3">Shards</h2>',
        '<div class="table-responsive">',
        '<table class="table table-striped table-bordered bg-white align-middle mb-0">',
        f"<thead><tr>{shard_head}</tr></thead>",
        f"<tbody>{shard_rows}</tbody>",
        "</table>",
        "</div>",
        "</section>",
        '<section class="mb-4">',
        '<h2 class="h5 mb-3">Plans</h2>',
        '<div class="table-responsive">',
        '<table class="table table-striped table-bordered bg-white align-middle mb-0">',
        f"<thead><tr>{plan_head}</tr></thead>",
        f"<tbody>{plan_rows}</tbody>",
        "</table>",
        "</div>",
        "</section>",
        "</div>",
        "</body>",
        "</html>",
    ]
    return "\n".join(parts)


def normalized_for_check(document: str) -> str:
    return GENERATED_AT_RE.sub(r"\1normalized\2normalized\3", document)


def write_dashboard(root: Path, document: str) -> None:
    output = root / "docs/factory/dashboard.html"
    temporary = output.with_name(output.name + ".tmp")
    try:
        temporary.write_text(document, encoding="utf-8")
        os.replace(temporary, output)
    finally:
        temporary.unlink(missing_ok=True)


def generate_dashboard(root: Path) -> str:
    return render_dashboard(load_dashboard(root))


def update_dashboard(root: Path) -> int:
    write_dashboard(root, generate_dashboard(root))
    return 0


def check_dashboard(root: Path) -> int:
    output = root / "docs/factory/dashboard.html"
    document = generate_dashboard(root)
    if not output.exists() or normalized_for_check(
        output.read_text(encoding="utf-8")
    ) != normalized_for_check(document):
        return 1
    return 0


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


def _validate_ledger_entry(
    entry: dict[str, Any], source: Path, entry_id: str
) -> None:
    for field in ("name", "agent"):
        value = entry[field]
        if not isinstance(value, str) or not value.strip():
            raise DashboardError(
                f"{source}: ledger entry '{entry_id}' field '{field}' must be a non-empty string"
            )

    for field in REQUIRED_INTEGER_FIELDS:
        value = entry[field]
        if isinstance(value, bool) or not isinstance(value, int) or value < 0:
            raise DashboardError(
                f"{source}: ledger entry '{entry_id}' field '{field}' must be a non-negative integer"
            )

    for field in OPTIONAL_INTEGER_FIELDS:
        if field in entry and entry[field] is not None:
            value = entry[field]
            if isinstance(value, bool) or not isinstance(value, int) or value < 0:
                raise DashboardError(
                    f"{source}: ledger entry '{entry_id}' field '{field}' must be a non-negative integer or null"
                )

    for field in ("agent_model", "notes"):
        if field in entry and entry[field] is not None and not isinstance(entry[field], str):
            raise DashboardError(
                f"{source}: ledger entry '{entry_id}' field '{field}' must be a string or null"
            )

    cost = entry.get("cost_usd")
    if cost is not None:
        if isinstance(cost, bool) or not isinstance(cost, (int, float)):
            raise DashboardError(
                f"{source}: ledger entry '{entry_id}' field 'cost_usd' must be a finite number or null"
            )
        try:
            finite_cost = math.isfinite(cost)
        except (OverflowError, TypeError, ValueError):
            finite_cost = False
        if not finite_cost:
            raise DashboardError(
                f"{source}: ledger entry '{entry_id}' field 'cost_usd' must be a finite number or null"
            )


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
        _validate_ledger_entry(entry, source, entry_id)
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


def main(argv: list[str] | None = None, root: Path | None = None) -> int:
    parser = argparse.ArgumentParser(
        description="Generate the VitaTrack factory dashboard snapshot."
    )
    parser.add_argument(
        "--check",
        action="store_true",
        help="exit 1 when the committed dashboard differs from current sources",
    )
    args = parser.parse_args(argv)
    if root is None:
        root = Path(__file__).resolve().parents[1]
    try:
        if args.check:
            return check_dashboard(root)
        return update_dashboard(root)
    except DashboardError as exc:
        print(str(exc), file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
