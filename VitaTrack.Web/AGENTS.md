# VitaTrack.Web – UI Layer

## Responsibilities
- Serve HTTP requests (MVC controllers).
- Render Razor views with Bootstrap 5 and HTMX.
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
- **ViewData/ViewBag:** Pass data via `ViewData["Key"]`. Use **anonymous objects** (not ValueTuples) when passing structured data — Razor's `dynamic` context cannot resolve ValueTuple named fields. Example: `ViewData["Items"] = list.Select(x => new { x.Name, x.Value }).ToList()`.

## Static Files
- Place custom CSS/JS/images in `wwwroot/css`, `wwwroot/js`, `wwwroot/img`.
- Reference via site-relative paths (`/css/site.css`, `/js/review.js`).
- **CSP `script-src 'self' cdn.jsdelivr.net`** (`Program.cs`, no `'unsafe-inline'` in non-Dev). Consequences:
  - **No inline `<script>` blocks** in `.cshtml` — they are silently dropped in Release. Put JS in external `.js` under `wwwroot/js` (or `wwwroot/lib`) and reference via `<script src="/js/...">`.
  - htmx re-executes external `<script src>` tags in swapped partials, so a partial can include its own `<script src="/js/...">` to (re)wire up after swap. Use this pattern for partials that need JS (e.g., `_NutrientEditor.cshtml` includes `nutrient-editor.js`).
- HTMX is self-hosted at `/lib/htmx/htmx.min.js` (no CDN; satisfies `'self'`).
- Bootstrap 5 loaded from `cdn.jsdelivr.net` in `_Layout.cshtml`.

## Dependencies
- References: `VitaTrack.Infrastructure` (projects), Dapper, SQLite, Microsoft.AspNetCore.Mvc, etc.
- Do **not** add direct data‑access code here; use repositories via constructor injection.

## Error Handling
- If `Program.cs` registers `app.UseExceptionHandler("/Home/Error")`, you **must** also provide:
  - A `HomeController.Error()` action that returns `View()`.
  - A `Views/Home/Error.cshtml` view.
- Without these, any unhandled exception cascades into a bare 500 with no diagnostics.

## Testing
- UI logic tested via `Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory` (in VitaTrack.Tests).
- Unit tests must pass before considering a feature complete; the aim of unit testing is to verify that a piece of functionality is defect‑free under the tested conditions.
- Keep unit tests thin; focus on repository and service layers.

## Build
- `dotnet build VitaTrack.Web.csproj` (or via solution).
- Publish: `dotnet publish -c Release -r win-x64 --self-contained true` (outputs EXE that launches browser).
