# In-App Service Connection Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move the LLM API connection and its settings out of configuration into the application, behind an in-app "connect to service" flow that probes the endpoint, lists models, and lets the user pick a model and variant. OpenCode ships as the first supported service.

**Architecture:** A new `ServiceConnections` vertical slice owns a `ServiceConnections` table and the service catalog. Services are **data records** (`ServiceDescriptor`), not behavior interfaces — every supported service is OpenAI-compatible, so model listing and request shaping are one generic code path. `LlmEnrichment` depends on `ServiceConnections` **one-way, through an interface only**; its `tables: []` declaration in `shards.yaml` does not change. The database is the only source of truth — `VitaTrackOptions`, the `VitaTrack` config block, and the `VitaTrack__ApiKey` env var are all deleted.

**Task order is load-bearing: 1 → 2 (tracer) → 3 → 4 → 5 → 6 → 7 → 8.** Task 2 is the vertical path
(`new-shard.md` step 3) and lands second, deliberately. The original order ran three horizontal tasks before
anything was observable, which is the factory's own recipe violated by the factory's own plan. Task 2 restores
it, and it is also what makes the Task 4 refactor safe to review: before Task 4 nothing reads the table; after
it, everything does.

**Tech Stack:** ASP.NET Core MVC (Razor, HTMX), Dapper + SQLite, MSTest + Moq, YamlDotNet, NetArchTest, Playwright (Node).

**Spec:** `docs/superpowers/specs/2026-09-26-in-app-service-connection-design.md` — the spec travels with this plan; executors read both. The spec's "Decisions recorded so they are not re-litigated" section (8 items) is binding on every task below.

---

## Global Constraints

