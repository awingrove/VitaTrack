# AI Agent Instructions (AGENTS.md)

This document defines the coding standards, architectural guidelines, testing philosophy, and tech stack for the VitaTrack (Vitamin and Supplement Tracking) ASP.NET application. 

**AI Agents:** Read and adhere to these rules strictly before generating, refactoring, or modifying code in this repository.

## 🏗️ Architecture & Project Structure
*   **Paradigm:** Pragmatic Pure ASP.NET MVC. Avoid over-engineering and strict Clean Architecture dogmas.
*   **Structure:** Use traditional layered folders (`/Controllers`, `/Models`, `/Services`, `/Repositories`). Keep it simple and navigable. Keep business logic in Services/Repositories; Web layer only handles HTTP, views, and thin mapping.
*   **File Size & Organization:** 
    *   Proactively extract classes/interfaces into separate files if a file exceeds 20-30 lines. Keep methods small (< 30 lines) and focused.
    *   **Hard Limit:** No single file should exceed **300 lines**. Refactor immediately if a file approaches this limit.

## 🖥️ UI & Frontend Stack
*   **Views:** Razor Pages (`.cshtml`) using the `_Layout.cshtml` template.
*   **Interactivity:** Use **HTMX** for AJAX-style updates (`hx-get`, `hx-post`, `hx-target`).
*   **Styling:** Bootstrap 5. Avoid custom CSS unless absolutely necessary.

## 💾 Data Access & External Services
*   **Database:** SQLite (`VitaTrack.db` relative to the executable). Tables are created on startup via `DbInit.EnsureCreated`.
*   **ORM:** Dapper only (via `IFamilyRepository`, `ISupplementRepository`, etc.). **Do NOT use Entity Framework Core.**
*   **Pattern:** Use the Repository Pattern to abstract Dapper SQL queries away from the business logic (Services).
*   **LLM Integration:** The app uses `OpenRouterLlmService` (reading `OpenRouter:BaseUrl` and `OpenRouter:ApiKey` from config) to enrich supplements. 

## 💻 C# Coding Standards & Style
*   **Modern C#:** Utilize modern C# 10+ features (e.g., file-scoped namespaces, implicit usings, pattern matching).
*   **Formatting & Naming:** Follow `dotnet format` defaults. Use `PascalCase` for public members and `_camelCase` for private fields.
*   **Async/Await:** All asynchronous I/O must be `await`ed. Never use `.Result` or `.Wait()`.
*   **Nullability:** Nullable reference types are enabled (`<Nullable>enable</Nullable>`). Respect nullability strictly; avoid using the null-forgiving operator (`!`) unless necessary.
*   **Error Handling (Result Pattern):** 
    *   Use a `Result<T>` or `Result` object pattern for logic flow and validation. 
    *   **Do not** use exceptions for control flow. Reserve exceptions strictly for exceptional, unexpected system failures.
*   **Documentation:** Favor highly descriptive, clear naming for variables, methods, and classes over writing comments. 

## 🧪 Testing Philosophy
*   **Framework:** MSTest. Run `dotnet test` and keep the suite green. Tests must verify *actual functionality*.
*   **Unit Tests:**
    *   Test business logic in Services and Repositories.
    *   `Moq` is permitted only to mock out dependencies (e.g., Repositories when testing Services, or `HttpClient` for LLM service tests) to isolate the unit under test.
*   **Data Testing:** Use an in-memory SQLite database populated with known seed data for repository/data tests.
*   **Integration/End-to-End Tests:**
    *   Use **Playwright**.
    *   Playwright tests must run against the *real* running application and hit *real* endpoints. Do not mock HTTP responses for these tests.

## 🔧 CLI & Git Workflow
*   **Git Commits:** Commit often with descriptive messages prefixed by `feat:`, `fix:`, `refactor:`, `test:`, or `docs:`. Do not commit `bin/`, `obj/`, `*.user`, or populated `appsettings.*.json` (keep only templates).
*   **Commands:**
    *   Build: `dotnet build VitaTrack.sln`
    *   Run Web: `dotnet run --project VitaTrack.Web`
    *   Test: `dotnet test`

## 🤖 AI Workflow Directives
1.  **Understand Context:** Before modifying a file, check how it interacts with the layered folders (Controller -> Service -> Repository).
2.  **Naming:** Ensure generated names clearly describe *intent* without needing supplementary comments.
3.  **Refactoring:** If asked to add a feature to a file nearing 300 lines, stop and refactor the file into smaller components first.