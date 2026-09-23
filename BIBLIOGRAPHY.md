# VitaTrack Software Factory Bibliography & Agent Guide

This bibliography is the foundational knowledge base for LLM agents operating within the VitaTrack SDLC. Agents should evaluate their current context (scaffolding, gate-checking, refactoring, design decisions) against the **LLM Prompt Triggers** to determine when to apply principles from these texts.

## Factory Concepts Glossary

Factory-specific terms mapped to their origin texts:

| Factory Term | Origin Text | Meaning |
|---|---|---|
| **Tracer bullet** | *The Pragmatic Programmer* (Hunt/Thomas) | Thin but complete vertical path through every layer, built first so the whole pipe works from commit one |
| **Shard** | *Domain-Driven Design* (Evans) + factory invention | Vertical feature slice owning its logic end-to-end; declared in `shards.yaml` |
| **Verify-shard gate** | *Fundamentals of Software Architecture* (Richards/Ford) — fitness functions | Scoped acceptance gate: format → build → arch tests → unit → e2e → tracer green |
| **Design-before-code gate** | *Team Topologies* (Skelton/Pais) — cognitive load boundaries | Human checkpoint when a shard needs an ADR or slice-boundary decision; agent stops, prompts developer |
| **Measure touch, not time** | *Software Estimation* (McConnell) + *The Goal* (Goldratt) | Ledger tracks human interventions + escaped defects, not wall-clock hours |
| **Cross-slice invariant** | *Domain-Driven Design* (Evans) — bounded contexts | Read only via published repository interface; never SQL on another slice's tables |
| **Post-mortem rule** | *Working Effectively with Legacy Code* (Feathers) — injection-stage thinking | Escaped defect → log injection stage + root cause → update AGENTS.md/recipe same change |

## Core SDLC & Process

### *The Pragmatic Programmer: Your Journey to Mastery* (20th Anniversary Edition) — David Thomas, Andrew Hunt
**LLM Prompt Trigger:** When scaffolding a new shard, writing tracer-bullet code, applying DRY, or deciding whether to extract a concept into a separate file.
**Factory Context:** Origin of "tracer bullet" term. DRY principle enforces no duplication across slices. Orthogonality validates slice independence. Post-mortem culture (when something breaks, fix the system, not just the bug).

### *Working Effectively with Legacy Code* — Michael Feathers
**LLM Prompt Trigger:** When refactoring tangled code, identifying seams for testing, or deciding how to break dependencies without breaking behavior.
**Factory Context:** Injection-stage thinking (where was the defect born?) is Feathers. Seam identification validates the handler-per-rule threshold. TD-003/004 paydown (DosageParser → Dosage, Dictionary → typed records) is Feathers-flavored refactoring.

### *Software Estimation: Demystifying the Black Art* — Steve McConnell
**LLM Prompt Trigger:** When setting ratchet targets, interpreting metrics ledger data, or estimating effort for a shard.
**Factory Context:** Construx lineage. Measurement discipline: track what matters (touch, rework), not what's easy (hours). Interest rates on technical debt. Ratchet logic (tighten targets as recipe improves).

### *Release It!: Design and Deploy Production-Ready Software* (2nd Edition) — Michael Nygard
**LLM Prompt Trigger:** When designing failure modes, adding resilience patterns, or deciding whether a feature is production-ready.
**Factory Context:** Stability patterns (bulkheads, circuit breakers) are slice-ownership thinking applied to runtime. Tracer-bullet-as-safety: if the vertical path fails, the slice boundary is wrong.

### *The Phoenix Project* — Gene Kim, Kevin Behr, George Spafford
**LLM Prompt Trigger:** When identifying bottlenecks in the factory process, or when a shard's `human_interventions` count rises.
**Factory Context:** Theory of Constraints applied to IT. Rising interventions = bottleneck in recipe, not agent. Fix the constraint, not the symptom.

### *The Goal: A Process of Ongoing Improvement* — Eliyahu Goldratt
**LLM Prompt Trigger:** When optimizing factory throughput, or when deciding which technical debt to pay down first.
**Factory Context:** "Measure touch, not time" is Goldratt. Focus on the constraint. Local optimization (one slice) vs system optimization (factory-wide).

## Architecture & Design

### *Domain-Driven Design: Tackling Complexity in the Heart of Software* — Eric Evans
**LLM Prompt Trigger:** When evaluating slice boundaries, resolving shared kernels, or deciding if a domain rule belongs in `Primitives/` allowlist versus a specific feature slice.
**Factory Context:** Dictates "Slices, not layers." Validates `shards.yaml` allowlist for cross-cutting logic. Domain logic lives in owning slice, consumed via published repository interfaces. Bounded contexts = shard boundaries.

