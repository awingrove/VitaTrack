# VitaTrack — Architecture Review & Implementation Guide

**Review date:** 2026-08-05 · **Scope:** `AGENTS.md`, solution structure, layered architecture, tests, automated guardrails.
Use the Prioritized Roadmap (§3) for work order; each item maps to a finding in §2 with file:line evidence.

## 1. What's already good (don't re-do)

- 3-project solution (`VitaTrack.Web → Infrastructure` only; `Tests` refs both) — inter-project direction is **enforced by csproj**, no test needed.
- Dapper + SQLite repository pattern; interfaces in `Infrastructure.Data`, no EF Core.
- Per-project `AGENTS.md` (`Web`, `Infrastructure`, `Tests`) for context-specific rules.
- `SqliteTestBase` gives in-memory SQLite per unit test.
- Playwright E2E starts the real app with `--environment Test`; `global-setup.js` deletes `VitaTrack.Test.db` / `VitaTrack.db`.
- `storymap.yaml`; helper scripts `run.sh`, `test-unit.sh`, `test-e2e.sh`.

## 2. Gaps & Fixes

### 2.1 CSP blocks HTMX in Release — live production bug
`Program.cs` sets `script-src 'self' https://cdn.jsdelivr.net` (applied when `!IsDevelopment()`), but `_Layout.cshtml:43` loads HTMX from `https://unpkg.com/htmx.org@2.0.4`. Release browsers block HTMX → `Enrich`/`UpdateNutrients` dead in production. E2E runs under `Test` (CSP applies, but Chromium doesn't fail a test on a CSP-blocked `<script>` unless the test asserts HTMX behavior), so the suite can't see it.

**Fix:** Self-host `htmx.min.js` under `wwwroot/lib` (preferred — removes the runtime CDN dep) **or** add `https://unpkg.com` to `script-src`. Add an E2E assertion that HTMX actually loads, ideally against a Release build. Reconcile the CDN split (Bootstrap = `cdn.jsdelivr.net`, HTMX = `unpkg.com`) — pick one or self-host both.

### 2.2 `AGENTS.md` contradicts the code
| Root `AGENTS.md` says | Reality |
|---|---|
| "Razor Pages" | **MVC** (`Program.cs` uses `MapControllerRoute`, not Razor Pages) |
| "Interactivity: Vanilla JavaScript" | **HTMX is load-bearing** (`Create.cshtml`, `_NutrientEditor.cshtml`, `_ValidationErrors.cshtml` use `hx-post`/`hx-target`/`hx-swap`/`hx-swap-oob`; `delete-selected.js`/`review.js` are vanilla JS *alongside*) |
| "`/Controllers`, `/Models`, `/Services`, `/Repositories`" single-project layout | **3-project solution**; repos live in `Infrastructure/Data`, services in `Infrastructure/Services` |

**Fix:** Make the root `AGENTS.md` match reality (MVC, HTMX+vanilla JS, 3-project layering). **Commit to HTMX** — do **not** strip it from `VitaTrack.Web/AGENTS.md`. Add a line stating per-project `AGENTS.md` files **supplement** the root; any contradiction is a defect to report, not a license to pick one.

### 2.3 Architecture tests — intra-assembly rules only
Inter-project direction is already enforced by csproj (don't duplicate). Add `VitaTrack.ArchitectureTests` with NetArchTest for rules csproj can't express. Use `Types.InAssembly(...)`, not `InCurrentAssembly()` (the latter would only see the test assembly):

```csharp
Types.InAssembly(typeof(SupplementController).Assembly)
    .That().ResideInNamespace("VitaTrack.Web.Controllers")
    .Should().NotDependOnAny(Types.InGlobalNamespace().That().ResideInNamespace("System.Data"))
    .Check();
```

Also assert: no EF Core assemblies referenced anywhere; repo implementations named `*Repository` and in `Infrastructure.Data`; no `.cs` file exceeds 300 lines; no `catch (Exception)` in controllers (a `Result<T>` discipline proxy — see §2.5).

### 2.4 No automated convention enforcement
`.editorconfig` currently only sets `csharp_style_namespace_declarations = file_scoped`.

**Fix:** Expand `.editorconfig` (naming, `dotnet_diagnostic` `CA1062`/`CA1822`/`CA2200`, treat warnings as errors in CI); add `format-check.sh`; add the 300-line file-size gate as an architecture test (§2.3); add a policy against the null-forgiving operator `!`.

### 2.5 Business logic in controllers — extract services, use `Result<T>`
`ReportingController` computes nutrient/cost dicts inline (`memberTotals`, `memberCosts`). `SupplementController` swallows `Exception` per nutrient in three actions — `Enrich` (`:33`), `UpdateNutrients` (`:91`), `Edit` (`:149`):

```csharp
catch (Exception ex) { _logger.LogError(ex, "Failed to add nutrient {GenericName} ..."); }
```

A partially-applied nutrient set is silently committed. Root `AGENTS.md` already mandates `Result<T>` but it isn't used; `LlmService.LlmResult` with `ExtractionError` is the precedent but not generic.

**Fix:** Introduce `ReportingService`/`IReportingService` (`NutrientReportResult`, `CostReportResult`); `SupplementNutrientService.ReplaceAsync(supplementId, nutrients)` for the delete-then-readd flow; add a generic `Result<T>` and return per-nutrient failures the controller maps to the view:

```csharp
public record Result<T> { public bool IsSuccess { get; init; } public T? Value { get; init; } public string Error { get; init; } = string.Empty; }
```

### 2.6 Controllers bind infrastructure entities directly
`Enrich(Supplement supplement)` (`:33`), `UpdateNutrients(int, List<SupplementNutrientDto>)` (`:91`), `Edit(int id, Supplement supplement)` (`:149`) bind domain entities — overpost/mass-assignment risk. (`Create(Supplement)` is often cited but `Create()` is GET-only; the risk is real, just at different actions.)

**Fix:** `CreateSupplementRequest` / `EditSupplementRequest` / `ReplaceNutrientsRequest`; map to entities in the controller. Make this the default for new features.

### 2.7 E2E flakiness — go in-memory, stay parallel
`playwright.config.js` shares one file-backed `VitaTrack.Test.db` with `fullyParallel: true`; `workers: process.env.CI ? 1 : undefined` never fires today (no CI — §2.8). Two distinct races: (1) SQLite file-lock ("database is locked") — a storage race; (2) logical cross-test interference — all workers share the one web server's DB.

**Fix:** Web server uses in-memory SQLite (`Data Source=:memory:` shared cache) → eliminates race #1 outright. **Keep `fullyParallel: true`** (don't set `workers: 1`). Race #2 is **not** solved by storage — keep enforcing root `AGENTS.md`'s dynamic-assertions / test-specific-data rule (`.first()`, `.last()`, relative counts; create test data over depending on seed). Per-worker DB isolation (one server per worker, distinct ports + DBs) is heavyweight — not worth it now.

Side: `appsettings.Test.json` is committed though root `AGENTS.md` says "keep only templates" — gitignore it and ship a `.template`, or document the exception (test infra needs a concrete value to point at the in-memory DB).

### 2.8 No CI
No `.github/workflows`. Add (note the `CI` env var so the `playwright.config.js` workers/retries logic actually fires):

```yaml
name: Build & Test
on: [push, pull_request]
jobs:
  build:
    runs-on: ubuntu-latest
    env: { CI: 'true' }
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: '10.0.x' }
      - run: dotnet format VitaTrack.sln --verify-no-changes
      - run: dotnet build VitaTrack.sln
      - run: dotnet test VitaTrack.sln
      - run: ./test-e2e.sh
```

### 2.9 `LlmService` approaching the 300-line limit (283 lines)
Mixes `FetchPageHtmlAsync` / `CleanHtml` / `ExtractNutrientsWithLlmAsync` / `BuildExtractionPrompt`. Split into `HtmlScraperService`, `SupplementLabelParser`, `LlmClient` before adding features.

### 2.10 No ADRs
No `docs/adr/` explaining: pragmatic MVC over Clean Architecture; Dapper over EF Core; SQLite over PostgreSQL; no auth. Risk: future agents add abstractions that fight the original intent. Add `docs/adr/`.

### 2.11 No coverage gate
Add `coverlet`; CI gate ≥80% on `Infrastructure` per root `AGENTS.md`.

## 3. Prioritized Roadmap

**High** (correctness first, then guardrails)
1. CSP/HTMX fix (§2.1) — live production bug.
2. Correct root `AGENTS.md` (§2.2) — wrong docs make agents break the app.
3. Architecture tests (§2.3).
4. `ReportingService` + `SupplementNutrientService` (§2.5).
5. `.editorconfig` + `dotnet format` CI check (§2.4).
6. GitHub Actions (§2.8).

**Medium**
7. `Result<T>`; remove swallowed `catch (Exception)` (§2.5).
8. Input DTOs for `Enrich`/`UpdateNutrients`/`Edit` (§2.6).
9. Split `LlmService` (§2.9).
10. In-memory web server, keep parallel (§2.7).
11. `coverlet` gate (§2.11).

**Low**
12. `appsettings.Test.json` template policy (§2.7).
13. `docs/adr/` (§2.10).
14. Pre-commit hook (`dotnet format` + architecture tests).
15. Update NuGet packages (`Microsoft.NET.Test.Sdk` 17.8.0 is aging).