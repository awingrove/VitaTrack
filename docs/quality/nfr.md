# Non-Functional Requirements

Reference for the slices that touch these concerns. Existing controls are noted; gaps are
named explicitly so they are not silently assumed.

## Security

- **CSP:** `Program.cs` sets `script-src 'self' https://cdn.jsdelivr.net` in non-Dev envs
  (no `'unsafe-inline'`). Self-host JS under `wwwroot/lib` or `wwwroot/js`; partials may
  include their own `<script src>` (htmx re-executes external scripts on swap).
- **Auth:** none. The app is single-user/local by design (per ADR-0001). Do not add
  auth implicitly — a slice needing multi-tenant isolation is an ADR.
- **Secrets:** `VitaTrack:*` options (BaseUrl, ApiKey, Model, …) via `IOptions`, never
  logged. `appsettings.Test.json` holds only a connection string (no secrets).
- **SQL:** parameterised Dapper only; no string-concatenated SQL (injection surface).

## Performance

- In-memory SQLite for tests; file SQLite (`VitaTrack.db`) in prod. Queries are simple
  primary-key / foreign-key lookups; no N+1 loops expected, but `ReportingService`
  caches per-request (`GetCachedAsync`) to avoid re-fetching supplements/family.
- Keep report aggregation in SQL where possible; the current in-memory aggregation is O(n)
  over active doses and is fine at this scale. Revisit if dose count grows large.

## Accessibility

- Bootstrap 5 defaults; semantic markup in Razor views. Interactive controls (HTMX buttons,
  row selection) must be keyboard-operable. No custom CSS that breaks focus order.

## Operability

- **Error page:** `Program.cs` uses `app.UseExceptionHandler("/Home/Error")`; the matching
  `Views/Home/Error.cshtml` + `HomeController.Error()` must exist (a missing handler
  cascades to a bare 500).
- **Diagnostics:** keep error views and the global handler intact; never swallow exceptions
  into control flow (use result records, per `AGENTS.md`).
- **Cache:** Kestrel serves static files with only `ETag`/`Last-Modified` — so
  `<script>/<link>` to `wwwroot` MUST use `asp-append-version="true"` to avoid stale-JS
  no-ops.

## Gaps (named, not yet addressed)

- No automated accessibility audit in CI.
- No performance budget / load test.
- No structured logging; failures surface via the error view only.
