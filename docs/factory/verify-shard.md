# verify-shard — scoped acceptance gate for one shard

The gate a shard must pass before it is "done" (Definition of Done in `FACTORY.md`).
Run it from the shard's working tree; all checks must be green.

## Steps

1. **Format** — `dotnet format VitaTrack.sln --verify-no-changes`
   Fails on any style drift. Auto-fix locally with `dotnet format VitaTrack.sln`.
2. **Build** — `dotnet build VitaTrack.sln -c Release`
3. **Architecture tests** — `dotnet test VitaTrack.sln -c Release`
   Inside: `ShardOwnershipTests` (no orphan/double-claim, story-map id integrity),
   `FileSizeTests` (no complete type > 300 lines incl. partials),
   `RepositoryNamingTests` (repos end in `Repository`),
   `CrossSliceSqlTests` (SQL references only the slice's declared `tables`).
4. **Slice unit tests** — run the slice's `unit_tests` from `shards.yaml`
   (in-memory SQLite). Must be green and actually exercise the feature.
5. **Slice e2e** — `cd e2e-tests/playwright && npx playwright test tests/<name>.spec.js`
   Must arrive by **clicking in-app links**, not `goto` deep URLs (`UiReachabilityTests`).
6. **Tracer green** — the cross-layer path (controller → handler → repo → view) works from
   the first commit (see `tracer-bullet.md`); no step is silently stubbed.

## Hard failures

- Architecture test red → **stop**. Do not widen the change to "fix forward"; fix the shard
  (re-point `shards.yaml`, split the type, rename the repo).
- E2E arrives only via `goto` → the page is orphaned; wire a nav/list entry point.

## Ledger gate

A shipped slice needs an entry in `docs/factory/shard-metrics.yaml` (`ShardMetricsLedgerTests`
enforces schema integrity in CI). Warn-only until three shards have entries; afterward a
missing entry fails the gate, and `human_interventions` above the ratchet target
(start: ≤ 1 per shard) fails review. See `metrics.md`.

Before merging, reconcile the entry against the review outcome:

- A review finding that forced changes ⇒ `human_interventions ≥ 1`.
- Any commit after the executor's first done claim ⇒ `fix_commits ≥ 1`.
- A plan-mandated Important finding (or any finding that conflicts with the
  plan's text) is the human's decision — surface it; do not self-adjudicate
  (design-before-code gate, `BIBLIOGRAPHY.md` glossary).