- **Variant vocabulary is exactly six values:** `none`, `low`, `medium`, `high`, `xhigh`, `max`. One home, in `ServiceDescriptorRegistry`. `AGENTS.md` gains a dosage-adjacent bullet naming this set.
- **No silent model default.** The `?? "gpt-4o-mini"` fallback at `LlmClient.cs:31` is deleted. A `NULL` `Model` is a hard failure that names Settings.
- **The key is always saved**, whatever the probe returns; only `Verification` changes. A failed probe still leaves a usable connection marked `unverified`.
- **`UrlSafetyValidator` must NOT be applied to the LLM base URL.** It guards only `HtmlScraperService.cs:16` (the scraped manufacturer URL) and stays there. Applying it would reject `http://` and loopback, killing local Ollama/LM Studio and the e2e stub. This is a recorded decision, not an oversight.
- **No `BaseAddress` on any `HttpClient`.** Every LLM request builds an absolute `Uri` from the connection's `BaseUrl`. The `AddHttpClient("llm", …)` configure delegate is deleted.
- **`Authorization` and descriptor headers are set per request**, never on a cached handler.
- **No foreign keys on `ServiceConnections`.** Deleting a connection must never touch a supplement.
- **No seed row for `ServiceConnections`.** Seeding a credential is wrong. This is a recorded exception to the AGENTS.md seeding rule, written into the debt register in the same change (as **`TD-022`** — see Task 7; `TD-021` was claimed during Task 2's fix round).
- **The tracer comes second, not last.** `new-shard.md` step 3 requires a green vertical path before depth, and this plan originally ran three horizontal tasks first. Task 2 restores it: table → save-a-connection → *then* the probe, the seam, and the picker. **Task 2 must not change anything that works today** — `LlmClient` still reads configuration until Task 4, so the existing enrichment path is untouched while the tracer lands.
- **`Service` is a descriptor id, not free text.** The user picks from the registry; the controller never accepts an arbitrary string.
- **`IsActive` invariant: at most one row has `IsActive = 1`.** Enforced in the repository, not by convention.
- **`x-opencode-session` is a per-process singleton id**, not per-request.
- **Complete types stay under 300 lines** including partials (`FileSizeTests`).
- **Every new file under `VitaTrack.Core/Features/ServiceConnections/` must be claimed in `shards.yaml` in the same commit** — the pre-commit hook runs `ShardOwnershipTests` (DL-002 defect a).

---

## Review Focus

Five input classes the spec implies that the task tests below must pin. Most likely to bite first.

1. **A base URL with or without a trailing slash** — `"http://x/v1"` and `"http://x/v1/"` must both produce a request to the same endpoint. A naive `new Uri(base, "v1/models")` drops the last segment on the unslashed form. Pinned in Task 3.
2. **The `IHttpClientFactory` cached-handler trap** — after the user saves a new key, the very next Enrich must use the new key, not the one captured when the handler was built. Pinned in Task 3 and Task 7.
3. **Two `IsActive = 1` rows** — connecting a second time, changing service, or re-saving must deactivate the previous row. Pinned in Task 1 and Task 4.
4. **A free-text model with stray whitespace** (`" gpt-4o-mini "`, pasted from a doc) must be trimmed before it reaches the request body. Pinned in Task 4.
5. **Reconnecting must not wipe the chosen model** — a user who re-enters their key should not lose their model selection to the connect flow. Pinned in Task 4.

---

## File Structure

**Create — `VitaTrack.Core/Features/ServiceConnections/`** (one concern each; several will be under 30 lines)

| File | Responsibility |
|---|---|
| `ServiceConnection.cs` | The entity record. Pure data. |
| `IServiceConnectionRepository.cs` | Persistence contract. |
| `ServiceConnectionRepository.cs` | Dapper implementation. Owns the table and the `IsActive` invariant. |
| `ServiceDescriptor.cs` | The service data record (id, label, default URL, header factory). |
| `ServiceDescriptorRegistry.cs` | The known-service list + the single home of the variant vocabulary. |
| `ModelCatalog.cs` | Probe result: models + whether the key verified. |
| `ServiceCatalogClient.cs` | `GET {BaseUrl}/v1/models`, OpenAI-compatible parse. |
| `ConnectServiceRequest.cs` | Form DTO for the connect POST. |
| `SelectModelRequest.cs` | Form DTO for the model/variant POST. |
| `SaveConnectionHandler.cs` | Validates + saves. Created by the tracer (Task 2), extended in Task 5. |
| `ProbeConnectionHandler.cs` | Calls `ServiceCatalogClient`, stamps `Verification`. Task 5. |

**Create — web:** `Controllers/ServiceConnectionController.cs` (Task 2), `Views/ServiceConnection/Index.cshtml` and `_ConnectForm.cshtml` (Task 2), `_ModelPicker.cshtml` (Task 5), plus a nav item in `Views/Shared/_Layout.cshtml`. The controller and first two views are **created by the tracer and extended by Task 5**, not created twice.

**Create — tests:** `VitaTrack.Tests/Features/ServiceConnections/*.cs`, `VitaTrack.Tests/LlmClientRequestTests.cs`, `VitaTrack.ArchitectureTests/ConfigBindingAbsentTests.cs`, `e2e-tests/playwright/helpers/llm-stub.js`, `e2e-tests/playwright/tests/service-connection.spec.js`.

**Modify:** `ILlmClient.cs`, `LlmClient.cs`, `ISupplementLabelParser.cs`, `SupplementLabelParser.cs`, `ILlmService.cs` *(no signature change)*, `LlmService.cs`, `ServiceCollectionExtensions.cs`, `Data/DbInit.cs`, `shards.yaml`, `storymap.yaml`, `AGENTS.md`, `docs/quality/nfr.md`, `DESIGN.md`, `docs/factory/technical-debt.yaml`, `VitaTrack.Tests/{LlmClientTests,LlmClientHeaderTests,LlmServiceTests,BlendEnrichmentTests,ServiceCollectionExtensionsTests}.cs`, `supplement-llm-integration.spec.js`.

**Delete:** `VitaTrack.Core/VitaTrackOptions.cs`.

---

## Interfaces Established Here

Later tasks depend on these exact signatures. Read this block before any task.

```csharp
// Features/ServiceConnections/ServiceConnection.cs
public sealed record ServiceConnection
{
    public int Id { get; init; }
    public string Service { get; init; } = string.Empty;
    public string BaseUrl { get; init; } = string.Empty;
    public string ApiKey { get; init; } = string.Empty;
    public string? Model { get; init; }
    public string? Variant { get; init; }
    public int MaxTokens { get; init; } = 16384;
    public double Temperature { get; init; } = 1.0;
    public string Verification { get; init; } = "unverified";   // "verified" | "unverified"
    public DateTimeOffset? VerifiedAt { get; init; }
    public bool IsActive { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

// Features/ServiceConnections/IServiceConnectionRepository.cs
public interface IServiceConnectionRepository
{
    Task<ServiceConnection?> GetActiveAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ServiceConnection>> GetAllAsync(CancellationToken ct = default);
    Task<int> SaveAsync(ServiceConnection connection, CancellationToken ct = default); // returns Id
    Task<bool> DeleteAsync(int id, CancellationToken ct = default);
}

// Features/ServiceConnections/ServiceDescriptor.cs
public sealed record ServiceDescriptor(
    string ServiceId,
    string DisplayName,
    string DefaultBaseUrl,
    Func<IReadOnlyDictionary<string, string>> HeaderFactory);

// Features/ServiceConnections/ModelCatalog.cs
public sealed record ModelCatalog(IReadOnlyList<string> Models, bool Verified);

// Features/ServiceConnections/ServiceDescriptorRegistry.cs
public static class ServiceDescriptorRegistry
{
    public static IReadOnlyList<ServiceDescriptor> All { get; }
    public static IReadOnlyList<string> Variants { get; }  // none, low, medium, high, xhigh, max
    public static ServiceDescriptor? Find(string serviceId);
    // The `IHttpClientFactory HttpClientFactory { get; set; }` seam this block used to
    // declare was deleted in Task 3's fix round: it had no reader in any task, and both
    // consumers take the factory by constructor injection instead.
    public static string SessionId { get; }  // the per-process session id; one home for it
}

// Features/ServiceConnections/ServiceCatalogClient.cs
public interface IServiceCatalogClient
{
    Task<ModelCatalog> ListModelsAsync(ServiceConnection connection, CancellationToken ct = default);
}

// LlmEnrichment — the seam that changes in Task 3
public record LlmRequestSettings(string? Model, string? Variant, int MaxTokens, double Temperature);

public interface ILlmClient
{
    Task<LlmCompletion> PostChatAsync(string systemPrompt, string userPrompt,
        ServiceConnection connection, LlmRequestSettings settings);
}

public interface ISupplementLabelParser
{
    Task<LlmResult> ExtractNutrientsAsync(string supplementName, string brand, string cleanedHtml,
        ServiceConnection connection, LlmRequestSettings settings);
}
```

`ILlmService.EnrichSupplementAsync(Supplement supplement)` **does not change** — `LlmService` resolves the active connection itself so the controller stays thin.

---

### Task 1: Domain entity and persistence

**Files:**
- Create: `VitaTrack.Core/Features/ServiceConnections/ServiceConnection.cs`
- Create: `VitaTrack.Core/Features/ServiceConnections/IServiceConnectionRepository.cs`
- Create: `VitaTrack.Core/Features/ServiceConnections/ServiceConnectionRepository.cs`
- Modify: `VitaTrack.Core/Data/DbInit.cs` (add `CREATE TABLE IF NOT EXISTS ServiceConnections`)
- Modify: `shards.yaml` (add the `SC` slice skeleton — `core:` and `tables:`; other lists may start empty except `tables: [ServiceConnections]`)
- Test: `VitaTrack.Tests/Features/ServiceConnections/ServiceConnectionRepositoryTests.cs`

**Interfaces:** produces everything in the Interfaces Established Here block relating to `ServiceConnection` and the repository.

- [ ] **Step 1: Write the failing repository tests**

Extend `SqliteTestBase` (it already builds the real schema with `seedData: false`, so the new table appears automatically once `DbInit` creates it). Tests:

- `SaveAsync_ThenGetActive_ReturnsRow` — save with `IsActive = true`, assert every field round-trips including `Verification` and `VerifiedAt`.
- `SaveAsync_SecondActiveConnection_DeactivatesTheFirst` — save A active, save B active, assert `GetActiveAsync` returns B and A has `IsActive == false`. This is **Review Focus #3**.
- `SaveAsync_UpdatesExistingRow_WhenIdIsNonZero` — save, then save the same `Id` with a changed `ApiKey`; assert one row, updated value.
- `GetActiveAsync_WithNoRows_ReturnsNull`.
- `DeleteAsync_RemovesTheRow_AndGetActiveThenReturnsNull`.
- `SaveAsync_StampsCreatedAtAndUpdatedAt`.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test VitaTrack.Tests/VitaTrack.Tests.csproj --filter "FullyQualifiedName~ServiceConnectionRepositoryTests"`
Expected: FAIL — table `ServiceConnections` does not exist.

- [ ] **Step 3: Add the table to `DbInit.EnsureCreated`**

Add a `CREATE TABLE IF NOT EXISTS ServiceConnections (...)` statement matching the spec's column list exactly, alongside the existing `db.Execute(@"...")` calls. **No seed statement** — the `seedData` parameter governs domain seed data; a connection row is never seeded, and the exception is recorded in Task 7.

- [ ] **Step 4: Add the `SC` slice to `shards.yaml`**

`id: SC`, `name: Service Connections`, `tables: [ServiceConnections]`, and `controller` / `core` / `views` / `js` / `unit_tests` / `e2e_specs` lists that are empty for now except `core` (the three files this task creates) and `unit_tests`. **`ShardOwnershipTests` errors on a glob that matches nothing** — so add paths as each task creates them, in the same commit (Global Constraints, last bullet).

- [ ] **Step 5: Implement `ServiceConnection`, `IServiceConnectionRepository`, `ServiceConnectionRepository`**

Follow `VitaTrack.Core/Features/Family/FamilyRepository.cs` for Dapper style and `FamilyRepository.cs:17` for the `ORDER BY` discipline. Parameterised SQL only. Store `VerifiedAt` / `CreatedAt` / `UpdatedAt` as ISO-8601 round-trip strings; parse on read. `IsActive` is a SQLite `INTEGER` mapped to `bool`. The deactivation is a single `UPDATE ServiceConnections SET IsActive = 0 WHERE IsActive = 1` issued **before** the insert/update, in the same command batch.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test VitaTrack.Tests/VitaTrack.Tests.csproj --filter "FullyQualifiedName~ServiceConnectionRepositoryTests"`
Expected: PASS.

- [ ] **Step 7: Run the full gate**

Run: `dotnet test VitaTrack.sln -c Release`
Expected: PASS. Baseline before this change was arch 18 / unit 218 — unit grows, arch unchanged.

- [ ] **Step 8: Commit**

```bash
git add VitaTrack.Core/Features/ServiceConnections/ VitaTrack.Core/Data/DbInit.cs shards.yaml VitaTrack.Tests/Features/ServiceConnections/
git commit -m "feat: ServiceConnections table, entity, and repository (SC slice)"
```

---

### Task 2: Tracer — save a connection, end to end

**The vertical path, per `new-shard.md` step 3.** A thin but complete route through every layer: Settings page →
connect form → POST → handler → repository → render. After this task a user can **save a connection and see it
saved**, from commit one. Depth — the probe, the catalog, the model picker, the unverified branch — arrives in
Task 5.

**This task deliberately touches nothing that works today.** `LlmClient` still reads `IOptions<VitaTrackOptions>`
until Task 4, so enrichment continues to work from configuration exactly as it does now. The tracer adds a
*new* capability beside the old one; it replaces nothing. That is what makes it safe to run first and what makes
the refactor in Task 4 observable — before it, nothing reads the table; after it, everything does.

**What is absent is absent, not stubbed.** There is no probe, no model list, no variant dropdown, and no
`SelectModel` action. `Verification` is written as `unverified` unconditionally, because nothing exists yet to
set it otherwise. `new-shard.md`'s "the skeleton is never stubbed" is about not faking a *working* step; this
task has no fake steps, it has fewer steps.

**Files:**
- Create: `VitaTrack.Core/Features/ServiceConnections/ConnectServiceRequest.cs`
- Create: `VitaTrack.Core/Features/ServiceConnections/SaveConnectionHandler.cs`
- Create: `VitaTrack.Web/Controllers/ServiceConnectionController.cs`
- Create: `VitaTrack.Web/Views/ServiceConnection/Index.cshtml`
- Create: `VitaTrack.Web/Views/ServiceConnection/_ConnectForm.cshtml`
- Modify: `VitaTrack.Web/Views/Shared/_Layout.cshtml` (nav item — **required**, `UiReachabilityTests` fails an orphan page)
- Modify: `VitaTrack.Core/ServiceCollectionExtensions.cs` (register `IServiceConnectionRepository`, `SaveConnectionHandler`)
- Modify: `shards.yaml` (claim the new files)
- Modify: `storymap.yaml` (one activity + one task, so the new e2e spec is referenced — `StoryMapConsistencyTests` fails an unreferenced spec)
- Test: `VitaTrack.Tests/Features/ServiceConnections/SaveConnectionHandlerTests.cs`
- Test: `VitaTrack.Tests/ServiceConnectionControllerTests.cs`
- Test: `e2e-tests/playwright/tests/service-connection.spec.js` (thin — no stub server needed, there is no probe)

**Interfaces:** consumes `IServiceConnectionRepository` (Task 1). Produces `ConnectServiceRequest`,
`SaveConnectionHandler`, the controller, the two views, and both test classes — all of which **Task 5 extends**.

- [ ] **Step 1: Write the failing handler tests**

In `SaveConnectionHandlerTests`:

- `HandleAsync_ValidConnect_SavesAndActivates` — a valid request persists and sets `IsActive`.
- `HandleAsync_RejectsBlankBaseUrlOrKey`.
- `HandleAsync_SecondConnect_DeactivatesThePrevious` — **Review Focus #3** at the handler layer.
- `HandleAsync_Reconnect_PreservesTheExistingModel` — **Review Focus #5**. Save with a model, connect again, assert it survived.
- `HandleAsync_TrimsWhitespaceFromFreeTextModel` — **Review Focus #4**. `" gpt-4o-mini "` stores trimmed.
- `HandleAsync_SetsVerificationToUnverified_BecauseNoProbeExistsYet` — pins the tracer's honest state rather than leaving it incidental.

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test VitaTrack.Tests/VitaTrack.Tests.csproj --filter "FullyQualifiedName~SaveConnectionHandlerTests"`
Expected: FAIL — types do not exist.

- [ ] **Step 3: Implement `ConnectServiceRequest` and `SaveConnectionHandler`**

`ConnectServiceRequest` carries `BaseUrl`, `ApiKey`, and an optional free-text `Model` — **no `Service` field
yet**. The handler writes `Service = "opencode"` as a literal; **Task 5** replaces that literal with a registry
lookup and adds the selector (its test list carries `HandleAsync_RejectsUnknownServiceId`). It validates
non-blank URL and key, trims the model, then saves — the
repository already deactivates the previous active row, so the handler does not duplicate that.

**One design question, decided here to keep Task 5 small:** the tracer's controller uses a plain form POST and
redirect, **not** HTMX. HTMX arrives with the model picker in Task 5, where the partial swap it exists to serve
actually exists. Adding `hx-post` now would be scaffolding for a swap that has no content.

- [ ] **Step 4: Write the failing controller tests**

In `ServiceConnectionControllerTests`:

- `Index_WithNoConnection_RendersTheConnectForm`.
- `Index_WithActiveConnection_RendersTheSavedConnection_WithMaskedKey` — the key shows its last four characters and the raw value appears nowhere in the rendered markup.
- `Connect_ValidRequest_SavesAndRedirectsToIndex`.
- `Connect_InvalidRequest_ReturnsTheFormWithErrors` and does not save.

- [ ] **Step 5: Run to verify they fail**

Run: `dotnet test VitaTrack.Tests/VitaTrack.Tests.csproj --filter "FullyQualifiedName~ServiceConnectionControllerTests"`
Expected: FAIL — controller does not exist.

- [ ] **Step 6: Implement the controller and the two views**

`Index` resolves the active connection and renders the form or the saved state. `Connect` binds the DTO, calls
the handler, redirects. Thin controller: bind, call, return. `_ConnectForm.cshtml` carries a service label
(read-only — one service ships), base URL, API key as a password field, and an optional model field. Add the
**Settings** nav item to `_Layout.cshtml:21-28`.

- [ ] **Step 7: Register the new services**

`AddScoped` for `IServiceConnectionRepository` and `SaveConnectionHandler` in `AddCore`. Nothing else — the
catalog client, the session id, and the descriptor seam all belong to Tasks 3 and 5.

- [ ] **Step 8: Claim the files in `shards.yaml` and add the story-map task**

Every new path in the same commit (the pre-commit hook runs `ShardOwnershipTests` — DL-002 defect a). In
`storymap.yaml`, add an activity "Configure AI service" with a single task for connecting, `entry_point`
described as the **Settings nav item** (not a deep URL), and a `tests:` ref to the spec created in Step 9.
Task 7 adds the remaining tasks.

- [ ] **Step 9: Write the thin e2e spec**

`service-connection.spec.js`: arrive by **clicking the nav item**, fill base URL and a dummy key, submit, and
assert the saved state renders with a masked key. **No stub server** — there is no probe to satisfy, so this
spec needs no third-party double. Reload and assert the connection survived, which is the round trip the
tracer exists to prove.

- [ ] **Step 10: Run the gates**

Run: `dotnet test VitaTrack.sln -c Release` then `./test-e2e.sh`
Expected: PASS both. Arch 27 / unit 234 plus this task's additions. **If enrichment e2e fails, the tracer broke
the existing LLM path** — that is a finding to report, not a red gate to paper over.

- [ ] **Step 11: Commit**

```bash
git add VitaTrack.Core/Features/ServiceConnections/ VitaTrack.Core/ServiceCollectionExtensions.cs VitaTrack.Web/ shards.yaml storymap.yaml VitaTrack.Tests/ e2e-tests/playwright/tests/
git commit -m "feat: connect-a-service tracer — save a connection end to end (SC slice)"
```

---

### Task 3: Service descriptors and the catalog probe

**Files:**
- Create: `VitaTrack.Core/Features/ServiceConnections/ServiceDescriptor.cs`
- Create: `VitaTrack.Core/Features/ServiceConnections/ServiceDescriptorRegistry.cs`
- Create: `VitaTrack.Core/Features/ServiceConnections/ModelCatalog.cs`
- Create: `VitaTrack.Core/Features/ServiceConnections/ServiceCatalogClient.cs`
- Modify: `shards.yaml` (extend the `SC` `core:` list)
- Test: `VitaTrack.Tests/Features/ServiceConnections/ServiceDescriptorRegistryTests.cs`
- Test: `VitaTrack.Tests/Features/ServiceConnections/ServiceCatalogClientTests.cs`

**Interfaces:** produces `ServiceDescriptor`, `ServiceDescriptorRegistry`, `ModelCatalog`, `IServiceCatalogClient`. Consumes `ServiceConnection` from Task 1.

- [ ] **Step 1: Write the failing registry tests**

- `All_ContainsExactlyOneDescriptor_ShippedToday` — assert `All` has exactly one entry, `ServiceId == "opencode"`. This pins scope: a second descriptor is a scope change, and the test makes that visible rather than silent.
- `Variants_Are_ExactlyTheSixDocumentedValues` — assert the set equals `none, low, medium, high, xhigh, max`. One home, per Global Constraints.
- `Find_UnknownId_ReturnsNull`.
- `OpenCodeDescriptor_HeaderFactory_IncludesXOpencodeSession` — assert the dictionary has key `x-opencode-session` with a non-empty value.
- `OpenCodeDescriptor_DefaultBaseUrl_IsEmpty` — the gateway URL is the user's to supply.

- [ ] **Step 2: Write the failing catalog client tests**

Use a stub `HttpMessageHandler` (Moq is permitted for `HttpClient`). Assert:

- `ListModelsAsync_On200_ParsesIds_AndVerifies` — response body `{"object":"list","data":[{"id":"m1"},{"id":"m2"}]}` → `Models` is `["m1","m2"]`, `Verified` is `true`.
- `ListModelsAsync_On401_ReturnsUnverified_AndEmptyCatalog` — `Verified == false`, `Models` empty, **no exception**.
- `ListModelsAsync_On404_ReturnsUnverified` — 404 is not a bad key; several OpenAI-compatible endpoints do not implement model listing.
- `ListModelsAsync_On500OrMalformedJson_ReturnsUnverified` — never throws.
- `ListModelsAsync_SendsBearerAuthorizationHeader` — assert the request carried `Authorization: Bearer <key>`.
- `ListModelsAsync_SendsDescriptorHeaders` — assert `x-opencode-session` present.
- `ListModelsAsync_BuildsAbsoluteUri_ForBaseUrlWithAndWithoutTrailingSlash` — **Review Focus #1**. Both `http://h/v1` and `http://h/v1/` must request `http://h/v1/v1/models`... **no** — assert the two forms produce the *same* request URI, whatever it is. Pin the invariant, not a guess at the path. The two must not differ.

- [ ] **Step 3: Run both test classes to verify they fail**

Run: `dotnet test VitaTrack.Tests/VitaTrack.Tests.csproj --filter "FullyQualifiedName~ServiceDescriptorRegistryTests|FullyQualifiedName~ServiceCatalogClientTests"`
Expected: FAIL — types do not exist.

- [ ] **Step 4: Implement `ServiceDescriptor`, `ServiceDescriptorRegistry`, `ModelCatalog`**

The registry's static list contains the single `opencode` descriptor. `HeaderFactory` returns `{ "x-opencode-session": <singleton id> }`; the value is `ServiceDescriptorRegistry.SessionId`, a per-process `Guid` the registry owns — Task 4's `LlmSessionId` singleton must be a DI wrapper over it, not a second id. `Variants` is the six-value list, `IReadOnlyList<string>`.

- [ ] **Step 5: Implement `IServiceCatalogClient` / `ServiceCatalogClient`**

Inject `IHttpClientFactory`. Build an **absolute** `Uri` from `connection.BaseUrl` — trim trailing `/`, then combine, so both slash forms collapse to one URI. Request `v1/models`. **Any** non-2xx, any exception, or any unparseable body returns `new ModelCatalog([], false)`. Never throw: a failed probe is a state, not an error.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test VitaTrack.Tests/VitaTrack.Tests.csproj --filter "FullyQualifiedName~ServiceDescriptorRegistryTests|FullyQualifiedName~ServiceCatalogClientTests"`
Expected: PASS.

- [ ] **Step 7: Extend `shards.yaml` and commit**

```bash
git add VitaTrack.Core/Features/ServiceConnections/ shards.yaml VitaTrack.Tests/Features/ServiceConnections/
git commit -m "feat: service descriptors and the /v1/models catalog probe (SC slice)"
```

---

### Task 4: The LLM seam

**Files:**
- Create: `VitaTrack.Core/Features/LlmEnrichment/LlmSessionId.cs`
- Modify: `VitaTrack.Core/Features/LlmEnrichment/ILlmClient.cs`
- Modify: `VitaTrack.Core/Features/LlmEnrichment/LlmClient.cs`
- Modify: `VitaTrack.Core/Features/LlmEnrichment/ISupplementLabelParser.cs`
- Modify: `VitaTrack.Core/Features/LlmEnrichment/SupplementLabelParser.cs`
- Modify: `VitaTrack.Core/Features/LlmEnrichment/LlmService.cs`
- Modify: `shards.yaml` (claim `LlmSessionId.cs` under `LLM`'s `core:`)
- Test: `VitaTrack.Tests/LlmClientRequestTests.cs` (new)
- Test: `VitaTrack.Tests/LlmClientTests.cs`, `LlmClientHeaderTests.cs`, `LlmServiceTests.cs`, `BlendEnrichmentTests.cs` (rework)

**Interfaces:** consumes `ServiceConnection` (Task 1), `ServiceDescriptorRegistry` (Task 3). Produces the new `ILlmClient` and `ISupplementLabelParser` signatures and the unchanged `ILlmService`.

**The connection flows through three signatures**, because `ILlmClient` is consumed by `SupplementLabelParser`, not directly by `LlmService`. All three change in this task.

- [ ] **Step 1: Write the failing request-shape tests**

New `LlmClientRequestTests` against a stub handler. Assert:

- `PostChatAsync_BuildsAbsoluteUri_NoBaseAddressOnClient` — **Review Focus #1**. The slashed and unslashed `BaseUrl` forms produce the same request URI. Pin the invariant, not a guessed path.
- `PostChatAsync_SendsBearerAuthorizationHeader_PerRequest` — the request carries `Authorization: Bearer <connection.ApiKey>`. This is the test that would fail if the header were ever set on a cached handler.
- `PostChatAsync_BuildsUriFromTheConnectionNotFromAClientBaseAddress` — a connection pointing at a different host than any previously-used client still hits the connection's host. **Review Focus #2**.
- `PostChatAsync_SendsModelFromSettings` — `settings.Model` appears as `model` in the body.
- `PostChatAsync_SendsVariantAsReasoningEffort_AndOmitsWhenNull` — `Variant = "high"` → body has `reasoning_effort: "high"`; `Variant = null` → key absent.
- `PostChatAsync_UsesMaxTokensAndTemperatureFromSettings` — a `settings` override changes both in the body, proving the values are no longer read from configuration.
- `PostChatAsync_SendsXOpencodeSessionHeader_FromTheSessionSingleton` — the value is the `LlmSessionId` singleton's, stable across two calls.
- `PostChatAsync_WithNullModel_ReturnsErrorPointingAtSettings` — no `gpt-4o-mini` fallback; the error text names Settings.
- `PostChatAsync_KeepsExistingErrorAndEmptyResponseBehavior` — non-2xx, empty choices, and empty content still return their existing `LlmCompletion` errors.

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test VitaTrack.Tests/VitaTrack.Tests.csproj --filter "FullyQualifiedName~LlmClientRequestTests"`
Expected: FAIL — signatures do not match.

- [ ] **Step 3: Add `LlmSessionId`**

A registered singleton exposing one `Guid` generated at construction, exposed as a string. One id per process, stable across every request, fresh across restarts.

- [ ] **Step 4: Rewrite `ILlmClient` / `LlmClient`**

New signature per the Interfaces block. In `LlmClient`: drop the `IOptions<VitaTrackOptions>` constructor parameter and the `_options` field; delete the `?? "gpt-4o-mini"` default; build the absolute `Uri` from `connection.BaseUrl` exactly as `ServiceCatalogClient` does (same trim-and-combine rule — **keep these two implementations in agreement or extract the helper**); set `Authorization` and the descriptor's headers per request; map `settings.Variant` onto `reasoning_effort` only when non-null.

- [ ] **Step 5: Thread the connection through `ISupplementLabelParser` / `SupplementLabelParser`**

Add the two parameters and pass them to `_llmClient.PostChatAsync`. The prompt-building and JSON-parsing logic is untouched.

- [ ] **Step 6: Rewrite `LlmService` resolution**

Drop `IOptions<VitaTrackOptions>`; take `IServiceConnectionRepository`. In `EnrichSupplementAsync`: resolve the active connection, and if there is none **or** `Model` is `null`/blank, return `new LlmResult { ExtractionError = <message naming Settings> }` before scraping or calling the LLM. Otherwise build `LlmRequestSettings` from the connection and pass it through. The `EnrichSupplementAsync(Supplement)` signature does not change.

- [ ] **Step 7: Rework the four affected test classes**

`LlmClientTests`, `LlmClientHeaderTests`, `LlmServiceTests`, `BlendEnrichmentTests` each construct `Options.Create(new VitaTrackOptions { … })` today. Replace with a `ServiceConnection` + `LlmRequestSettings` pair. `LlmServiceTests` additionally needs an `IServiceConnectionRepository` — stub it with Moq to return an active connection with a `Model` set, and add a case proving the Settings-pointer error when it returns `null` or a connection with a null `Model`.

**The pre-existing assertions in these classes must keep their intent.** If one of them encodes behavior the seam change deliberately removes (e.g. a config-driven model default), delete it and say so in the commit body — do not weaken an assertion to make a build pass.

- [ ] **Step 8: Run the full gate**

Run: `dotnet test VitaTrack.sln -c Release`
Expected: PASS.

- [ ] **Step 9: Commit**

```bash
git add VitaTrack.Core/Features/LlmEnrichment/ VitaTrack.Tests/ shards.yaml
git commit -m "refactor: LLM seam takes the connection per call; drop the config-driven model default"
```

---

### Task 5: Probe, model picker, and the unverified state

> **The tracer already built the save path.** Task 2 created `ConnectServiceRequest`, `SaveConnectionHandler`,
> `ServiceConnectionController`, `Index.cshtml`, `_ConnectForm.cshtml`, the nav item, `SaveConnectionHandlerTests`,
> `ServiceConnectionControllerTests`, and a thin `service-connection.spec.js`. This task **extends** those files
> with the probe, the catalog-driven model picker, and the unverified branch. It does not recreate them, and it
> does not change the connect form's field set — it adds the model/variant controls beside it.
>
> The tracer's `Verification` is always `unverified` (no probe existed to set it). This task is what makes that
> column mean anything.

**Files:**
- Create: `.../ServiceConnections/{SelectModelRequest,ProbeConnectionHandler}.cs`
- Modify: `.../ServiceConnections/{ConnectServiceRequest,SaveConnectionHandler}.cs` (tracer files — swap the `Service` literal for a registry lookup, add registry validation)
- Modify: `VitaTrack.Web/Controllers/ServiceConnectionController.cs` (add the two HTMX POST targets)
- Create: `VitaTrack.Web/Views/ServiceConnection/_ModelPicker.cshtml`
- Modify: `VitaTrack.Web/Views/ServiceConnection/{Index,_ConnectForm}.cshtml` (add the model/variant controls and the unverified branch)
- Modify: `VitaTrack.Core/ServiceCollectionExtensions.cs` (register the new services)
- Modify: `shards.yaml` (claim the new files)
- Test: `VitaTrack.Tests/Features/ServiceConnections/SaveConnectionHandlerTests.cs` (extend — `ProbeAsync_*` cases and `HandleAsync_RejectsUnknownServiceId`)
- Test: `VitaTrack.Tests/ServiceConnectionControllerTests.cs` (extend — the two HTMX targets, the unverified branch, `SelectModel`, `Delete`)

**Interfaces:** consumes `ServiceConnection` (Task 1), the save path (Task 2), `ServiceDescriptorRegistry` and
`IServiceCatalogClient` (Task 3). Produces no new public types.

- [ ] **Step 1: Write the failing handler tests**

- `HandleAsync_ValidConnect_SavesAndActivates` — a valid request persists and sets `IsActive`.
- `HandleAsync_RejectsUnknownServiceId` — a `Service` not in the registry fails validation rather than persisting an arbitrary string.
- `HandleAsync_RejectsBlankBaseUrlOrKey`.
- `HandleAsync_SecondConnect_PreservesExistingModel` — **Review Focus #5**. Save with a model, connect again, assert the model survived.
- `ProbeAsync_VerifiedProbe_StampsVerifiedAndVerifiedAt`.
- `ProbeAsync_FailedProbe_StampsUnverified_AndLeavesTheConnectionSaved` — the Global Constraints bullet: a failed probe still leaves a usable connection.
- `HandleAsync_TrimsWhitespaceFromFreeTextModel` — **Review Focus #4**. `" gpt-4o-mini "` is stored trimmed.

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test VitaTrack.Tests/VitaTrack.Tests.csproj --filter "FullyQualifiedName~SaveConnectionHandlerTests"`
Expected: FAIL — types do not exist.

- [ ] **Step 3: Implement the DTOs and the two handlers**

`SaveConnectionHandler` validates, then saves (deactivating the previous active row via the repository). `ProbeConnectionHandler` calls `IServiceCatalogClient`, writes `Verification` / `VerifiedAt` back, and returns the `ModelCatalog`. Handlers return result records, never throw for control flow.

- [ ] **Step 4: Write the controller tests**

- `Index_WithNoConnection_RendersTheConnectForm`.
- `Index_WithActiveConnection_RendersTheModelPicker`.
- `Connect_ValidProbe_RendersModelPickerWithCatalogModels` — the two HTMX targets, both exercised.
- `Connect_FailedProbe_RendersTheUnverifiedWarningAndSavesTheConnection`.
- `Connect_InvalidModel_ReturnsTheFormWithErrors`.
- `SelectModel_PersistsModelAndVariant`.
- `Delete_ClearsTheActiveConnection`.

- [ ] **Step 5: Run the controller tests to verify they fail**

Run: `dotnet test VitaTrack.Tests/VitaTrack.Tests.csproj --filter "FullyQualifiedName~ServiceConnectionControllerTests"`
Expected: FAIL — controller does not exist.

- [ ] **Step 6: Implement the controller and views**

Thin controller: bind the DTO, call the handler, return a view or partial. Two HTMX POST targets — `Connect` swapping in `_ModelPicker` **or** the unverified branch, and `SelectModel`. The settings page renders the key **masked to its last four characters**, and the raw key never appears in the markup. Button intent classes per `DESIGN.md`: Connect `btn-success`, Re-test / Save `btn-sm btn-primary`, Disconnect `btn-sm btn-danger`. Add the Settings nav item to `_Layout.cshtml`.

- [ ] **Step 7: Register the services in `ServiceCollectionExtensions`**

`AddScoped` for `IServiceCatalogClient` (`AddHttpClient<IServiceCatalogClient, ServiceCatalogClient>()` — it takes `IHttpClientFactory` and `ILogger<ServiceCatalogClient>` by constructor, so DI supplies both); `AddSingleton` for `LlmSessionId`, which must wrap `ServiceDescriptorRegistry.SessionId` rather than mint a second id. The registry has no `HttpClientFactory` seam to set: it was declared in the Interfaces block above and deleted in Task 3's fix round as unread. The repository and `SaveConnectionHandler` were registered by the tracer (Task 2) — extend, do not duplicate. **Do not add a configure delegate to the `"llm"` client** — that is Task 6's deletion, and this task only adds the new registrations.

- [ ] **Step 8: Run the full gate**

Run: `dotnet test VitaTrack.sln -c Release`
Expected: PASS.

- [ ] **Step 9: Commit**

```bash
git add VitaTrack.Core/Features/ServiceConnections/ VitaTrack.Core/ServiceCollectionExtensions.cs VitaTrack.Web/ shards.yaml VitaTrack.Tests/
git commit -m "feat: connect-a-service flow — connect, probe, pick model and variant"
```

---

### Task 6: Delete configuration

**Files:**
- Delete: `VitaTrack.Core/VitaTrackOptions.cs`
- Modify: `VitaTrack.Web/Program.cs` (remove `builder.Services.Configure<VitaTrackOptions>(…)` at line 5)
- Modify: `VitaTrack.Web/appsettings.json` (remove the `VitaTrack` block)
- Modify: `VitaTrack.Core/ServiceCollectionExtensions.cs` (delete the `"llm"` configure delegate at `:65-77`)
- Modify: `test-e2e.sh` (remove the `VitaTrack__ApiKey` export)
- Test: `VitaTrack.ArchitectureTests/ConfigBindingAbsentTests.cs` (new)
- Test: `VitaTrack.Tests/ServiceCollectionExtensionsTests.cs` (rework)

**Interfaces:** none produced. This task removes configuration, so every reference must go or the build breaks — that is the check.

- [ ] **Step 1: Write the failing architecture test**

`ConfigBindingAbsentTests` — a file-scan test that fails if any of these appear anywhere under `VitaTrack.Core/`, `VitaTrack.Web/`, or `VitaTrack.Tests/`:

- `IOptions<VitaTrackOptions>`
- `VitaTrackOptions`
- `VitaTrack:ApiKey`, `VitaTrack:BaseUrl`, `VitaTrack:Model`
- `VitaTrack__ApiKey`

This is the guard that stops configuration quietly returning. Per the factory's own rule (*a rule never observed red is indistinguishable from a rule that cannot go red*), confirm it is red now, before the deletions.

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test VitaTrack.ArchitectureTests/VitaTrack.ArchitectureTests.csproj --filter "FullyQualifiedName~ConfigBindingAbsentTests"`
Expected: FAIL — names `VitaTrackOptions` occurrences. **This confirms the test can go red.**

- [ ] **Step 3: Delete the configuration**

Delete `VitaTrackOptions.cs`; remove the `Configure` call from `Program.cs`; remove the `VitaTrack` block from `appsettings.json`; remove the export from `test-e2e.sh`; delete the `AddHttpClient("llm", …)` configure delegate entirely, keeping the named-client registration with no delegate.

- [ ] **Step 4: Rework `ServiceCollectionExtensionsTests`**

It currently registers `Options.Create(new VitaTrackOptions { … })` and presumably asserts on the configured `Authorization` header. Replace with an assertion that the `"llm"` client has **no** `BaseAddress` and **no** `Authorization` default header — the headers are now per-request.

- [ ] **Step 5: Run to verify the arch test now passes**

Run: `dotnet test VitaTrack.ArchitectureTests/VitaTrack.ArchitectureTests.csproj --filter "FullyQualifiedName~ConfigBindingAbsentTests"`
Expected: PASS.

- [ ] **Step 6: Run the full gate**

Run: `dotnet test VitaTrack.sln -c Release`
Expected: PASS. The build itself is the check that every reference was found.

- [ ] **Step 7: Commit**

```bash
git rm VitaTrack.Core/VitaTrackOptions.cs
git add VitaTrack.Web/Program.cs VitaTrack.Web/appsettings.json VitaTrack.Core/ServiceCollectionExtensions.cs test-e2e.sh VitaTrack.ArchitectureTests/ VitaTrack.Tests/ServiceCollectionExtensionsTests.cs
git commit -m "refactor: delete VitaTrackOptions — the database is the only source of connection state"
```

---

### Task 7: Manifests, docs, and the design system

**Files:**
- Modify: `shards.yaml` (finalize the `SC` slice — every `core` / `views` / `unit_tests` / `e2e_specs` path must resolve)
- Modify: `storymap.yaml` (new activity, tasks, `entry_point`, test refs)
- Modify: `AGENTS.md` (LLM Integration bullet rewritten; variant vocabulary recorded; seed exception)
- Modify: `docs/quality/nfr.md` (secrets bullet rewritten; SSRF decision recorded)
- Modify: `DESIGN.md` (three-state connection indicator, dependent dropdown, Settings page, button intents)
- Modify: `docs/factory/technical-debt.yaml` (the `open` collection — see Step 6)

**Interfaces:** none. Documentation and manifest task.

> **Debt register format (settled — the prose `.md` is deleted).** The register is
> `docs/factory/technical-debt.yaml`, guarded by `VitaTrack.ArchitectureTests/TechnicalDebtRegisterTests`.
> An `open` entry requires `id`, `title`, `where`, `what`, `interest`, `paydown` and must not carry
> `closed` or `note`. **Every prose field is a folded `>-` scalar** — the text contains `": "` throughout,
> which is a parse error in a plain scalar — with keys at four spaces and content at six.
>
> **The new id is `TD-022`.** Open entries are currently `TD-021`, `TD-017`, `TD-019`, `TD-020`, `TD-018`,
> `TD-016`, `TD-010`; closed are `TD-001`–`TD-015`. **Two ids this plan once reserved are now taken:**
> `TD-020` by PR #26 (which split `TD-019`), and `TD-021` by Task 2's fix round (the `with`-clone stamp
> contract, filed under AGENTS.md directive 6). An earlier draft naming either would have produced a
> duplicate-id build failure. Ids are unique across the whole register: **never reuse one and never
> renumber an existing entry to make room** (`AGENTS.md`, Post-Mortem Capture). Append `TD-022`, and
> **re-read the open list immediately before writing** — the generic "take the next free id" rule
> overrides a reservation stated here, which is exactly how `TD-021` was taken.
>
> **Rule 5 will check this entry's `where` on the same build.** Backtick-delimited spans starting with a
> known project root are stripped of any `:\d+` line suffix and must resolve. Every path named in the
> `where` below exists at the time this task runs — verify, do not recall (DL-002 defect b).

