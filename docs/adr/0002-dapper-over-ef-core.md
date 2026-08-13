# ADR-0002: Dapper + raw SQL over Entity Framework Core

- **Status:** Accepted
- **Date:** 2026-08-05 (retroactively documented 2026-08-13)

## Context

Need a persisting intermediate between in-memory and ORM choice.

- Schema is small (5 entities: FamilyMember, Supplement,
  SupplementNutrient, PrescribedDose, LlmResult; ~8 tables max).
- Foreign-key cascade behavior is explicit in raw SQL (`DELETE FROM
  SupplementNutrients WHERE SupplementId = @Id` before deleting
  parent Supplements, per root AGENTS.md). Hiding cascade behaviour
  in EF config makes the FK contract harder to audit.
- LLM-extraction patterns change frequently (per supplement); writing
  EF linq queries is harder than SQL for the team's wider habit.
- EF Core adds ~3 MB and a startup reflection cost we'd never earn
  back with an app this size.

## Decision

Use **Dapper only** over a single `IDbConnection` scope. SQL is
inline in the `*Repository` implementation; `IN async`/`EQ @Id`
basecase. Repos promote identity-PK rows by `last_insert_rowid()`.
No migration files. Schema is created on startup via
`DbInit.EnsureCreated` (raw `CREATE TABLE IF NOT EXISTS` SQL).

EF Core / EF Core SQLite are **banned** (architecture test
`VitaTrack.ArchitectureTests.EcosystemGuardrailTests` enforces no
transitive reference).

## Consequences

- SQL is visible in repo files. Auditing cascades is straightforward.
- No domain model annotations (`[Key]`, `[ForeignKey]`) needed —
  entities stay plain POCOs with `string.Empty` defaults.
- Schema migrations (rare) are bare-sql; no EF migration scaffolding.
- LINQ-to-SQL familiarity is not leveraged; new contributors need
  minimal comfort with SQL.
- If reporting queries get complex (joins across 4+ tables,
  window functions) the bar isn't higher — raw SQL stays readable;
  EF's visitor pattern would buy little here.