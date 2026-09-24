# VitaTrack.Core – Data & Service Layer

## Responsibilities
- Persist data using **Dapper** over SQLite.
- Define repository interfaces (`IFamilyRepository`, `ISupplementRepository`, `ISupplementNutrientRepository`, `IPrescribedDoseRepository`).
- Implement repositories with async CRUD methods.
- Provide access to external services (LLM) via `ILlmService`.
- Contain models used across layers; every slice owns its models under `Features/<Slice>/` (`Supplement`, `FamilyMember`, `SupplementNutrient`, `PrescribedDose`, `LlmResult`, report records) — `VitaTrack.Core/Models` and the flat `VitaTrack.Core/Services` namespace are retired.
- No direct HTTP or UI concerns; keep pure C#.

## Conventions
- Interfaces: prefix `I`, co-located with their implementation — feature slices own theirs under `VitaTrack.Core/Features/<Slice>/` (ADR-0006); shared infrastructure ones live in `VitaTrack.Core.Data` or `VitaTrack.Core.Services`.
- Implementations: suffix `Repository` or `Service`, same namespace.
- Models: plain POCOs with public get/set; default string values `string.Empty`. Feature-owned models live in their slice folder, not `VitaTrack.Core/Models`.
- Constructor injection: receive `IDbConnection` (repositories) or `HttpClient` + `IConfiguration` (LLM service).
- All I/O methods are `async` and return `Task<T>` or `Task<IReadOnlyList<T>>`.
- Use `await _db.QueryAsync<T>(sql)` for reads.
- Use `await _db.ExecuteAsync(sql, param)` for writes.
- For inserts returning identity, execute `INSERT` then `SELECT last_insert_rowid()` as two separate calls (SQLite limitation).

## Foreign Key Delete Order
SQLite enforces foreign keys. When implementing `DeleteAsync` for a parent table, **always delete child rows first**. The current dependency chain is:

    SupplementNutrients → Supplements
    PrescribedDoses    → Supplements
    PrescribedDoses    → FamilyMembers

When deleting a `Supplement`, delete in this order:
1. `DELETE FROM SupplementNutrients WHERE SupplementId = @Id`
2. `DELETE FROM PrescribedDoses WHERE SupplementId = @Id`
3. `DELETE FROM Supplements WHERE Id = @Id`

**Cross-slice deletes are routed via the owning slice's repository** (ADR-0006 cross-slice invariant). `SupplementRepository.DeleteAsync` (both overloads) no longer issues that raw SQL itself — it calls `ISupplementNutrientRepository.DeleteBySupplementIdsAsync` and `IPrescribedDoseRepository.DeleteBySupplementIdsAsync`, which implement the same order against their own tables. When a delete cascade crosses a slice boundary, add a bulk-delete method to the owning slice's repository and call it — never write SQL against another slice's table. Repo-to-repo constructor injection is the accepted pragmatic pattern for this.

When deleting a `FamilyMember`, delete in this order:
1. `DELETE FROM PrescribedDoses WHERE FamilyMemberId = @Id`
2. `DELETE FROM FamilyMembers WHERE Id = @Id`

`FamilyRepository.DeleteAsync` routes step 1 through `IPrescribedDoseRepository.DeleteByFamilyMemberIdsAsync` instead of issuing that SQL itself (same cross-slice rule as `SupplementRepository` below).

Bulk deletes (`DeleteAsync(IEnumerable<int> ids)`) must follow the same order using `WHERE Id IN @Ids`.

When deleting a `SupplementNutrient` that is a blend parent, delete its children first:
1. `DELETE FROM SupplementNutrients WHERE ParentNutrientId = @Id`
2. `DELETE FROM SupplementNutrients WHERE Id = @Id`

`SupplementNutrients.ParentNutrientId` is a self-reference **without a DB constraint** (added via `ALTER TABLE` in `DbInit`); the cascade above is app-enforced and is the only thing preventing orphaned child rows.

When adding new tables (or FK-like columns, via `CREATE TABLE` or `ALTER TABLE` migration in `DbInit`) with foreign keys, update the relevant `DeleteAsync` methods **in the same change** — a migration without its cascade update silently orphans or blocks deletes at runtime. Add cascade-delete unit tests alongside (`Delete_Parent_AlsoDeletesChildren` pattern).

## Transaction Handling
- Currently each repository method opens/closes the connection via Dapper (connection is scoped from Web).
- If multiple operations need a transaction, open a connection and use `IDbTransaction` (future work).

## Dependencies
- Packages: `Dapper`, `Microsoft.Data.Sqlite`, `Microsoft.Extensions.Configuration.Abstractions`, `Microsoft.Extensions.Http`.
- No reference to `VitaTrack.Web`; only depends on .NET primitives and NuGet.

## Testing
- Tests live in `VitaTrack.Tests`.
- Unit tests must pass before considering a feature complete; the aim of unit testing is to verify that a piece of functionality is defect‑free under the tested conditions.
- Use **in‑memory SQLite** (`Microsoft.Data.Sqlite`) with connection string `Data Source=:memory:`.
- Base class `SqliteTestBase` handles connection creation and schema initialization.
- Mock `HttpClient` (with Moq) for `OpenRouterLlmService` tests.

## Adding New Features
1. Add model (if needed) to the owning feature slice under `VitaTrack.Core/Features/<Slice>/`.
2. Extend repository interface (if new entity) and implement.
3. Register new interface/implementation in `VitaTrack.Web/Program.cs` via `builder.Services.AddScoped<...>()`.
4. If external service, add to `VitaTrack.Core.Services` and register via `AddHttpClient<TInterface, TImplementation>()` (you may also need to register `HttpClient` separately if not already).
5. Write unit tests in `VitaTrack.Tests` before or after implementation (TDD encouraged).

## Build
- `dotnet build VitaTrack.Core.csproj` (or via solution).
- No executable produced; it's a class library.
