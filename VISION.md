# VitaTrack Software Factory Vision

This document captures what we aspire to — not concrete plans, but directions.
Each theme represents a capability that would make the factory measurably better.

## Self-Improving Factory

**Current state:** Post-mortem is manual. Defect → human notices → recipe edit.

**Target state:** Ledger auto-derived from telemetry. Factory files its own recipe PRs when guardrail failures show patterns. Human approves, doesn't author.

**Key enablers:**
- Auto-derive `guardrail_failures` from CI logs (metrics.md already says hand-entered drifts)
- Pattern detection on red-cycle evidence → auto-draft `new-shard.md` amendments
- Rising `human_interventions` triggers automatic recipe review

**Open questions:**
- How to distinguish "recipe needs update" from "agent misread recipe"?
- What's the review SLA for auto-filed recipe PRs?
- Should pattern detection be rule-based or ML-driven?

**Related current work:**
- [FACTORY.md](FACTORY.md) §6 (post-mortem rule)
- [docs/factory/metrics.md](docs/factory/metrics.md) (ledger schema)

## Self-Verifying Documentation

**Current state:** Docs can lie. ADR-0006 claims "arch test locks it" but no test exists. Trust erodes.

**Target state:** Doc claims become executable. Every "enforced by" claim in docs carries a marker that a meta-test resolves. Claims a test that doesn't exist = red build. Docs, manifests, tests stop being three things that can disagree.

**Key enablers:**
- ADR/arch-test cross-reference: doc claims carry testable markers
- Meta-test: resolves every "enforced by" claim, fails if target doesn't exist
- "Docs must not lie" (ArchitectureReview §2.2) becomes CI, not just review rule

**Open questions:**
- What's the marker format? Inline comment? YAML frontmatter? Separate registry?
- How to handle aspirational claims ("we plan to enforce X") vs factual claims ("X is enforced")?
- Should this apply to all docs or only AGENTS.md/ADRs?

**Related current work:**
- [docs/adr/0006-vertical-slice-architecture.md](docs/adr/0006-vertical-slice-architecture.md) (known gap)
- [docs/factory/design-review.md](docs/factory/design-review.md) (human gate, not automated)

## Auto-Metrics Ledger

**Current state:** Ledger entries are hand-entered. Numbers drift. metrics.md says "derived from CI/git history, not self-reported" but tooling doesn't exist yet.

**Target state:** Ledger entries assemble from git/CI telemetry automatically. Agent only writes `notes`. Ratchet target computes from trailing percentiles instead of hand-set "≤1." Productivity-per-dollar becomes a live number, not a blog claim.

**Key enablers:**
- CI pipeline exports guardrail failures, fix commits, intervention count to ledger
- `X-Trace-Id` tracing (factory-v3 Phase 4) correlates escaped defects to injecting shard
- Ratchet algorithm: trailing 90-day percentile of `human_interventions`, tightens quarterly

**Open questions:**
- How to attribute "human intervention" automatically? Manual tagging vs. PR review comments?
- What's the cost model for `cost_usd`? Token metering per session?
- Should ratchet tighten on calendar (quarterly) or on milestone (N shards)?

**Related current work:**
- [docs/factory/metrics.md](docs/factory/metrics.md) (ledger schema, "derived from CI/git")
- [docs/factory/shard-metrics.yaml](docs/factory/shard-metrics.yaml) (current ledger)
- [docs/plans/2026-09-21-factory-v3-vertical-slices.md](docs/plans/2026-09-21-factory-v3-vertical-slices.md) Phase 4 (tracing)

## Design Gate with Superpowers

**Current state:** Agent STOPs, human stares at options. Briefing is assembled by hand, current-state facts recalled from memory (DL-002 showed this fails).

**Target state:** Briefing auto-assembles. Blast radius computed from shards.yaml. Current-state facts verified against manifests at generation time. Dry-run against gating guardrail pre-executed. Migration+rollback sketched. Human reviews pre-chewed decision with named sign-off in minutes.

**Key enablers:**
- Briefing generator: reads shards.yaml, computes affected slices/files/tests
- Fact-checker: verifies every "X is in allowlist" claim against actual manifest
- Guardrail dry-run: pre-executes the gate that will check the commit
- Migration sketcher: for schema changes, generates forward migration + rollback SQL

**Open questions:**
- How to handle ambiguous boundaries (e.g., "should Money live in Primitives or Dosing?")?
- Should the briefing generator be a skill, a script, or a CI step?
- What's the format? Markdown? YAML? Interactive prompt?

**Related current work:**
- [docs/factory/design-review.md](docs/factory/design-review.md) (checklist, manual)
- [docs/factory/technical-debt.md](docs/factory/technical-debt.md) DL-002 (briefing authoring defects)

## Cold-Start Any Agent, Any Team

**Current state:** Session context lives in plan files (AGENTS.md rule). New agent loads FACTORY.md + manifests + verify-shard green = productive with zero chat-history inheritance.

**Target state:** Recipe becomes portable. Another repo adopts new-shard/verify-shard/ledger pattern in a day. VitaTrack stops being an app and becomes the template. Factory-as-product.

**Key enablers:**
- Factory template repo: extract VitaTrack factory into reusable skeleton
- Onboarding script: clones template, runs verify-shard, generates first shard
- Cross-repo metrics: compare productivity across repos using same factory

**Open questions:**
- What's portable vs. VitaTrack-specific? (e.g., Dapper repos vs. EF Core)
- Should the template support multiple languages/frameworks?
- How to handle different team sizes (solo vs. 10-person team)?

**Related current work:**
- [FACTORY.md](FACTORY.md) (process contract)
- [docs/factory/new-shard.md](docs/factory/new-shard.md) (recipe)
- [docs/factory/verify-shard.md](docs/factory/verify-shard.md) (gate)

## The Money Moment

**Current state:** Ledger shows 3 entries (PD, NT, one more). `agent: cheap` proof is aspirational.

**Target state:** Ledger shows N consecutive `agent: cheap` shards: ≤1 intervention, 0 escaped, gate green. Productivity-per-dollar isn't aspiration — it's a chart with rows you can click into commits. Leadership question flips from "can agents code?" to "which slice next?"

**Key enablers:**
- More `agent: cheap` shard entries (Nutrients slice conversion is next)
- Ratchet validation: prove interventions trend down as recipe improves
- Public dashboard: live metrics, not blog post

**Open questions:**
- What's N? When do we have enough data to claim proof?
- Should we publish the dashboard externally (open-source factory)?
- How to handle frontier-model shards (PD pilot) vs. cheap-model shards in the same chart?

**Related current work:**
- [docs/factory/shard-metrics.yaml](docs/factory/shard-metrics.yaml) (ledger)
- [docs/plans/2026-09-21-nutrients-slice.md](docs/plans/2026-09-21-nutrients-slice.md) (next cheap-model proof)
