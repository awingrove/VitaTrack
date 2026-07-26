# VitaTrack.Tests – Test Project

## Purpose
- Verify correctness of repository logic and service layer.
- Ensure refactors do not break existing behavior.
- Unit tests must pass before considering a feature complete; the aim of unit testing is to verify that a piece of functionality is defect‑free under the tested conditions.
- Run on every commit; keep suite green.

## Test Framework
- **MSTest** (`[TestClass]`, `[TestMethod]`).
- Use `TestInitialize`/`TestCleanup` for per‑test setup.
- Use `Async` test methods when awaiting.

## Dependencies
- References: `VitaTrack.Infrastructure` (for repositories, models, services).
- Packages: `MSTest.TestFramework`, `MSTest.TestAdapter`, `Microsoft.NET.Test.Sdk`, `Moq`, `Microsoft.Data.Sqlite`, `Dapper`.

## Test Organization
- One test class per repository/service: `*RepositoryTests.cs`, `*ServiceTests.cs`.
- Shared base class: `SqliteTestBase` – creates an **in‑memory SQLite** connection, runs `DbInit.EnsureCreated`, provides `IDbConnection`.
- Dispose connection after each test.

## Writing Repository Tests
1. Instantiate repository with the test connection.
2. Exercise CRUD operations: Add → GetById → GetAll → Update → (optional) Delete.
3. Assert on returned values and counts.
4. Use `Assert.IsTrue`, `Assert.AreEqual`, `Assert.IsNull`, etc.

## Writing Service Tests (LLM)
- Mock `HttpClient` using `Moq.Protected().Setup<...>("SendAsync", ...)`.
- Provide a fake `IConfiguration` with `OpenRouter:BaseUrl` and `OpenRouter:ApiKey`.
- Verify the service returns a `LlmResult` with expected fields.
- Do **not** hit the real OpenRouter API in unit tests.

## Naming
- Test method names describe the scenario: `Add_GetAll_GetById_Update_Works`, `GetAll_ReturnsEmpty_WhenNoData`.
- Keep them readable; avoid underscores in the middle of words unless separating logical parts.

## Running Tests
- `dotnet test` from solution root or test project folder.
- In Visual Studio: Test Explorer.
- Fail fast: treat any test failure as a blocker for committing.

## Coverage Goal
- Aim for **≥80%** line coverage on repository and service layers.
- UI layer tested via integration tests (WebApplicationFactory) if needed later.
