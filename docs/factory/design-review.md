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
3. **Current-state facts verified** — every claim about existing code ("X is claimed by
   Y", "Z is already in the allowlist") is checked against `shards.yaml` /
   `storymap.yaml` at briefing time, not recalled (DL-002). Likewise, dry-run each
   briefing step against the guardrail that gates its commit (e.g. the pre-commit hook
   runs ShardOwnershipTests on every commit, so file moves and manifest re-points land
   in the same step).
4. **Recommended option** — with the material tradeoff called out explicitly.
5. **Blast radius** — which slices, repos, views, and tests are affected; which
   `shards.yaml` / `storymap.yaml` entries change.
6. **Migration / rollback** — for schema changes: forward migration + how to roll back
   without data loss; for code: the commit that reverses it.
7. **Reviewer sign-off** — name + date. Unsigned = not merged.
8. **Record** — outcome written to the relevant ADR (or a new `docs/adr/` entry) and, if it
   reveals a systemic gap, to `AGENTS.md` / this checklist in the same change.

## Notes

- The intervention ledger (`metrics.md`) is warn-only until three shards have entries;
  after that a shipped slice without an entry fails the gate, and `human_interventions`
  above the ratchet target fails review.
- The `new-shard` recipe (`docs/factory/new-shard.md`) embeds this gate at step 1.