### *Vertical Slice Architecture* — Jimmy Bogard
**LLM Prompt Trigger:** When deciding whether to extract a handler, when to organize code by feature vs layer, or when a controller grows beyond 80 lines.
**Factory Context:** Direct lineage for ADR-0006. Explicit handlers over MediatR (zero deps, more readable). Handler-per-operation threshold: extract when operation carries a rule, not for CRUD passthrough.

### *Patterns of Enterprise Application Architecture* — Martin Fowler
**LLM Prompt Trigger:** When designing request DTOs, result records, or deciding between transaction script and domain model.
**Factory Context:** DTO binding (never entity binding from forms), result records for control flow, service layer patterns. Your factory is Fowler-tactical: pragmatic, not dogmatic.

### *A Philosophy of Software Design* — John Ousterhout
**LLM Prompt Trigger:** When a file approaches 300 lines, when deciding whether to split a type, or when evaluating complexity of an abstraction.
**Factory Context:** "No complete type > 300 lines" is Ousterhout-flavored. Deep modules (simple interface, complex implementation) vs shallow modules. Complexity management: define errors out of existence.

### *Team Topologies: Organizing Business and Technology Teams for Fast Flow* — Matthew Skelton, Manuel Pais
**LLM Prompt Trigger:** When deciding shard ownership, when a concept is shared across slices, or when cognitive load is too high for one agent/human.
**Factory Context:** Stream-aligned teams = slice ownership. Cognitive load reduction: one slice = one concept per file. Platform teams = shared kernel (`Primitives/`, `DbInit`, `ServiceCollectionExtensions`).

### *Building Microservices: Designing Fine-Grained Systems* (2nd Edition) — Sam Newman
**LLM Prompt Trigger:** When designing cross-slice communication, when deciding whether to issue SQL on another slice's tables, or when refactoring shared logic.
**Factory Context:** Cross-slice invariant (read-only via interface) is microservices-thinking applied to slices. Service decomposition: bounded contexts, shared-nothing where possible.

### *Fundamentals of Software Architecture* — Mark Richards, Neal Ford
**LLM Prompt Trigger:** When designing fitness functions (arch tests), when evaluating tradeoffs, or when deciding whether an abstraction is justified.
**Factory Context:** Fitness functions = arch tests. Tradeoff analysis: every decision has costs. "No speculative scaffolding" is Ford/Richards-flavored: adopt, adapt, don't over-engineer.

## Testing & Quality

### *Test-Driven Development: By Example* — Kent Beck
**LLM Prompt Trigger:** When writing unit tests, when deciding test structure, or when a test is biased toward the bug (same-agent test syndrome).
**Factory Context:** Factory's test philosophy: test behavior, not implementation. DL-001 (Money + silent currency keep) showed same-agent tests encode bugs — test error edges, not just happy path.

### *Growing Object-Oriented Software, Guided by Tests* — Steve Freeman, Nat Pryce
**LLM Prompt Trigger:** When designing testable code, when identifying seams, or when deciding whether to extract a handler.
**Factory Context:** Test-first design, seam identification. Handler-per-rule threshold: extract when operation carries a rule, test the rule in isolation.

### *xUnit Test Patterns* — Gerard Meszaros
**LLM Prompt Trigger:** When structuring tests, when deciding fixture strategy, or when tests are flaky or slow.
**Factory Context:** In-memory SQLite tests are Meszaros-flavored: fresh fixture per test, teardown isolation. Test structure: arrange-act-assert, clear naming.

## AI & Agent Systems

### *AI Engineering: Building AI-Powered Systems* — Chip Huyen
**LLM Prompt Trigger:** When designing eval pipelines, when orchestrating multi-step agent workflows, or when deciding how to measure agent output quality.
**Factory Context:** Eval pipelines = verify-shard gate. Agent orchestration = shard process (claim → exemplar → tracer → build → verify → record). Production AI: measure what matters (interventions, escaped defects), not what's easy (token count).

### *Building LLM-Powered Applications* — various authors (O'Reilly, Packt editions)
**LLM Prompt Trigger:** When designing agent harnesses, when writing prompts for agents, or when deciding how to integrate LLMs into the factory.
**Factory Context:** Agent harnesses = FACTORY.md + AGENTS.md + arch tests. Prompt engineering: factory docs are prompts for agents. Eval: ledger measures agent output quality.

### *The Art of Doing Science and Engineering: How to Be a Successful Scientist* — Richard Hamming
**LLM Prompt Trigger:** When deciding what to measure, when designing feedback loops, or when evaluating whether a claim is proven or asserted.
**Factory Context:** "Prove, don't assert" is Hamming. Measurement: track what matters, not what's easy. Feedback loops: defect → post-mortem → recipe update.
