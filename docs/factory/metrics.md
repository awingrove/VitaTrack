# Shard Metrics & Intervention Ledger

The factory's claim to prove: an agent, given the shard recipe (`new-shard.md`) and the
guardrails, delivers a correct slice with **minimal human touch**. So we measure touch and
rework — not wall-clock hours, which are meaningless across models and sessions.

## Target model class

The productivity proof targets **≤ $1 per 1M token models** (GLM-5.3-Flash, Hy3 class).
This is a benchmark target, not a rule: the factory does not refuse to run other models.
A shard only counts toward the proof when its ledger entry records `agent` in that class.

## What we measure (per shard)

| Field | Meaning |
|---|---|
| `agent` | Model class that built it (`frontier` / `cheap` / `human`). Only `cheap` entries count toward the productivity proof. |
| `human_interventions` | Every stop that needed a human: design-review prompts, corrections, unblocks, review findings that forced changes. **The headline number.** `0` = fully autonomous. |
| `guardrail_failures` | Red gate cycles (arch/format/build/e2e failures before green). Loud failures are the design working; a high count means the recipe under-contextualizes the shard. |
| `fix_commits` | Commits after the first "done" claim. Measures verification honesty. |
| `defects_escaped` | Defects found after merge (from the defect log in `technical-debt.md`). |
| `agent_model` (optional) | Exact `provider/model` of the executor session, e.g. `opencode-go/glm-5.3-flash`. |
| `tokens_input` / `tokens_output` / `tokens_reasoning` / `tokens_cache_read` (optional) | Exact token usage of the executor session. `tokens_reasoning` is `0` when the model does not expose thinking tokens. |
| `cost_usd` (optional) | Dollar spend reported by opencode for the session. `0` on free-tier models is a real measurement, not an omission — record it as `0`, not blank. |

### How to extract usage numbers (opencode)

Dispatching the executor session (so the numbers exist at all): define the executor
as a subagent in `.opencode/agents/<name>.md` with `mode: subagent`, the pinned
`model: <provider>/<model-id>`, and `permission: { edit: allow, bash: allow }` (see
`.opencode/agents/mimo-flash.md`). Dispatch it from the controller session via the
Task tool with that agent's name as the subagent type — the session then shows up in
`opencode session list` as a child of the controller session, with model, tokens, and
cost recorded. Avoid `opencode run --agent ...` headless runs for slice work: the
report does not return to the controller, and a hung provider stream has no supervisor.

After the executor session finishes:

```bash
opencode session list                 # find the executor session id
opencode export <sessionID> > s.json  # dump the session as JSON
```

The session-level block (`info` in the export) carries the exact numbers to copy into
the ledger entry:

- `info.modelID` / `info.providerID` → `agent_model` (`<providerID>/<modelID>`)
- `info.tokens.input` / `.output` / `.reasoning` / `.cache.read` → the token fields
- `info.cost` → `cost_usd`

Cross-check the model class with `opencode stats --models` (aggregate view). Note the
per-message `tokens` in the export are cumulative-per-turn; **always use the
session-level `info` block**, not a sum of messages.

Executions done outside a tracked opencode session (e.g. a human or an external tool)
omit these fields — they are optional, and an empty field is more honest than a guess.

## Ledger

Canonical ledger: `docs/factory/shard-metrics.yaml`. One block per shipped shard.
`ShardMetricsLedgerTests` enforces its integrity: ids resolve to real shards in
`shards.yaml`, ids are unique, and every entry carries the required fields.

`guardrail_failures` and `fix_commits` should be **derived from CI/git history**, not
self-reported — hand-entered numbers drift and flatter. Until tooling derives them,
the agent records them from the branch's actual commit/test history.

## Gate

- **Warn-only until three shards** have entries. Warn = reviewer checks the entry exists
  and the numbers are plausible.
- **Hard afterward**: a shipped slice without a ledger entry fails `verify-shard`, and
  `human_interventions` above the ratchet target (start: **≤ 1 per shard**) fails review —
  the target ratchets down as the recipe improves.

## Reading the numbers

- Rising `human_interventions` → slices are too large or under-specified; fix the recipe,
  not the agent.
- High `guardrail_failures` with low `interventions` → guardrails are doing their job
  (self-correction), but consider enriching `new-shard.md` with the recurring failure mode.
- `defects_escaped > 0` → post-mortem per the defect-log rule; the systemic gap updates
  `AGENTS.md` / the recipe in the same change.

## Factory dashboard

`docs/factory/dashboard.html` is a generated static view of the factory's current
state, rendered from `shards.yaml`, `docs/factory/shard-metrics.yaml`, and the plan
Markdown under `docs/plans/` and `docs/superpowers/plans/`. The committed snapshot is
regenerated and verified with:

Plan status precedence is explicit status, whole-plan progress, checkboxes, then
needs review. Positive whole-word completion values are `done`, `complete`, `completed`,
and `shipped`; negated forms such as `Incomplete` or `Not completed` do not complete a plan.
Fenced or indented examples are not plan metadata.

```bash
python3 scripts/generate-factory-dashboard.py
python3 scripts/generate-factory-dashboard.py --check
python3 scripts/test-generate-factory-dashboard.py
```

- The first command rewrites `docs/factory/dashboard.html` from current sources —
  run it after any change to the manifest, ledger, or plan files, and commit the
  result in the same change.
- `--check` regenerates in memory and exits non-zero if the committed snapshot
  is stale (CI-style freshness gate); it never writes.
- The third command runs the generator's unit tests.

Missing ledger records (shards that have not shipped) and needs-review plans are
**intentional rendered states**, not errors: the dashboard shows them as such rather
than failing. PyYAML must be available in the Python 3 environment that runs the
generator.
