# VitaTrack.Infrastructure – Data & Service Layer

## Responsibilities
- Persist data using **Dapper** over SQLite.
- Define repository interfaces (`IFamilyRepository`, `ISupplementRepository`, `ISupplementNutrientRepository`, `IPrescribedDoseRepository`).
- Implement repositories with async CRUD methods.
- Provide access to external services (LLM) via `ILlmService`.
- Contain models (`FamilyMember`, `Supplement`, `SupplementNutrient`, `PrescribedDose`, `LlmResult`) used across layers.
- No direct HTTP or UI concerns; keep pure C#.

## Conventions
- Interfaces: prefix `I`, located in `VitaTrack.Infrastructure.Data`.
- Implementations: suffix `Repository` or `Service`, same namespace.
- Models: plain POCOs with public get/set; default string values `string.Empty`.
- Constructor injection: receive `IDbConnection` (repositories) or `HttpClient` + `IConfiguration` (LLM service).
- All I/O methods are `async` and return `Task<T>` or `Task<IReadOnlyList<T>>`.
- Use `await _db.QueryAsync<T>(sql)` for reads.
- Use `await _db.ExecuteAsync(sql, param)` for writes.
- For inserts returning identity, execute `INSERT` then `SELECT last_insert_rowid()` as two separate calls (SQLite limitation).

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
1. Add model (if needed) to `VitaTrack.Infrastructure.Models`.
2. Extend repository interface (if new entity) and implement.
3. Register new interface/implementation in `VitaTrack.Web/Program.cs` via `builder.Services.AddScoped<...>()`.
4. If external service, add to `VitaTrack.Infrastructure.Services` and register via `AddHttpClient<TInterface, TImplementation>()` (you may also need to register `HttpClient` separately if not already).
5. Write unit tests in `VitaTrack.Tests` before or after implementation (TDD encouraged).

## Build
- `dotnet build VitaTrack.Infrastructure.csproj` (or via solution).
- No executable produced; it's a class library.
