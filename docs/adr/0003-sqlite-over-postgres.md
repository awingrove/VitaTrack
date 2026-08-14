# ADR-0003: SQLite (file + in-memory shared cache) over PostgreSQL

- **Status:** Accepted
- **Date:** 2026-08-05 (retroactively documented 2026-08-13)

## Context

Local-first single-user supplement tracker; no multi-instance setup
planned.

- Hosting Postgres requires installation, port, credentials — a
  friction tax for someone who wants to clone and run locally.
- SQLite is one file next to the executable; well-supported by
  Dapper.
- Test runtime benefits from in-memory SQLite (`Mode=Memory;Cache=Shared`
  with keep-alive singleton, see Commit 0a21bbe) to avoid file I/O
  races across parallel workers.

## Decision

**Production** mode uses file-backed SQLite (`VitaTrack.db` near the
executable).

**Test** mode (`--environment Test`, `appsettings.Test.json`) uses a
named shared in-memory DB: `Data Source=VitaTrack.Test.Memory;
Mode=Memory;Cache=Shared`. The keep-alive singleton in
`ServiceCollectionExtensions.AddInfra` (registered when
`builder.Mode == SqliteOpenMode.Memory`) holds one connection open
for the process lifetime, so the named DB is not destroyed when
scoped connections dispose.

## Consequences

- One DB file ships with the executable; copy/sync it like any other
  asset for migration.
- Concurrency is read-many, writer-serialized. Single-user app:
  tolerable. If multi-user write contention becomes the norm,
  rethink this ADR.
- In-memory tests parallelize cleanly; no `database is locked`.
- Foreign keys are enforced by SQLite (so deletion order in
  repositories matters; see root AGENTS.md "Foreign Key
  Constraints").
- Cannot use SQLite extensions not bundled in SQLitePCLRaw
  (e.g., full-text search requires enabling). For text search we
  lean on LLM-text extraction rather than SQL FTS.