# Executive Summary — Our Software Factory Process

How we build software: a repeatable process where AI agents construct features under
strict automated quality controls, and humans own every design decision. The goal is
provable quality and productivity — not asserted quality.

## The principles

- **Plan before code.** Requirements and design are written down and checked before any
  implementation starts.
- **Features own themselves.** Each feature is built as a complete vertical slice — user
  interface, business rules, data access, and tests together — rather than being spread
  across separate layers that must be reassembled.
- **Machines enforce rules; people make decisions.** Automated checks catch violations of
  agreed conventions instantly. Judgement calls — architecture, boundaries, tradeoffs —
  always stop for human approval.
- **Measure touch, not time.** We track how often humans had to intervene, how often
  automated checks caught problems, and how many defects escape to production. Hours are
  meaningless across different AI models; touch and rework are the signal.

## The process — six steps per feature

1. **Claim the work.** Register the feature in our requirements and ownership records.
   If it raises a new architectural question, work stops until a developer decides.
2. **Copy a proven template.** Every feature starts from a reference implementation that
   already passed every check — change the names, keep the shape.
3. **Prove the path end to end first.** Before adding detail, build a thin but complete
   version of the feature that works from screen to database, with a test that clicks
   through the real app. If this "tracer bullet" fails, the design is wrong — fix it now,
   not later.
4. **Build it out.** Add validation, edge cases, and depth, following the conventions:
   typed contracts, explicit business rules, no shortcuts around the patterns.
5. **Pass the gate.** One acceptance gate runs everything — formatting, build, all
   architecture rules, unit tests, and end-to-end tests against the real running
   application. "Done" means the gate is green; a red gate means stop and fix the feature,
   never widen the change.
6. **Record the results.** Log how much human help the feature needed, any problems the
   gate missed, and lessons that should improve the process itself.

## Responsibilities

| Who | Owns |
|---|---|
| **AI agent** | Scaffolding, implementation, running the gate, self-correction, recording metrics |
| **Automated guardrails** | Conformance: ownership, code size, layering rules, reachability, test coverage |
| **Human (design review)** | Architecture, feature boundaries, data changes with business meaning — signed off by a named reviewer |
| **Human (quality loop)** | Reviewing escaped defects and approving process changes |

The split is deliberate: guardrails can tell you a component is too large, but not whether
a boundary is *right*. Machines build; people decide; the checklists make the handoff
explicit and rare.

## How we know it works

Every feature ships with a metrics entry: which class of AI model built it, how many
human interventions were needed (zero means fully autonomous), how many times automated
checks caught problems before green, and how many defects escaped. Targets tighten as the
process improves — once a feature ships without its metrics record, the gate rejects it,
and excessive human intervention fails review.

Escaped defects are the most valuable signal. Each one is logged with **where it was
introduced** and **why it slipped through**, and every defect triggers a mandatory
improvement: if a mistake reveals a gap in our rules or templates, that gap is closed in
the same change that fixes the bug. The process learns from its own failures — two
example defects have already rewritten the recipe and the design-review checklist.

Independent evidence matters: when our tests were written by the same AI that introduced
the defect, they passed while encoding the bug. That is why human review stays mandatory
and why defects are measured, not hidden.

## The takeaway

Requirements and ownership are declared in machine-checked records; features follow a
template and must clear one acceptance gate; AI works freely where rules exist and stops
where design begins; and every failure feeds back into the process. The documents describe
the process, the automated checks enforce it, and the metrics prove it — so quality claims
are evidence, not marketing.