- [ ] **Step 1: Finalize `shards.yaml`**

Every declared path must resolve — `ShardOwnershipTests` errors on a glob matching nothing and on any unclaimed `VitaTrack.Core` file. Claim the `SC` core files, views, unit tests, and the `service-connection.spec.js` e2e spec (which Task 7 creates, so this step comes after Task 7's file exists, or the entry is added in Task 7's commit).

- [ ] **Step 2: Add the `storymap.yaml` activity**

Activity "Configure AI service" with tasks for connect, choose model, and disconnect. Each task needs a unique `id` prefixed `SC-`, a real `entry_point` (**Settings nav item**, not a deep URL), and `tests:` refs. `StoryMapConsistencyTests` resolves every `unit:` and `e2e:` ref against real test source and **fails if any e2e spec is unreferenced** — so the Task 7 spec must appear here.

- [ ] **Step 3: Rewrite the `AGENTS.md` LLM Integration bullet**

Replace the `VitaTrack:BaseUrl` / `VitaTrack:ApiKey` / `IOptions<VitaTrackOptions>` text with the new model: settings live in the `ServiceConnections` table, services are `ServiceDescriptor` records in `ServiceDescriptorRegistry`, the variant vocabulary is the six values, and the probe is `GET {BaseUrl}/v1/models` with a failed probe leaving the connection `unverified`. Add the AGENTS.md seed-data exception for this table.

