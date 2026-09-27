# In-App Service Connection — design

**Date:** 2026-09-26 · **Status:** approved for planning · **Reviewer:** Alex Wingrove (design-review gate, `docs/factory/design-review.md`)

## Decision statement

Move the LLM API connection and its settings out of `appsettings.json` / environment variables and
into the application, behind an in-app "connect to service" flow, so that a user can connect an
OpenAI-compatible endpoint and choose a model without editing configuration. OpenCode ships as the
first supported service; the design admits more.

This is not mechanical: it decides where credential state lives, whether `IOptions<VitaTrackOptions>`
survives, and whether the LLM slice keeps depending on configuration. It alters interfaces other code
depends on. It therefore took the design-before-code gate.

## Options considered

1. **New `ServiceConnections` slice, N-capable schema, data-record descriptors. (Chosen.)** A new
   bounded context owning connection persistence and the service catalog; one descriptor (OpenCode)
   ships. Adding a service is a new record in a list.
2. **Same slice, single-connection schema, migrate when a second service arrives.** Defensible, but
   defers a migration onto a table that will hold a credential — the worst place to have one.
3. **Fold settings into the existing `LlmEnrichment` slice.** Smallest diff, but directly contradicts
   "many services eventually": a connection to a non-LLM service would live in a slice named
   `LlmEnrichment`, and the second service becomes a rename-plus-refactor. `docs/factory/new-shard.md`
   is explicit that a new user-facing capability deserving its own bounded context gets its own slice.

Blast radius: new shard in `shards.yaml`, new activity in `storymap.yaml`, new table, new controller
and three views, a new nav item, `LlmClient` / `ILlmClient` / `LlmService` reshaped, `VitaTrackOptions`
deleted, `Program.cs` / `appsettings.json` / `test-e2e.sh` / `AGENTS.md` / `nfr.md` / `DESIGN.md`
updated, five unit test classes reworked, one e2e spec re-pointed and one added.

Rollback: revert the change. There is no data migration — the table is new and no existing state is
rewritten. Reverting restores the config path, which is why the config removal is committed in the
same change rather than staged behind a flag.

---

## Architecture

### Storage is the source of truth; configuration is removed

The database is the only home for connection state. `builder.Services.Configure<VitaTrackOptions>(…)`
is deleted from `Program.cs:5`, `VitaTrackOptions.cs` is deleted, the `VitaTrack` block is removed from
`appsettings.json`, and `test-e2e.sh` stops exporting `VitaTrack__ApiKey`.

Nothing is seeded from configuration. The committed `appsettings.json` ships `"ApiKey": ""` — the only
real value ever lived in the environment variable, and a single-user local app can re-enter a key in
thirty seconds. A fallback or seed path would preserve two ways to hold a credential with a silent
precedence rule nobody remembers.

**Consequence, stated plainly:** after this change there is no headless way to set a key. Any CI or
scripted path that needs enrichment must go through the settings surface. That is correct, and it is
the reason the new e2e test configures a connection through the UI.

### Slice boundary and dependency direction

New slice `SC` — `VitaTrack.Core/Features/ServiceConnections/`. The dependency is **one-way**:

```
LlmEnrichment  ──(IServiceConnectionRepository, interface only)──▶  ServiceConnections
ServiceConnections  ──(nothing)──▶  LlmEnrichment
```

`ServiceConnections` needs nothing from `LlmEnrichment` **because probing is a `GET /v1/models` and
never an inference call.** Had the probe retained the inference fallback, `ServiceConnections` would
have needed the chat client and the two slices would be circular. That single decision is what keeps
the graph acyclic.

`shards.yaml` gives `LLM` `tables: []` and it stays `[]` — `LlmService` takes an
`IServiceConnectionRepository` interface, never a table name. `CrossSliceSqlTests` scans SQL string
literals, so an interface reference passes untouched. `ServiceConnections` declares its own table.

### Service abstraction: a data record, not a behavior interface

```csharp
public sealed record ServiceDescriptor(
    string ServiceId,
    string DisplayName,
    string DefaultBaseUrl,
    Func<IReadOnlyDictionary<string, string>> HeaderFactory);
```

Every supported service is OpenAI-compatible, so **model listing and request shaping are one generic
code path**, not one per service. The descriptor supplies only what genuinely differs: identity,
label, default URL, headers. `HeaderFactory` is a delegate rather than a dictionary because
`x-opencode-session` needs a fresh value per process, not a static string.

Adding OpenRouter, a local gateway, or another OpenCode-compatible endpoint is a new record in a list —
no new class, no new code path, no test rewrite. A service needing a genuinely different protocol gets
promoted to an interface at that point, with evidence, rather than paying for the seam speculatively.

**Shipped descriptor — `opencode`:**

