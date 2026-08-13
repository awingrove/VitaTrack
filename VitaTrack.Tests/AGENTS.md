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

## Testing Delete with Foreign Keys
When a table has foreign key dependencies, you **must** test that deleting a parent row also deletes the child rows:
1. Create parent and child rows (e.g., a Supplement with SupplementNutrients and PrescribedDoses).
2. Delete the parent row via the repository.
3. Assert the parent row is gone.
4. Assert **all** child rows that referenced the parent are also gone.
5. Example: `DeleteMultiple_RemovesSupplementsWithNutrients` creates supplements with nutrients and a prescribed dose, deletes them, then verifies all three tables are clean.

This is critical — missing cascade delete tests leads to foreign key constraint failures at runtime.

## Writing Service Tests (LLM)
- Mock `HttpClient` using `Moq.Protected().Setup<...>("SendAsync", ...)`.
- Provide `IOptions<VitaTrackOptions>` via `Options.Create(new VitaTrackOptions { ... })`.
- Verify the service returns a `LlmResult` with expected fields.
- Do **not** hit the real LLM API in unit tests.

## Naming
- Test method names describe the scenario: `Add_GetAll_GetById_Update_Works`, `GetAll_ReturnsEmpty_WhenNoData`.
- Keep them readable; avoid underscores in the middle of words unless separating logical parts.

## Running Tests
- `dotnet test` from solution root or test project folder.
- In Visual Studio: Test Explorer.
- Fail fast: treat any test failure as a blocker for committing.

## Coverage Goal
- Aim for **≥80%** line coverage on repository and service layers (aspirational target, root AGENTS.md).
- CI currently gates at **≥50%** line on `VitaTrack.Infrastructure` via `./coverage-check.sh` (current actual ~53%; see ArchitectureReview §2.11 and commit history). Raise the threshold via `COVERAGE_THRESHOLD=NN` as targeted test PRs ratchet coverage toward 80%.
- UI layer tested via Playwright E2E tests (in `e2e-tests/playwright/`).

## Playwright E2E Tests
- Tests live in `e2e-tests/playwright/tests/`.
- Run via `npx playwright test` from `e2e-tests/playwright/`.
- **Never mock HTTP** — E2E tests hit the real running application.
- **DB Isolation:** `global-setup.js` deletes `VitaTrack.Test.db` before each run. The server loads `appsettings.Test.json` via `--environment Test`.
- **Shared DB state:** Tests run in parallel (4 workers) against one server. When mutating data, use dynamic assertions (`.first()`, `.last()`, relative counts) instead of exact values.
- **Seeding:** Report tests depend on `PrescribedDoses` seed data in `DbInit.EnsureCreated`. If adding a new report, seed the required data there.
- **Adding a new test file:** Create `tests/<feature>.spec.js`. Follow existing patterns (e.g., `home.spec.js` for simple navigation, `prescribed-dose.spec.js` for CRUD with create-before-edit/delete).