- [ ] **Step 4: Update `docs/quality/nfr.md`**

Rewrite the secrets bullet — the key is a plaintext `TEXT` column, masked to the last four characters in the UI, never logged, and never in rendered markup, with `*.db` gitignored. Add the SSRF decision: `UrlSafetyValidator` is **deliberately not** applied to the LLM base URL, with the reason, so a future reader does not "fix" it.

- [ ] **Step 5: Amend `DESIGN.md`**

Add: the three-state connection indicator (disconnected / connected-verified / connected-unverified), the dependent model→variant dropdown, the Settings page as a nav destination, and the button intent classes used in Task 4.

- [ ] **Step 6: Record the no-seed exception as `TD-022` in the register**

Append to the `open` collection in `docs/factory/technical-debt.yaml`. Content, as prose — this is
deliberately not a decision-log line, because the interest is a real per-change cost:

- `id: TD-022` · `title: >-` "`ServiceConnections` is deliberately unseeded, and nothing in code marks it"
- `where: >-` `` `VitaTrack.Core/Data/DbInit.cs`, `AGENTS.md` `` — both resolve, so rule 5 stays green
- `what: >-` Every other table `DbInit.EnsureCreated` creates carries a seed. This one does not, because
  seeding a credential is wrong. The AGENTS.md seeding rule has exactly one documented exception and it
  is here.
