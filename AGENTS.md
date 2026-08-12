# AI Agent Instructions (AGENTS.md)

This document defines the coding standards, architectural guidelines, testing philosophy, and tech stack for the VitaTrack (Vitamin and Supplement Tracking) ASP.NET application. 

**AI Agents:** Read and adhere to these rules strictly before generating, refactoring, or modifying code in this repository. Per-project `AGENTS.md` files (under `VitaTrack.Web/`, `VitaTrack.Infrastructure/`, `VitaTrack.Tests/`) **supplement** this root; any contradiction is a defect to report, not a license to pick one.

## 🏗️ Architecture & Project Structure
*   **Paradigm:** Pragmatic ASP.NET MVC. Avoid over-engineering and strict Clean Architecture dogmas.
*   **Structure:** 3-project solution (`VitaTrack.Web → VitaTrack.Infrastructure`; `VitaTrack.Tests` refs both). Inter-project direction is enforced by csproj. Web layer handles HTTP, views, and thin mapping; business logic lives in `Infrastructure/Services` and `Infrastructure/Data` (Dapper repositories).
*   **File Size & Organization:** 
    *   Proactively extract classes/interfaces into separate files if a file exceeds 20-30 lines. Keep methods small (< 30 lines) and focused.
    *   **Hard Limit:** No single file should exceed **300 lines**. Refactor immediately if a file approaches this limit.

## 🖥️ UI & Frontend Stack
*   **Views:** Razor views (`.cshtml`) rendered by MVC controllers (not Razor Pages — `Program.cs` uses `MapControllerRoute`). Shared layout: `Views/Shared/_Layout.cshtml`.
*   **Interactivity:** HTMX is load-bearing (`hx-post`/`hx-target`/`hx-swap`/`hx-swap-oob` in Create/UpdateNutrients/Review flows). Vanilla JS (external `.js` under `wwwroot/js`) alongside HTMX for row add/remove and checkbox selection.
*   **CSP:** `Program.cs` sets `script-src 'self' https://cdn.jsdelivr.net` in non-Dev envs (no `'unsafe-inline'`). Self-host JS under `wwwroot/lib` or `wwwroot/js`; htmx re-executes external `<script src>` tags on swap, so partials can include their own `<script src="/js/...">`.
*   **Styling:** Bootstrap 5 (CDN via `cdn.jsdelivr.net`). Avoid custom CSS unless absolutely necessary.
*   **Razor Gotcha — ValueTuples and `dynamic`:** Do **not** pass `ValueTuple` types through `ViewData` and cast to `dynamic` in Razor views. Razor's DLR cannot resolve named tuple fields (`Item1`, `Item2`) as properties. Always project tuples into **anonymous objects** before assigning to `ViewData` (e.g., `ViewData["Items"] = list.Select(x => new { x.Name, x.Value }).ToList()`).

## 💾 Data Access & External Services
*   **Database:** SQLite (`VitaTrack.db` relative to the executable). Tables are created on startup via `DbInit.EnsureCreated`.
*   **ORM:** Dapper only (via `IFamilyRepository`, `ISupplementRepository`, etc.). **Do NOT use Entity Framework Core.**
*   **Pattern:** Use the Repository Pattern to abstract Dapper SQL queries away from the business logic (Services).
*   **Foreign Key Constraints:** SQLite enforces foreign keys. When deleting a parent row that has child rows referencing it, you **must** delete the child rows first. The current foreign key relationships are:
    - `SupplementNutrients.SupplementId` → `Supplements(Id)`
    - `PrescribedDoses.SupplementId` → `Supplements(Id)`
    - `PrescribedDoses.FamilyMemberId` → `FamilyMembers(Id)`
    - Always delete in dependency order: `SupplementNutrients` → `PrescribedDoses` → `Supplements` → `FamilyMembers`.
    - When adding new tables with foreign keys, update the relevant repository `DeleteAsync` methods to handle cascade deletes.
*   **Seed Data:** `DbInit.EnsureCreated` seeds test data (family members, supplements, nutrients, prescribed doses) **only when ALL tables are empty** (fresh database). This prevents foreign key errors when partial data is cleared. When adding new entity types, always add corresponding seed data here so reports and E2E tests have realistic data to work with.
*   **Configuration Layering:** `appsettings.json` holds defaults. Environment-specific overrides go in `appsettings.{Environment}.json`. For test environments, create `appsettings.Test.json` with test-specific connection strings. **Do not** rely on `ConnectionStrings__Default` env var via Playwright's `webServer.env` — it does not propagate to `dotnet run` child processes. Use `--environment Test` flag instead.
*   **Error Views:** If `Program.cs` uses `app.UseExceptionHandler("/Home/Error")`, you **must** provide a `Views/Home/Error.cshtml` and a `HomeController.Error()` action. Without them, any controller exception cascades into a bare 500 with no diagnostics.
*   **LLM Integration:** The app uses `LlmService` (reading `VitaTrack:BaseUrl`, `VitaTrack:ApiKey`, `VitaTrack:Model`, `VitaTrack:ReasoningEffort`, and `VitaTrack:Temperature` via `IOptions<VitaTrackOptions>`) to enrich supplements. Any OpenAI-compatible API endpoint works (e.g., OpenRouter, OpenAI, local servers).