| | |
|---|---|
| `ServiceId` | `opencode` |
| `DisplayName` | `OpenCode` |
| `DefaultBaseUrl` | empty — the user must supply it; the gateway URL is not ours to assume |
| `HeaderFactory` | `{ "x-opencode-session": <process session id> }` |

`User-Agent: VitaTrack/1.0 (+https://github.com/awingrove/VitaTrack)` stays a constant on the HTTP
client; it is not service-specific and does not belong in the descriptor.

### Session id lifetime changes

`LlmClient.cs:15` generates a `Guid` per instance and the client is registered `AddScoped`, so a new id
is minted on every request — which makes the header nearly useless for correlating anything. The id
becomes a **per-process singleton** generated once at startup, so every request in a run shares it and
a restart begins a new session.

### `HttpClient` — the factory delegate is deleted

`ServiceCollectionExtensions.cs:65-77` currently sets `BaseAddress` and the `Authorization` header from
`IOptions<VitaTrackOptions>` inside `AddHttpClient("llm", …)`. `IHttpClientFactory` caches the handler,
so **if the configure delegate kept reading options after this change, a new connection would silently
do nothing** for the handler's lifetime — a "works on my machine" bug where the old key keeps being
sent. This is the highest-risk part of the change.

Instead: the `"llm"` client is registered with **no** configure delegate; **`BaseAddress` is never
set**; every request builds an absolute `Uri` from the connection's `BaseUrl` (no relative-URL or
trailing-slash hazard); and `Authorization` plus the descriptor's headers are set **per request** from
the resolved connection.

### `LlmClient` / `LlmService`

`LlmService` takes `IServiceConnectionRepository` and resolves the active connection itself, so
`EnrichSupplementAsync(supplement)` keeps its signature and the controller stays thin. `ILlmClient`
gains the connection and generation settings as explicit parameters; it no longer reads options.
`Authorization` moves to a per-request header (it currently lives in the `AddHttpClient` configure
delegate, `ServiceCollectionExtensions.cs:70`); the `x-opencode-session` header stays where it is,
`LlmClient.cs:26`.

**The `?? "gpt-4o-mini"` default at `LlmClient.cs:31` is deleted.** A hardcoded vendor default in a
service-agnostic layer is the coupling being removed, and a silent default is invisible until it bills
you.

---

## Data

```sql
CREATE TABLE IF NOT EXISTS ServiceConnections (
    Id           INTEGER PRIMARY KEY AUTOINCREMENT,
    Service      TEXT    NOT NULL,   -- descriptor id, e.g. 'opencode'
    BaseUrl      TEXT    NOT NULL,
    ApiKey       TEXT    NOT NULL,
    Model        TEXT    NULL,       -- NULL = unset; enrichment refuses and points at Settings
    Variant      TEXT    NULL,       -- see "Variants" below; NULL = provider default
    MaxTokens    INTEGER NOT NULL DEFAULT 16384,
    Temperature  REAL    NOT NULL DEFAULT 1.0,
    Verification TEXT    NOT NULL,   -- 'verified' | 'unverified'
    VerifiedAt   TEXT    NULL,
    IsActive     INTEGER NOT NULL DEFAULT 0,
    CreatedAt    TEXT    NOT NULL,
    UpdatedAt    TEXT    NOT NULL
);
```

`ReasoningEffort` from `VitaTrackOptions` is **replaced by** `Variant` — a variant *is* a reasoning
effort, and keeping both would create two fields fighting over one request key. `MaxTokens` and
`Temperature` move here from configuration; leaving them in `appsettings.json` would violate the
decision to remove config.

No foreign keys. Connections are orthogonal to domain data — deleting a connection must never touch a
supplement. `IsActive` is the extension point for a second service: adding a service inserts a row and
flips the flag. `ServiceConnectionRepository.DeleteAsync` has no cascade to implement, which is worth
stating because AGENTS.md's FK-cascade rule would otherwise be silent about it.

### Seed data: deliberately none

AGENTS.md requires seed data for new entity types so reports and e2e have realistic data. **Seeding a
credential is wrong**, so this is a recorded exception: the table starts empty, and the e2e suite
creates its connection through the UI. The exception is written into the register in the same change,
not left as a silent gap.

### Secret handling

Plaintext `TEXT`. `*.db` is already gitignored and no `.db` file is tracked, so the credential is not
at risk of being committed. The app already holds the key in process memory, and with no auth
(ADR-0004) the trust domain is "whoever can read this user's files" — which is also whoever could read
the old config.

What genuinely protects it, and is therefore tested: the key is **never logged**, is **masked in the UI**
(last four characters only), and appears in **no rendered settings markup**.

