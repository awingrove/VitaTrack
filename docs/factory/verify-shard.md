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
   `RepositoryNamingTests` (repos end in `Repository`).
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

## Estimation gate

If `docs/factory/metrics.md` has ≥ 3 shards with `actual_hours`, the gate also checks the
shard's `estimate_hours` is present and within 3x the running median. Warn-only before
that threshold.