## 💻 C# Coding Standards & Style
*   **Modern C#:** Utilize modern C# 10+ features (e.g., file-scoped namespaces, implicit usings, pattern matching).
*   **Formatting & Naming:** Follow `dotnet format` defaults. Use `PascalCase` for public members and `_camelCase` for private fields.
*   **Async/Await:** All asynchronous I/O must be `await`ed. Never use `.Result` or `.Wait()`.
*   **Nullability:** Nullable reference types are enabled (`<Nullable>enable</Nullable>`). Respect nullability strictly; avoid using the null-forgiving operator (`!`) unless necessary. There is **no Roslyn analyzer** that enforces this — it is a convention only, review-determined (ArchitectureReview §2.4). Reviewers should reject gratuitous `!` in PRs; prefer explicit `null` checks, `??`, or refactoring the contract. Document each `!` with a brief inline comment explaining why the compiler's null warning is wrong.
*   **Formatting & Conventions:** `.editorconfig` at the repo root encodes naming (PascalCase public, `_camelCase` private fields, camelCase parameters/constants), brace placement, and analyzer overrides per ArchitectureReview §2.4. Run `./format-check.sh` (which executes `dotnet format VitaTrack.sln --verify-no-changes`) before committing; CI will gate on it. `dotnet format VitaTrack.sln` auto-applies fixes.
*   **Error Handling (Result Pattern):** 
    *   Use a `Result<T>` or `Result` object pattern for logic flow and validation. 
    *   **Do not** use exceptions for control flow. Reserve exceptions strictly for exceptional, unexpected system failures.
*   **Documentation:** Favor highly descriptive, clear naming for variables, methods, and classes over writing comments. 

## 🧪 Testing Philosophy
*   **Framework:** MSTest. Run `dotnet test` and keep the suite green. Tests must verify *actual functionality*.
*   **Architecture Tests:** `VitaTrack.ArchitectureTests` project uses NetArchTest + reflection to enforce rules csproj can't express: Web controllers must not depend on `System.Data`/`Dapper`/`Microsoft.Data.Sqlite`; no assembly transitively references EF Core; `Infrastructure.Data` concrete classes must end in `Repository` (known exception: `DbInit`); no `.cs` file exceeds the 300-line hard limit; controllers must not `catch (Exception)` (currently `[Ignore]` until §2.5 fix). Uses `Types.InAssembly(typeof(Marker).Assembly)` — **not** `InCurrentAssembly()` (would only see the test assembly).
*   **Unit Tests:**
    *   Test business logic in Services and Repositories.
    *   `Moq` is permitted only to mock out dependencies (e.g., Repositories when testing Services, or `HttpClient` for LLM service tests) to isolate the unit under test.
*   **Data Testing:** Use an in-memory SQLite database populated with known seed data for repository/data tests.
*   **Integration/End-to-End Tests:**
    *   Use **Playwright**.
    *   Playwright tests must run against the *real* running application and hit *real* endpoints. Do not mock HTTP responses for these tests.
    *   **Playwright E2E Infrastructure:** Tests live in `e2e-tests/playwright/`. The `global-setup.js` deletes the test DB before each run. The web server starts via `webServer.command` in `playwright.config.js`. **Always use `--environment Test`** in the command (not `webServer.env`) to load `appsettings.Test.json` with the test connection string — Playwright's `env` property does not propagate to `dotnet run` child processes reliably.
    *   **Parallel Execution:** Tests run with `fullyParallel: true`. When tests mutate shared DB state (adding/deleting rows), use dynamic assertions (`.first()`, `.last()`, relative counts) instead of exact values, since workers share the same DB. Prefer creating test-specific data over relying on seed data state.
    *   **Debugging failures:** Playwright drops `test-results/<spec>-chromium/error-context.md` (page snapshot + source) and a screenshot per failure. To save tokens, `grep "Error details" -A5 test-results/*/error-context.md` rather than reading the full ~200-line file; or read the screenshot attachment directly for visual page state.
    *   **Seeding:** Reports (Cost Report, Nutrient Report) depend on `PrescribedDoses` seed data. Always seed PrescribedDoses in `DbInit.EnsureCreated` alongside Supplements and FamilyMembers so report tests have data.

## 🔧 CLI & Git Workflow
*   **Git Commits:** Commit often with descriptive messages prefixed by `feat:`, `fix:`, `refactor:`, `test:`, or `docs:`. Do not commit `bin/`, `obj/`, `*.user`, or populated `appsettings.*.json` (keep only templates).
*   **Commands:**
    *   Build: `dotnet build VitaTrack.sln`
    *   Run Web: `dotnet run --project VitaTrack.Web`
    *   Test: `dotnet test`

## 📋 Story Map
*   **Location:** [`storymap.yaml`](storymap.yaml)
*   **Purpose:** Machine-readable story map capturing all user activities, tasks, and stories with priority, status, and test coverage.
*   **Usage:** Reference when adding new features to understand existing scope and where new stories fit.

## 🤖 AI Workflow Directives
1.  **Understand Context:** Before modifying a file, check how it interacts with the layered folders (Controller -> Service -> Repository).
2.  **Naming:** Ensure generated names clearly describe *intent* without needing supplementary comments.
3.  **Refactoring:** If asked to add a feature to a file nearing 300 lines, stop and refactor the file into smaller components first.