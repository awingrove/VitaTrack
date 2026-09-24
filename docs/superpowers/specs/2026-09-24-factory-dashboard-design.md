# Factory Dashboard — Design

Date: 2026-09-24

## Goal

Provide a repository-local HTML snapshot for reviewing agentic factory state:
shard ledger coverage, intervention metrics, and plans that remain incomplete or
need review. Dashboard must open directly from disk and must not add runtime
coupling to VitaTrack.Web.

## Approved approach

Add a Python 3 generator using PyYAML. It reads existing factory artifacts and
writes a committed static page at `docs/factory/dashboard.html`.

The generator is deliberately separate from the ASP.NET application. It does not
run builds, tests, network calls, or database queries. Regeneration is an explicit
local command, so the committed page is a reproducible snapshot rather than a
runtime dashboard.

## Files

- Create: `scripts/generate-factory-dashboard.py`
- Create: `scripts/test-generate-factory-dashboard.py`
- Create: `docs/factory/dashboard.html`
- Modify: `docs/factory/metrics.md` with generation and status-rule documentation

No ASP.NET controller, route, view, database, story-map, shard-manifest, or
`DESIGN.md` changes are required. The dashboard is a repository artifact, not a
new VitaTrack application surface.

## Data sources

- `shards.yaml`: declared shard IDs, names, and manifest paths.
- `docs/factory/shard-metrics.yaml`: one ledger record per shipped shard and its
  intervention, usage, and cost fields.
- `docs/plans/**/*.md`: current factory plans.
- `docs/superpowers/plans/**/*.md`: all legacy plan Markdown files.
- Companion progress evidence under `.superpowers/sdd/**/progress.md` when a plan
  has an associated SDD progress record.

The generator scans all Markdown plans under both requested plan directories. It
uses source-relative paths in links and escapes all rendered values.

## Plan status rules

Status precedence is deliberate because existing plans contain stale checkboxes
after progress logs or explicit status lines have recorded completion:

1. Explicit plan status containing `done`, `complete`, `completed`, or `shipped`.
2. Explicit progress evidence that says the plan itself is complete or shipped;
   references to completed child phases or individual rollout items do not
   complete the parent plan.
3. Checkboxes: all checked means complete; any unchecked means incomplete.
4. No usable evidence means needs review.

When explicit completion evidence conflicts with unchecked boxes, explicit
completion wins. The page shows the checkbox count as stale evidence, so the
conflict remains visible without reopening a completed plan. A plan with no
authoritative completion signal is shown as `Needs review`, not silently treated
as incomplete.

The generator records the evidence source and remaining or stale checkbox counts
for each plan.

## Shard and metrics rules

A shard is considered to have a ledger record only when its ID appears in the
metrics ledger. Missing entries are shown as `Missing ledger`; they are not
inferred to be shipped. Optional metrics remain `—` when absent. Zero remains a
real value when explicitly recorded.

The page summarizes:

- Declared shard count and ledger coverage.
- Missing ledger IDs.
- Total human interventions, guardrail failures, fix commits, and escaped defects.
- Recorded spend and cheap-agent record count.
- Incomplete and needs-review plan counts.

The ledger table shows ID, name, agent, intervention/rework fields, optional model,
and recorded cost. Source notes remain available in the generated page's source
or detail area rather than being discarded during normalization.

## Page layout and behavior

Use the approved summary-plus-queues layout with stock Bootstrap 5:

- Header: page title, generated timestamp, source links, and regeneration command.
- Summary cards: declared shards, ledger records, missing ledger records, total
  interventions, guardrail failures, recorded spend, and incomplete plans.
- Shard table: ID/name, ledger coverage, agent, interventions, guardrail failures,
  fix commits, escaped defects, and cost.
- Plan queue: incomplete plans first, then needs-review plans; each row shows title,
  status, evidence, checkbox counts, and source link.

Status badges use existing semantic roles: `Complete` success, `Incomplete`
warning, `Needs review` secondary, and `Missing ledger` danger. The page has no
JavaScript and uses only Bootstrap's CDN stylesheet. It remains useful without
interactivity and opens from `file://`.

## Generator behavior and failure handling

The generator parses and normalizes all sources before writing output. Missing
required source files, invalid YAML, invalid manifest/ledger shape, or unexpected
source-directory failures return non-zero and leave the existing dashboard
untouched. HTML is assembled in memory and written only after successful
processing.

The `--check` mode regenerates the snapshot in memory and exits non-zero when the
committed HTML differs from current sources. The generated timestamp is volatile,
so `--check` normalizes only that value before comparison. This supports CI or a
pre-commit check without rewriting files.

## Testing

`scripts/test-generate-factory-dashboard.py` uses Python's standard `unittest`
framework and temporary fixtures. It covers:

- Complete, incomplete, and needs-review plan classification.
- Explicit status overriding stale unchecked boxes.
- Missing ledger IDs and optional metric values.
- HTML escaping for plan names, model names, and notes.
- Malformed-input failure with no partial dashboard replacement.
- `--check` success and stale-output failure.

Final verification runs generator tests, generator `--check`, `dotnet test`, and
`./format-check.sh`.

## Success criteria

- A fresh checkout contains a directly openable `docs/factory/dashboard.html`.
- A regeneration reflects current shard metrics and every plan Markdown file.
- Explicit completion evidence is authoritative over stale checkboxes.
- Missing or ambiguous state is visible rather than guessed.
- No application runtime, database, or generator network dependency is introduced;
  Bootstrap CDN access is only needed for page styling.