- `interest: >-` A future agent reading "seed data for new entity types" and finding no seed block for
  `ServiceConnections` cannot distinguish a deliberate exception from an oversight, and "fixing" it puts
  an API key in a source file that is committed to git.
- `paydown: >-` None needed. The exception is recorded in AGENTS.md and here; the compensating coverage is
  the e2e spec, which creates its connection through the UI rather than reading a seed row.

Then regenerate the dashboard in Step 7 — the register is one of its three sources, and CI gates freshness
(`ci.yml`, commit `2d786ea`), so a register edit without a regeneration is a red build.

- [ ] **Step 7: Run the gates and commit**

```bash
dotnet test VitaTrack.sln -c Release
python3 scripts/generate-factory-dashboard.py
python3 scripts/generate-factory-dashboard.py --check
git add AGENTS.md DESIGN.md docs/quality/nfr.md docs/factory/technical-debt.yaml docs/factory/dashboard.html shards.yaml storymap.yaml
git commit -m "docs: manifests, AGENTS, NFR, and DESIGN.md for the service-connection slice"
```

Expected: arch 27 / unit 234 plus whatever Tasks 1–5 added, 0 failed. **The register's rule 1 and rule 5
run in that `dotnet test`** — if `TD-022`'s `where` names a path that does not resolve, the build is red
and the message names the id.