`docs/quality/nfr.md` currently states secrets travel "via `IOptions`". That sentence becomes false and
is rewritten. Worth stating in the spec: a credential that was "in a config file a user can see"
becomes "in a database file a user is less likely to think about." Same machine, same trust domain, a
different mental model for whoever later wonders whether the key is in there.

---

## The flow

Two phases, HTMX, in the repo's existing idiom (`hx-post` / `hx-target` / `hx-swap`).

**`GET /ServiceConnection`** renders one of three states.

### State 1 — not connected

Connect form: service selector (one option today), base URL (prefilled from the descriptor), API key
(password field). Submit is an HTMX POST.

### The connect POST

**The key is always saved, whatever the probe returns.** (This revises the earlier "failure does not
save" phrasing — a failed probe still leaves a usable connection, marked unverified.) The handler:

1. Validates the form.
2. Saves the connection as the active row, preserving the previous `Model` text.
3. Probes `GET {BaseUrl}/v1/models` with the connection's `Authorization` and the descriptor's headers.
4. On `2xx`: `Verification = 'verified'`, `VerifiedAt` stamped, catalog parsed. Swaps in the
   **model picker**.
5. On anything else (401, 404, 405, DNS failure, connection refused): `Verification = 'unverified'`.
   Swaps in the **free-text model field** plus a warning alert.

A `404` is *not* a failure of the key. Several OpenAI-compatible endpoints do not implement model
listing, and a proxy may block `/v1/models` while allowing completions. The badge means "we could not
confirm this; first real use is the test."

### State 2 — connected, verified

Shows service, base URL, masked key, a **model dropdown populated from the catalog**, a variant
dropdown, max tokens, temperature. Plus "Re-test connection" and "Disconnect".

### State 3 — connected, unverified

Same form, with: a persistent warning alert — *the key could not be verified; it will be used on first
enrichment* — a **free-text model field** prefilled with the last value that worked, and a visible
"unverified" badge. The badge also appears on the Enrich flow, so the failure surfaces where it
happens rather than only where it was configured.

### Variants are a fixed vocabulary, not discovered

**`/v1/models` does not return variants.** The standard OpenAI-compatible response is
`{ id, object, created, owned_by }`. The `variants` data is opencode's own catalog, served by no
documented HTTP endpoint for this purpose; no field name is assumed.

So the model dropdown comes from the endpoint, and the variant dropdown offers a documented vocabulary:
`none`, `low`, `medium`, `high`, `xhigh`, `max` — the values `reasoning_effort` accepts across the
current model families. If a provider rejects a combination, its error surfaces and the user picks
another. The set is recorded in `AGENTS.md` alongside the dosage-unit vocabulary already maintained
there, and is extended by editing the list.

This is a **real reduction from the original ask**: variants are offered from a fixed set rather than
discovered per model. Fetching the real catalog (`models.dev`, which opencode itself uses) would give
genuine per-model data but adds a runtime external dependency, a cache, a staleness policy, and a new
failure mode to a local app that currently has none. The endpoint tells you which models exist; the
vocabulary tells you which settings are offered; the provider is the authority on whether the
combination works.

### The hard-fail path

No active connection, or `Model` is `NULL` → `LlmResult.ExtractionError` naming Settings. The user is
sent to fix it rather than being handed a default. Also: a `Variant` the catalog no longer offers is
still sent — the catalog is a snapshot, and the provider's error is the feedback.

---

## Files

**New — `VitaTrack.Core/Features/ServiceConnections/`:** `ServiceConnection.cs`,
`IServiceConnectionRepository.cs`, `ServiceConnectionRepository.cs` (Dapper, owns the table),
`ServiceDescriptor.cs`, `ServiceDescriptorRegistry.cs`, `ServiceCatalogClient.cs`, `ModelCatalog.cs`,
`ConnectServiceRequest.cs`, `SelectModelRequest.cs`, `SaveConnectionHandler.cs`,
`ProbeConnectionHandler.cs`. Single-purpose and small per `new-shard.md`; several will be under 30 lines.

**New — web:** `Controllers/ServiceConnectionController.cs`,
`Views/ServiceConnection/{Index,_ConnectForm,_ModelPicker}.cshtml`, and a **Settings** nav item in
`Views/Shared/_Layout.cshtml:21-28`.

**Modified:** `LlmClient.cs`, `ILlmClient.cs`, `LlmService.cs`; `ServiceCollectionExtensions.cs`
(drop the `"llm"` configure delegate, register the new slice and the session-id singleton);
`Data/DbInit.cs` (table + the recorded no-seed exception); `shards.yaml` (new `SC` slice);
`storymap.yaml` (new activity + tasks, each with an `entry_point` and test refs);
`Program.cs`; `appsettings.json`; `test-e2e.sh`; `DESIGN.md`; `docs/quality/nfr.md`; root `AGENTS.md`;
the e2e suite; five unit test classes.

**Deleted:** `VitaTrack.Core/VitaTrackOptions.cs`; the `Configure<VitaTrackOptions>` call; the
`VitaTrack` block; the `VitaTrack__ApiKey` export.

### `DESIGN.md` amendment (in the same change)

This change introduces patterns the design contract does not cover. Amend it rather than leave it
stale: the **three-state connection indicator** (disconnected / connected-verified /
connected-unverified) as a documented pattern, the **dependent model→variant dropdown**, the
**Settings page** in the nav, and button intent classes for Connect (create = `btn-success`),
Re-test (edit = `btn-sm btn-primary`) and Disconnect (delete = `btn-sm btn-danger`).

---

## Testing

**Unit** — repository against in-memory SQLite; `ServiceCatalogClient` against a stubbed
`HttpMessageHandler` (Moq is permitted for `HttpClient`); `HeaderFactory` shape; resolution across the
three states; the absolute-URL construction that replaced `BaseAddress`; the `NULL`-model refusal.

**Architecture** — the new shard's ownership and table declarations in `shards.yaml`; and a **guard
that configuration binding is gone**, grep-backed, so nobody quietly reinstates
`IOptions<VitaTrackOptions>` or the `VitaTrack:` config key. `UiReachabilityTests` covers the Settings
page once the nav item exists.

**E2E — new: `service-connection.spec.js`.** A Playwright fixture starts a **real Node HTTP server on
an ephemeral port per test**, serving `GET /v1/models` and `POST /v1/chat/completions` in
OpenAI-compatible shape and recording received requests. The test enters `http://127.0.0.1:<port>` as
the base URL. It asserts: connect → verified → model picker populated → variant selected → the `Authorization`
header and the chosen variant reached the stub; and the unverified path → warning shown → connection
saved → Enrich refuses with a pointer to Settings.

Per-test and ephemeral rather than a second `webServer` entry: `fullyParallel: true` means a fixed port
is shared by every worker and stub state would leak across parallel tests.

**The no-mock rule is amended, not bent.** AGENTS.md currently says *"Do not mock HTTP responses for
these E2E tests."* The intent is to prevent faking the app's own HTTP layer so a test passes without
exercising the app. A separate listening socket standing in for a third-party vendor is different: the
app is unmodified, the transport is real, status codes and JSON parsing are real. The rule is rewritten
as *"never intercept the app's own HTTP; a test-double server for an external dependency is fine"* so the
next agent does not re-derive it.

**E2E — existing.** `supplement-llm-integration.spec.js` re-points from the env-var skip to skipping on
"no active connection", and stays as the single test that talks to a genuine provider. The stub tests the
wiring; that one tests the world.

**The stub is not a substitute for the manual check.** The AGENTS.md HTMX checklist already calls for
verifying spinner visibility and rapid-click protection on this kind of flow; those are manual.

---

## Decisions recorded so they are not re-litigated

1. **OpenAI-compatible gateway, not `opencode serve`.** The request shape does not change, and the
   service abstraction is better served by a descriptor than by coupling to opencode's client API.
2. **Database only; config and env var removed.** No seed, no fallback, no precedence rule.
3. **Plaintext credential**, gitignored DB, masked, never logged, tested.
4. **`UrlSafetyValidator` is deliberately NOT applied to the LLM base URL.** It guards only
   `HtmlScraperService.cs:16` on the user's manufacturer URL and stays there. Applying it would reject
   `http://` outright and reject loopback, killing local Ollama, LM Studio, and the e2e stub. The threat
   it mitigates requires a privilege boundary to cross; this app is single-user with no auth
   (ADR-0004), so the only person who can enter that URL is the person who runs the app. **A future
   agent finding an unvalidated URL here should read this before "fixing" it.**
5. **Fixed variant vocabulary, not per-model discovery** — `/v1/models` does not carry variants.
6. **Descriptor as data, not a behavior interface** — one generic OpenAI-compatible code path.
7. **Per-process session id** — stable within a run, fresh across restarts.
8. **No seed row** for a credential table; a recorded exception to the AGENTS.md seeding rule.

## Out of scope

- **A second service.** The schema and descriptor list admit it; none ships. The service selector
  renders with one option, which is a visible consequence of this scope, not an oversight.
- **Multiple simultaneous connections.** `IsActive` exists for the future; the UI manages exactly one.
- **Editing `MaxTokens` / `Temperature` in the UI** beyond what the form already needs — the fields
  exist on the row and render, but they are not a primary interaction.
- **Encrypted credential storage at rest.** Rejected: same trust domain, and it makes the credential
  harder to migrate when the second service arrives.
- **Model listing from a remote catalog** (`models.dev`).
- **Any change to the enrichment prompt or the nutrient JSON contract.** `LlmService`'s parsing is
  untouched.
