# Tracer Bullet (cross-layer slice)

A **tracer bullet** is a thin but *complete* vertical path through every layer of a feature,
built first so the whole pipe works from commit one. Depth (validation, edge cases) is added
later, but the skeleton is never stubbed. This is the Phase 4 factory primitive: it makes a
slice observable end-to-end before any logic is layered in.

## Why

- A feature that is green at the unit level but unreachable from the UI is a defect
  (`UiReachabilityTests` catches this mechanically).
- Reviewers can see the data at each hop (controller binding → handler result → repo read →
  view model) rather than trusting a type signature.

## Shape (mirrors the Dosing pilot)

1. **Controller** — one GET (list/detail) + one POST (create/edit) that binds a **request DTO**,
   calls one handler, returns a view or redirect. No entity binding from the form.
2. **Handler** — returns a result record (`XCommandResult`); carries the business rule.
3. **Repository** — one read + one write, Dapper-only, in the slice folder.
4. **View** — renders the result; reachable from an in-app link (`asp-controller` /
   nav bar), never only by direct URL.
5. **E2E** — one spec that clicks the in-app link and asserts the round trip.

## Observable intermediate state

For debugging, each hop can temporarily assert its contract: controller logs the bound DTO,
handler logs the result, repo logs the SQL param. These are removed before merge — the
tracer's value is the *working path*, not the logs. The canonical exemplar is
`VitaTrack.Core/Features/Dosing/`.

## Gate

The tracer must be green (verify-shard) before depth is added. A red tracer means the slice
boundary or entry point is wrong — stop, don't patch forward.
