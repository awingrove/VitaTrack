# Non-Functional Requirements

Reference for the slices that touch these concerns. Existing controls are noted; gaps are
named explicitly so they are not silently assumed.

## Security

- **CSP:** `Program.cs` sets `script-src 'self' https://cdn.jsdelivr.net` in non-Dev envs
  (no `'unsafe-inline'`). Self-host JS under `wwwroot/lib` or `wwwroot/js`; partials may
  include their own `<script src>` (htmx re-executes external scripts on swap).
- **Auth:** none. The app is single-user/local by design (per ADR-0001). Do not add
  auth implicitly — a slice needing multi-tenant isolation is an ADR.
- **Secrets:** the service API key is a **plaintext `TEXT` column** (`ServiceConnections.ApiKey`)
  that the user pastes into the Settings form. It is masked to its last four characters in the UI
  and nowhere else — the raw value is never rendered, never reaches a view model in full, and is
  never logged (DL-005 governs every log line that can see a connection; see root `AGENTS.md`).
  The database file is gitignored (`*.db`, `*.db-wal`, `*.db-shm`), so a locally-entered key is
  not committed. There is no encryption at rest: the app is single-user and local by design
  (per ADR-0001), so anyone who can read the file already has the app's whole database. If that
  assumption ever changes, encrypting this column is an ADR, not a patch. `appsettings.Test.json`
  holds only a connection string (no secrets).
- **SQL:** parameterised Dapper only; no string-concatenated SQL (injection surface).
- **SSRF — a deliberate gap, not an oversight.** `UrlSafetyValidator` (which blocks
  loopback, link-local, and RFC1918 targets) is applied to the *manufacturer* URL the
  scraper fetches, and is **deliberately not** applied to the LLM base URL the user
  types into Settings. Reasons: the target is the user's own machine, entered by that
  same user, for their own enrichment calls; the common deployment is a provider or a
  local gateway on `localhost`, which the validator would reject by default; and the
  app is single-user and local (ADR-0001), so there is no lower-trust party choosing the
  URL. Do not "fix" this by adding the validator to `ServiceCatalogClient` or
  `LlmClient` — a local OpenAI-compatible server is a supported configuration. If the app
  ever gains auth or a multi-tenant deployment, this decision is an ADR.

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
