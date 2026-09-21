# Human Design-Review Checklist

The factory's automated guardrails catch *rule* violations (ownership, size, naming).
They cannot make *design* judgements. When a shard needs an ADR or a slice-boundary
decision, the agent **stops and prompts the developer** — it does not invent architecture.
This checklist is the contract for that gate.

## When to trigger (STOP)

- A new ADR is needed (new abstraction, new cross-slice dependency, new persistence
  pattern).
- A slice boundary is ambiguous — e.g. where a shared concept (value object, service)
  lives, or whether a feature should be its own slice vs. fold into an existing one.
- A change requires a **schema migration with business meaning** (new column that carries
  domain data, e.g. a `Currency` column for `Money`).
- A feature touches another slice's tables directly (cross-slice SQL) — must be routed
  through that slice's published interface instead.

## Checklist (owner: named architecture reviewer)

Every item needs a named owner, not just "the team". A checklist with no owner silently
never happens.

1. **Decision statement** — one sentence: what is being decided and why it is not mechanical.
2. **Options considered** — at least two, with the tradeoff each imposes on total system
   complexity, ownership, and future change cost.
3. **Recommended option** — with the material tradeoff called out explicitly.
4. **Blast radius** — which slices, repos, views, and tests are affected; which
   `shards.yaml` / `storymap.yaml` entries change.
5. **Migration / rollback** — for schema changes: forward migration + how to roll back
   without data loss; for code: the commit that reverses it.
6. **Reviewer sign-off** — name + date. Unsigned = not merged.
7. **Record** — outcome written to the relevant ADR (or a new `docs/adr/` entry) and, if it
   reveals a systemic gap, to `AGENTS.md` / this checklist in the same change.

## Notes

- Estimation is **warn-only** until three shards have recorded actuals (see
  `metrics.md`); after that the gate hard-fails on missing/implausible estimates.
- The `new-shard` recipe (`docs/factory/new-shard.md`) embeds this gate at step 1.
