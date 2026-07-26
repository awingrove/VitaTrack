# VitaTrack.Web – UI Layer

## Responsibilities
- Serve HTTP requests (MVC controllers).
- Render Razor views with Bootstrap 5 and HTMX.
- Map view models to/from infrastructure models (thin mapping only).
- Handle form validation (data‑annotations).
- No business logic; delegate to services/repositories.

## Conventions
- Controllers: suffix `Controller`, inherit from `Controller`.
- Actions: return `IActionResult`; prefer `async Task<IActionResult>`.
- Use `[HttpGet]`/`[HttpPost]` attributes explicitly.
- Keep controllers thin: call repository or service, map result, return view.
- No direct `IDbAccess` or service instantiation; rely on DI.

## Views
- Located under `Views/<Controller>/`.
- Layout: `Views/Shared/_Layout.cshtml`.
- Use `@model` with infrastructure model types (e.g., `IEnumerable<FamilyMember>`).
- HTMX attributes: `hx-get`, `hx-post`, `hx-target`, `hx-swap`.
- Avoid inline JavaScript; keep in separate `.js` files under `wwwroot/js` if needed.

## Static Files
- Place custom CSS/JS/images in `wwwroot/css`, `wwwroot/js`, `wwwroot/img`.
- Reference via relative paths (`/css/site.css`).
- Bootstrap and HTMX loaded from CDN in `_Layout.cshtml` (fallback to local if needed).

## Dependencies
- References: `VitaTrack.Infrastructure` (projects), Dapper, SQLite, Microsoft.AspNetCore.Mvc, etc.
- Do **not** add direct data‑access code here; use repositories via constructor injection.

## Testing
- UI logic tested via `Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory` (in VitaTrack.Tests).
- Unit tests must pass before considering a feature complete; the aim of unit testing is to verify that a piece of functionality is defect‑free under the tested conditions.
- Keep unit tests thin; focus on repository and service layers.

## Build
- `dotnet build VitaTrack.Web.csproj` (or via solution).
- Publish: `dotnet publish -c Release -r win-x64 --self-contained true` (outputs EXE that launches browser).