---

### Task 8: End-to-end coverage with a real stub server

**Files:**
- Create: `e2e-tests/playwright/helpers/llm-stub.js`
- Modify: `e2e-tests/playwright/tests/service-connection.spec.js` (the tracer's spec — add the probe and model-selection cases against the stub)
- Modify: `e2e-tests/playwright/tests/supplement-llm-integration.spec.js`
- Modify: `shards.yaml` (claim the new helper and spec under `SC` `e2e_specs:`)
- Modify: `AGENTS.md` (amend the no-mock rule — see Step 4)

**Interfaces:** consumes the shipped feature. Produces no new types.

- [ ] **Step 1: Write the stub server**

`helpers/llm-stub.js` exports a factory that starts a **real Node `http` server on an ephemeral port** (listen on port `0`, read the assigned port back) and resolves to `{ baseUrl, close, requests }`. It serves:

- `GET /v1/models` → `{"object":"list","data":[{"id":"stub-model-a"},{"id":"stub-model-b"}]}`
- `POST /v1/chat/completions` → `{"choices":[{"message":{"role":"assistant","content":"<a nutrient JSON payload>"}}]}`
- Records every inbound request (method, url, headers, body) on `requests`.

Per-test and ephemeral — **not** a second `webServer` entry, because `fullyParallel: true` means a fixed port is shared by every worker and recorded state would leak across parallel tests.

- [ ] **Step 2: Write the spec**

Drive the real UI: Settings → fill base URL with the stub's `http://127.0.0.1:<port>` and a dummy key → Connect → assert the model picker lists `stub-model-a` / `stub-model-b` → select a model and a variant → Save → **assert on `stub.requests`** that an `Authorization` header and the chosen `reasoning_effort` reached the server. Then the unverified path: connect to a port with nothing listening → assert the warning renders, the connection is still saved, and Enrich refuses with a message naming Settings.

- [ ] **Step 3: Re-point the existing real-provider spec**

`supplement-llm-integration.spec.js` currently skips on `process.env.LLM_API_KEY || process.env.VitaTrack__ApiKey`. Change it to skip when there is no active connection in the app. It stays the one test that talks to a genuine provider.

- [ ] **Step 4: Amend the no-mock rule in `AGENTS.md`**

The current rule — *"Do not mock HTTP responses for these E2E tests"* — is aimed at faking the app's own HTTP layer. Rewrite it to make the distinction explicit: **never intercept the app's own HTTP; a test-double server standing in for an external dependency is fine.** The stub is a real listening socket; the app is unmodified; status codes and JSON parsing are real.

- [ ] **Step 5: Run the gate**

Run: `./test-e2e.sh`
Expected: PASS. A red e2e that contradicts this plan is a **finding to report**, not something to paper over by loosening an assertion.

- [ ] **Step 6: Commit**

```bash
git add e2e-tests/ AGENTS.md shards.yaml
git commit -m "test: e2e coverage for the connect flow against a real stub endpoint"
```

---

## Self-Review

**Spec coverage.** Every spec section maps to a task: storage-is-source-of-truth → Task 5 · slice boundary and one-way dependency → Tasks 1–3 · descriptor-not-interface → Task 2 · session id → Task 3 · the `HttpClient` fix → Task 3 (per-request) and Task 5 (delegate deleted) · the `gpt-4o-mini` default → Task 3 · the three states → Task 4 · the connect POST semantics → Tasks 2 + 4 · fixed variant vocabulary → Task 2 · the hard-fail path → Task 3 · schema → Task 1 · no-seed exception → Task 6 · secret handling → Task 6 · files → Tasks 1–4, 7 · `DESIGN.md` amendment → Task 6 · testing → Tasks 1–5, 7 · the no-mock amendment → Task 7 · all eight recorded decisions → Global Constraints plus Tasks 2, 3, 4, 6.

**Gaps found and closed during review.** (1) The spec's file list omits that `ILlmClient` is consumed by `SupplementLabelParser`, so the seam is **three** signatures — now called out in Task 4's header, since an implementer reading only the spec would miss it. (2) The spec does not say where the `LlmSessionId` singleton is registered — now Task 4 Step 3 and Task 5 Step 7. (3) The spec's e2e section does not name who re-points the real-provider spec — now Task 8 Step 3. (4) The debt-register format was left open pending the register-to-YAML conversion, which has since landed (`refactor/debt-register-yaml`) — Task 7 names the file, the `open` collection, the folded-scalar requirement, and the two register rules that will check the new entry on the same build. (5) Task 7 Step 3's seed exception and Step 6's register entry are the same decision recorded twice, deliberately: `AGENTS.md` is where a future agent looks for the rule, the register is where the *cost* of breaking it is tracked.

**Gaps found and closed on the launch review (2026-09-28), after PR #26 landed.**

- **(6) The plan had no tracer bullet.** It ran three horizontal tasks — domain, descriptors, seam — before
  anything was observable end to end, which is `new-shard.md` step 3's exact requirement. Task 2 is new and
  restores it. The old Tasks 2/3/4 became 3/4/5; Task 5 was retitled to make clear it *extends* the tracer's
  files rather than recreating them.
- **(7) The plan violated its own recipe, and the violation was unavoidable as written.** The tracer cannot be
  "no stubbed steps" in the usual sense, because the enrichment refactor (Task 4) is a pure seam change to
  working code — the tracer *is* that refactor, once it lands. Task 2 therefore draws the line explicitly: it
  changes nothing that works today (`LlmClient` still reads configuration until Task 4), and what is absent
  (probe, catalog, picker) is **absent rather than stubbed**. That distinction is stated in the task rather than
  left for the implementer to resolve.
- **(8) `TD-020` is taken.** The no-seed entry was numbered before PR #26 split `TD-019` and consumed that id.
  `TechnicalDebtRegisterTests` rule 1 would have failed the build at Task 7, after ~20 commits, on a doc edit.
  Renumbered to `TD-021`, and the instruction to re-read the open list before writing added, because the next
  branch may claim it too. **It then did:** Task 2's fix round took `TD-021` for the `with`-clone stamp
  contract under directive 6, so the no-seed entry is now `TD-022`. The generic "take the next free id"
  instruction overrides a reservation stated in the plan — the fix is to name the id in the step itself
  (done) and re-read the list immediately before writing (restated).
- **(9) The freeze covers this branch and it passes.** `new-shard.md:115` freezes the next two branches: a
  branch whose non-product commits outnumber its product commits is not a product branch. This plan commits
  5 product (Tasks 1–5) against 2 non-product (Tasks 7–8), so it clears the trigger without an exception — and
  the bow-tie branch that *would* have failed has already shipped.
- **(10) HTMX deferred to Task 5.** The original Task 4 introduced `hx-post` alongside a model picker it would
  serve. The tracer uses a plain form POST and redirect, because a partial swap with no content to swap is
  scaffolding. HTMX arrives where it has something to swap.

**Step scan.** Every step names a file, a signature, a test name, or a command. No step says "add appropriate validation." The only algorithm bodies deliberately omitted are the URI trim-and-combine rule, which Task 2 Step 5 and Task 3 Step 4 both define by invariant rather than by transcript, and the `/v1/models` JSON parse, which the test assertions fully determine.

**Type consistency.** `ServiceConnection`, `LlmRequestSettings`, `ModelCatalog`, `ServiceDescriptor`, and both changed interfaces appear once in Interfaces Established Here and are used with identical names and shapes in every task. `ILlmService.EnrichSupplementAsync` is explicitly marked unchanged so no implementer "helpfully" widens it.

**Review Focus coverage.** All five lines have a test in the task that owns the code: #1 → Tasks 2 and 3; #2 → Task 3 (`BuildsUriFromTheConnectionNotFromAClientBaseAddress`); #3 → Tasks 1 and 4; #4 → Task 4; #5 → Task 4.

**Proportion.** The plan is longer than the spec because it carries per-task steps, but code blocks are signatures, test names, and assertions — no method bodies are transcribed. The test names in Steps 1, 2, and 4 are the plan's real content: they are the decisions the implementer cannot make alone.
