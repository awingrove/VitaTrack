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
- References: `VitaTrack.Core` (for repositories, models, services).
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
- **Never mock `HttpClient`** for the LLM seam — fake the transport with a stub `HttpMessageHandler` (`TestDoubles/RecordingHandler.cs`, `ThrowingHandler.cs`, and `SequencedHttpClientFactory` when the factory is what a test drives). There is nothing to configure on a client, because the base URL, key and model travel per request. Moq's `Protected()` is still the right tool on a bare `HttpMessageHandler` where a test only needs a status code (the `HtmlScraperService` tests do exactly that); it is the `HttpClient` that is retired.
- Supply connection state as a `ServiceConnection` plus `LlmRequestSettings` via `LlmTestData` (`TestDoubles/LlmTestData.cs`); there is no configuration to bind.
- Verify the service returns a `LlmResult` with expected fields.
- Do **not** hit the real LLM API in unit tests.

## Naming
- Test method names describe the scenario: `Add_GetAll_GetById_Update_Works`, `GetAll_ReturnsEmpty_WhenNoData`.
- Keep them readable; avoid underscores in the middle of words unless separating logical parts.

## Running Tests
- `dotnet test` from solution root or test project folder.
- In Visual Studio: Test Explorer.
- Fail fast: treat any test failure as a blocker for committing.

## Debugging When Behavior Contradicts Source
When a running app behaves in a way the source says is impossible (e.g., validation errors that shouldn't fire), separate **code** from **deployment** before theorizing:
1. Write a plain unit test against the compiled model/service (`Validator.TryValidateObject`, direct method call). If it passes, the code is fine and the problem is hosting/binding — e.g., MVC's NRT implicit `[Required]` layer, which plain `Validator` does not reproduce.
2. Repro with `curl` against one manually started server (`dotnet run --environment Test --urls http://localhost:PORT`) — no Playwright loop. Capture server-side state (e.g., temporary controller logging of bound values + `ModelState` errors), not just page HTML.
3. Never conclude "stale DLL" without proof. `grep -a` on .NET assemblies gives **false negatives**: string literals live in the UTF-16 #US heap, so ASCII greps miss them; identifiers are ASCII and do match. Prefer behavioral probes over binary archaeology.
4. Kill leftover servers by port (`ss -ltnp` → kill pid); `pkill -f dotnet` can hang the shell. A stale server on a reused port poisons every subsequent repro.

## Coverage Goal
- Aim for **≥80%** line coverage on repository and service layers (aspirational target, root AGENTS.md).
- CI gates at **≥65%** line via `./coverage-check.sh` (actual 66.8% as of Aug 2026). Ratchet rule: when a PR adds tests that raise actual coverage, bump the default `THRESHOLD` in `coverage-check.sh` to just below the new actual — the floor only moves up, never down.
- UI layer tested via Playwright E2E tests (in `e2e-tests/playwright/`).

## Playwright E2E Tests
- Tests live in `e2e-tests/playwright/tests/`.
- Run via `npx playwright test` from `e2e-tests/playwright/`.
- **Never mock the app's own HTTP** — E2E tests hit the real running application, and nothing stands in for an app endpoint's answer. A **real local server standing in for an external dependency the app calls** is not that: see the no-mock rule in the root `AGENTS.md`, and `e2e-tests/playwright/helpers/llm-stub.js` for the worked example.
- **DB Isolation:** there is no test DB *file* to isolate — `appsettings.Test.json` points at a named shared **in-memory** SQLite database (`Data Source=VitaTrack.Test.Memory;Mode=Memory;Cache=Shared`) kept alive for the process by the keep-alive singleton in `ServiceCollectionExtensions.AddCore`. `global-setup.js` is a no-op that prints "In-memory SQLite — no file cleanup needed", not a deleter. The server loads `appsettings.Test.json` via `--environment Test`.
- **Shared DB state:** Tests run in parallel (4 workers) against one server. When mutating data, use dynamic assertions (`.first()`, `.last()`, relative counts) instead of exact values.
- **Seeding:** Report tests depend on `PrescribedDoses` seed data in `DbInit.EnsureCreated`. If adding a new report, seed the required data there.
- **Adding a new test file:** Create `tests/<feature>.spec.js`. Follow existing patterns (e.g., `home.spec.js` for simple navigation, `prescribed-dose.spec.js` for CRUD with create-before-edit/delete).
