# OpenCode Go Enrichment Fix — Briefing

**Branch:** `fix/opencode-go-enrichment` (branched off `origin/main` at `b99027f`).
**Executor:** `glm-flash` (GLM-5.3-Flash). **Controller:** this session.
**Design-review sign-off:** Human confirmed (2026-10-17):
1. Prefill `DefaultBaseUrl` as `https://opencode.ai/inference/go` on the `opencode` descriptor and strip trailing `/v1` and `/openai` (`/openai/v1`) in `ServiceEndpoint.Resolve` (closes `TD-025`).
2. Auto-select a working model (`glm-5.3-flash` if present in the verified catalog, else `catalog.Models[0]`) in `ProbeConnectionHandler` when `connection.Model` is blank on connect.
3. Amend `DESIGN.md`, `AGENTS.md`, `storymap.yaml`, `docs/factory/technical-debt.yaml` (+ regenerate `docs/factory/dashboard.html`), and `test-e2e.sh` in the same change.

**IMPORTANT — `PlanTickTests` rule:** Do NOT add any `- [ ]` lines to this file or any file under `docs/plans/`. `VitaTrack.ArchitectureTests/PlanTickTests` fails the build if any plan file changed on `origin/main...HEAD` contains an unticked `- [ ]`.

---

## Step 0 — Mandatory Reading List

Read these files before making any edits:
1. `VitaTrack.Core/Features/ServiceConnections/ServiceDescriptorRegistry.cs`
2. `VitaTrack.Core/Features/ServiceConnections/ServiceEndpoint.cs`
3. `VitaTrack.Core/Features/ServiceConnections/ProbeConnectionHandler.cs`
4. `VitaTrack.Web/Controllers/ServiceConnectionController.cs`
5. `VitaTrack.Tests/Features/ServiceConnections/ServiceDescriptorRegistryTests.cs`
6. `VitaTrack.Tests/Features/ServiceConnections/ServiceCatalogClientTests.cs`
7. `VitaTrack.Tests/Features/ServiceConnections/ProbeConnectionHandlerTests.cs`
8. `VitaTrack.Tests/ServiceConnectionControllerTests.cs`
9. `VitaTrack.Tests/LlmClientRequestTests.cs`
10. `e2e-tests/playwright/tests/service-connection.spec.js`

---

## Authorized Contract Changes

The human approved the following contract changes at the design-before-code gate. Updating the four existing test assertions listed below is **explicitly mandated by this briefing**, not test weakening:

1. `ServiceDescriptorRegistryTests.OpenCodeDescriptor_DefaultBaseUrl_IsEmpty` (`VitaTrack.Tests/Features/ServiceConnections/ServiceDescriptorRegistryTests.cs:176-183`):
   Rename to `OpenCodeDescriptor_DefaultBaseUrl_IsOpenCodeGoGateway` and assert `Assert.AreEqual(ServiceDescriptorRegistry.OpenCodeDefaultBaseUrl, descriptor!.DefaultBaseUrl);`.
2. `ServiceConnectionControllerTests.Index_WithNoConnection_RendersTheConnectForm` (`VitaTrack.Tests/ServiceConnectionControllerTests.cs:38`):
   Change `Assert.AreEqual(string.Empty, model.Form.BaseUrl);` to `Assert.AreEqual(ServiceDescriptorRegistry.OpenCodeDefaultBaseUrl, model.Form.BaseUrl);`.
3. `ServiceCatalogClientTests.ListModelsAsync_RequestsTheDocumentedCatalogPath` (`VitaTrack.Tests/Features/ServiceConnections/ServiceCatalogClientTests.cs:145`):
   Because `ServiceEndpoint.Resolve` now normalizes a trailing `/v1` on `"http://h/v1"`, change expected URI from `"http://h/v1/v1/models"` to `"http://h/v1/models"`.
4. `LlmClientRequestTests.PostChatAsync_BuildsUriFromTheConnectionNotFromAClientBaseAddress` (`VitaTrack.Tests/LlmClientRequestTests.cs:75`):
   Because `ServiceEndpoint.Resolve` now normalizes a trailing `/v1` on `"https://two.example/v1"`, change expected URI from `"https://two.example/v1/v1/chat/completions"` to `"https://two.example/v1/chat/completions"`.
5. `LlmServiceConnectionTests.EnrichSupplementAsync_PassesTheResolvedConnectionToTheCompletion` (`VitaTrack.Tests/LlmServiceConnectionTests.cs`):
   Because `ServiceEndpoint.Resolve` now normalizes a trailing `/v1` on `"https://svc.example/v1"`, change expected URI from `"https://svc.example/v1/v1/chat/completions"` to `"https://svc.example/v1/chat/completions"`.

Do not weaken or remove any other test assertion.

---

## Step 1 — Core & Web Implementation + Unit Tests (Commit 1)

### 1A. `VitaTrack.Core/Features/ServiceConnections/ServiceDescriptorRegistry.cs`
- Add constants:
  ```csharp
  public const string OpenCodeDefaultBaseUrl = "https://opencode.ai/inference/go";
  public const string DefaultCatalogModel = "glm-5.3-flash";
  ```
- Update `All` so the `opencode` descriptor uses `OpenCodeDefaultBaseUrl`:
  ```csharp
  public static IReadOnlyList<ServiceDescriptor> All { get; } =
  [
      new ServiceDescriptor(OpenCodeServiceId, "OpenCode", OpenCodeDefaultBaseUrl, OpenCodeHeaders)
  ];
  ```

### 1B. `VitaTrack.Core/Features/ServiceConnections/ServiceEndpoint.cs`
- Strip trailing `/v1` and `/openai` (and `/openai/v1`) before appending `relativePath` so pasting `https://opencode.ai/inference/go`, `https://opencode.ai/inference/go/v1`, `https://opencode.ai/inference/go/openai`, or `https://opencode.ai/inference/go/openai/v1` resolves to the same gateway root:
  ```csharp
  namespace VitaTrack.Core.Features.ServiceConnections;

  public static class ServiceEndpoint
  {
      public static Uri Resolve(string baseUrl, string relativePath)
      {
          var root = StripTrailingSuffix(baseUrl.TrimEnd('/'), "/v1");
          root = StripTrailingSuffix(root, "/openai");
          return new($"{root}/{relativePath.TrimStart('/')}", UriKind.Absolute);
      }

      private static string StripTrailingSuffix(string value, string suffix) =>
          value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
              ? value[..^suffix.Length].TrimEnd('/')
              : value;
  }
  ```

### 1C. `VitaTrack.Core/Features/ServiceConnections/ProbeConnectionHandler.cs`
- In `ProbeAsync`, when `connection.Model` is null/whitespace and the catalog verified with at least one model, auto-select `ServiceDescriptorRegistry.DefaultCatalogModel` (`"glm-5.3-flash"`) if present in `catalog.Models` (case-insensitive), else `catalog.Models[0]`:
  ```csharp
          var stamped = connection with
          {
              Model = string.IsNullOrWhiteSpace(connection.Model)
                  ? DefaultModelFromCatalog(catalog)
                  : connection.Model,
              Verification = catalog.Verified ? ServiceConnection.Verified : ServiceConnection.Unverified,
              // Cleared on the unverified path rather than left in place: the pair is read
              // together everywhere, and a stale VerifiedAt beside an unverified flag is a
              // contradiction the UI would have to interpret.
              VerifiedAt = catalog.Verified ? DateTimeOffset.UtcNow : null,
          };
  ```
- Add private helper on `ProbeConnectionHandler`:
  ```csharp
      private static string? DefaultModelFromCatalog(ModelCatalog catalog)
      {
          if (!catalog.Verified || catalog.Models.Count == 0)
              return null;

          return catalog.Models.FirstOrDefault(
                     m => string.Equals(m, ServiceDescriptorRegistry.DefaultCatalogModel, StringComparison.OrdinalIgnoreCase))
                 ?? catalog.Models[0];
      }
  ```

### 1D. `VitaTrack.Web/Controllers/ServiceConnectionController.cs`
- **File size warning:** `ServiceConnectionController.cs` (110 lines) + `ServiceConnectionController.Probe.cs` (160 lines) = 270 lines total across partials (limit is 300). Keep `PrefilledForm` concise:
  ```csharp
      private static ConnectServiceRequest PrefilledForm(ConnectServiceRequest form, ServiceConnection? active)
      {
          var service = string.IsNullOrWhiteSpace(form.Service) ? active?.Service ?? form.Service : form.Service;
          var fallbackUrl = active?.BaseUrl ?? ServiceDescriptorRegistry.Find(service)?.DefaultBaseUrl ?? string.Empty;
          return new()
          {
              Service = service,
              BaseUrl = string.IsNullOrWhiteSpace(form.BaseUrl) ? fallbackUrl : form.BaseUrl,
              ApiKey = string.Empty,
              Model = string.IsNullOrWhiteSpace(form.Model) ? active?.Model : form.Model,
          };
      }
  ```

### 1E. Unit Tests
- **ClaimCommentTests rule:** Any comment in `VitaTrack.Tests` using `CHECKS:` or `PINS:` must have an `Assert.` in its enclosing method. Do not use `CHECKS:`/`PINS:` outside a test method, and avoid guarantee words (`guarantees`, `always`, `never`, `ensures`) in comments.
- **`VitaTrack.Tests/Features/ServiceConnections/ServiceDescriptorRegistryTests.cs`:**
  Rename `OpenCodeDescriptor_DefaultBaseUrl_IsEmpty` to `OpenCodeDescriptor_DefaultBaseUrl_IsOpenCodeGoGateway` and assert `Assert.AreEqual(ServiceDescriptorRegistry.OpenCodeDefaultBaseUrl, descriptor!.DefaultBaseUrl);`.
- **`VitaTrack.Tests/Features/ServiceConnections/ServiceCatalogClientTests.cs`:**
  1. In `ListModelsAsync_RequestsTheDocumentedCatalogPath`, update line 145 to `Assert.AreEqual("http://h/v1/models", uri.ToString());`.
  2. Add test method `ListModelsAsync_NormalizesTrailingVersionAndOpenAiSegments`:
     ```csharp
     [TestMethod]
     public async Task ListModelsAsync_NormalizesTrailingVersionAndOpenAiSegments()
     {
         string[] inputs =
         [
             "https://opencode.ai/inference/go",
             "https://opencode.ai/inference/go/",
             "https://opencode.ai/inference/go/v1",
             "https://opencode.ai/inference/go/openai",
             "https://opencode.ai/inference/go/openai/v1",
         ];

         foreach (var input in inputs)
         {
             var handler = new RecordingHandler(HttpStatusCode.OK, CatalogBody);
             await Client(handler).ListModelsAsync(Connection(input));
             Assert.AreEqual(
                 "https://opencode.ai/inference/go/v1/models",
                 handler.LastRequest!.RequestUri!.ToString(),
                 $"input '{input}' should normalize to the gateway root");
         }
     }
     ```
- **`VitaTrack.Tests/LlmClientRequestTests.cs`:**
  **File size warning:** `LlmClientRequestTests.cs` is 289 lines (limit 300). Do NOT add lines. Only update line 75 from `"https://two.example/v1/v1/chat/completions"` to `"https://two.example/v1/chat/completions"`.
- **`VitaTrack.Tests/Features/ServiceConnections/ProbeConnectionHandlerTests.cs`:**
  Add test method `ProbeAsync_VerifiedProbe_WhenModelIsUnset_SelectsPreferredOrFirstCatalogModel` (disconnecting between segment 1 and segment 2 so segment 2 starts with `Model = null` rather than inheriting segment 1's model via reconnect carry-forward):
  ```csharp
  [TestMethod]
  public async Task ProbeAsync_VerifiedProbe_WhenModelIsUnset_SelectsPreferredOrFirstCatalogModel()
  {
      var firstId = await SaveAsync();
      await Handler(Catalog("deepseek-v4.1-flash", "glm-5.3-flash", "kimi-k3")).ProbeAsync();
      Assert.AreEqual("glm-5.3-flash", (await ActiveAsync()).Model);

      await _repository.DeleteAsync(firstId);
      await SaveAsync();
      await Handler(Catalog("stub-model-a", "stub-model-b")).ProbeAsync();
      Assert.AreEqual("stub-model-a", (await ActiveAsync()).Model);

      await _save.HandleAsync(new ConnectServiceRequest { BaseUrl = BaseUrl, ApiKey = ApiKey, Model = "custom-model" });
      await Handler(Catalog("glm-5.3-flash", "stub-model-a")).ProbeAsync();
      Assert.AreEqual("custom-model", (await ActiveAsync()).Model);
  }
  ```
- **`VitaTrack.Tests/ServiceConnectionControllerTests.cs`:**
  **File size warning:** `ServiceConnectionControllerTests.cs` is 277 lines (limit 300). Do NOT add lines. Only update line 38 from `Assert.AreEqual(string.Empty, model.Form.BaseUrl);` to `Assert.AreEqual(ServiceDescriptorRegistry.OpenCodeDefaultBaseUrl, model.Form.BaseUrl);`.

### 1F. Gate & Commit 1
1. Run `dotnet format VitaTrack.sln`
2. Run `./format-check.sh`
3. Run `dotnet test VitaTrack.sln -c Release` (all architecture and unit tests must pass, 0 failed).
4. Commit:
   ```bash
   git add VitaTrack.Core/ VitaTrack.Web/ VitaTrack.Tests/ .opencode/agents/glm-flash.md
   git commit -m "feat: default OpenCode Go base URL, normalize /v1 and /openai, auto-select model on verify"
   ```

---

## Step 2 — E2E Spec, Docs, Story Map, Debt Register & Dashboard (Commit 2)

### 2A. `e2e-tests/playwright/tests/service-connection.spec.js`
- **Line 62 (inside `should save a connection from the Settings page and show it after reload`):**
  Before `const baseUrl = 'https://api.example.com';`, assert that the empty Settings page prefills the OpenCode Go default URL:
  ```js
  await expect(page.getByLabel('Base URL')).toHaveValue('https://opencode.ai/inference/go');
  ```
- **Line 694 (inside `should post the chosen model and reasoning effort to the connection when enriching`, right after `await connectViaForm(page, service.baseUrl, apiKey, 'verified');` — where the preceding test disconnected so this is a fresh connect with `Model (optional)` blank):**
  Assert that connecting with `Model (optional)` blank automatically selected and saved `stub-model-a` from the verified catalog before the picker overrides it with `stub-model-b`:
  ```js
  await expect(page.locator('#connection-state table')).toContainText('stub-model-a');
  ```
- **Lines 530-541 (inside `should probe the same path for either form of the base URL trailing slash`):**
  Extend the loop to cover `/v1` and `/openai/v1` normalization (the discriminating test required by `TD-025`):
  ```js
  for (const baseUrl of [service.baseUrl, `${service.baseUrl}/`, `${service.baseUrl}/v1`, `${service.baseUrl}/openai/v1`]) {
    await connectViaForm(page, baseUrl, apiKey, 'verified');
    await expect(page.getByLabel('Model', { exact: true }).locator('option'))
      .toHaveText(['stub-model-a', 'stub-model-b']);
  }

  expect(service.requests.map(request => request.path)).toEqual([
    '/v1/models',
    '/v1/models',
    '/v1/models',
    '/v1/models',
  ]);
  ```

### 2B. `test-e2e.sh`
- Update line 49 prompt example from `https://gateway.example` to `https://opencode.ai/inference/go`.
- Update line 53 prompt example from `kimi-k2.7-code` to `glm-5.3-flash`.

### 2C. `DESIGN.md` & `AGENTS.md`
- **`DESIGN.md` (`:330-349`):** Update the `### Model and reasoning-effort choice` section to note that:
  - The connect form prefills `Base URL` from the descriptor's `DefaultBaseUrl` (`https://opencode.ai/inference/go`), and `ServiceEndpoint.Resolve` normalizes trailing `/v1` and `/openai` segments.
  - When a probe verifies a connection whose `Model` was left blank, `ProbeConnectionHandler` auto-selects `glm-5.3-flash` (when present in the catalog, else the first listed model) so the connection is immediately ready for enrichment without requiring a second submit.
- **`AGENTS.md` (`:59-63`):** Update the `LLM Integration` sub-bullets to record that:
  - The `opencode` descriptor prefills `DefaultBaseUrl` (`https://opencode.ai/inference/go`).
  - `ServiceEndpoint.Resolve` strips trailing `/v1` and `/openai` segments before appending `v1/models` or `v1/chat/completions` (TD-025).
  - A verified probe on a connection with no `Model` auto-selects `glm-5.3-flash` (if listed, else `catalog.Models[0]`).

### 2D. `storymap.yaml`
- Update `last_updated: 2026-10-17`.
- Under `SC-2` -> story `"Save a base URL, API key, and optional model and see it saved"`, add:
  `- unit: ServiceDescriptorRegistryTests.OpenCodeDescriptor_DefaultBaseUrl_IsOpenCodeGoGateway`
- Under `SC-3` -> story `"Test the service on connect and record whether the key was accepted"`, add:
  `- unit: ServiceCatalogClientTests.ListModelsAsync_NormalizesTrailingVersionAndOpenAiSegments`
- Under `SC-3` -> story `"Pick a model the service listed, or name the model by hand"`, add:
  `- unit: ProbeConnectionHandlerTests.ProbeAsync_VerifiedProbe_WhenModelIsUnset_SelectsPreferredOrFirstCatalogModel`

### 2E. `docs/factory/technical-debt.yaml` & `docs/factory/dashboard.html`
- Move `TD-025` from the `open:` list to the top of the `closed:` list in `docs/factory/technical-debt.yaml`.
- **Strict YAML formatting rules (`TechnicalDebtRegisterTests` + `DebtRegisterShapeTests`):**
  - `TD-025` must NOT appear in `open:` once added to `closed:`.
  - Every prose field in `closed:` (`title`, `where`, `what`, `interest`, `paydown`, `note`) MUST use `key: >-` at 4 spaces indent and its content lines at **exactly 6 spaces indent**.
  - Never write the characters `>-` inside the content of any scalar.
  - Include `closed: 2026-10-17` (plain scalar at 4 spaces indent, like `closed: 2026-10-02` on `TD-010`) and a `note: >-` explaining that `ServiceEndpoint.Resolve` now strips trailing `/v1` and `/openai` segments, `ServiceDescriptorRegistry` prefills `https://opencode.ai/inference/go`, `ProbeConnectionHandler` auto-selects `glm-5.3-flash` (or the first catalog model) when `Model` is unset on a verified probe, and `service-connection.spec.js` pins `/v1` and `/openai/v1` normalization against the stub.
- Run:
  ```bash
  python3 scripts/generate-factory-dashboard.py
  python3 scripts/test-generate-factory-dashboard.py
  python3 scripts/generate-factory-dashboard.py --check
  ```

### 2F. Full Gate Verification & Commit 2
1. `./format-check.sh`
2. `dotnet build VitaTrack.sln -c Release`
3. `dotnet test VitaTrack.sln -c Release`
4. `CI=true ./test-e2e.sh`
5. `python3 scripts/generate-factory-dashboard.py --check`
6. Commit all remaining changes (including this briefing file `docs/plans/2026-10-17-opencode-go-enrichment.md`):
   ```bash
   git add e2e-tests/ test-e2e.sh DESIGN.md AGENTS.md storymap.yaml docs/factory/technical-debt.yaml docs/factory/dashboard.html docs/plans/2026-10-17-opencode-go-enrichment.md
   git commit -m "docs: close TD-025, update DESIGN.md/AGENTS.md/storymap, and pin URL normalization in e2e"
   ```

---

## Step 3 — Align `SupplementLabelParser` Extraction Prompt with `Dosage.IsWellFormed` (Commit 3)

Live verification against OpenCode Go (`glm-5.3-flash`) on the BioCare Children's Mindlinxr label (`Vitamin A 1667iu 500µg Retinol Equivalent`, `Vitamin D3 400iu 10µg`) revealed that `SupplementLabelParser.BuildExtractionPrompt` (`VitaTrack.Core/Features/LlmEnrichment/SupplementLabelParser.cs:86`) asks for `"the amount per serving as shown on label"`, causing the LLM to emit compound strings like `"1667iu (500µg)"` or `"500µg Retinol Equivalent"` that `Dosage.IsWellFormed` (`PersistHierarchyAsync`) rejects.

### 3A. `VitaTrack.Core/Features/LlmEnrichment/SupplementLabelParser.cs`
- Update item 3 in `BuildExtractionPrompt` (`:86`) so the prompt states the single-unit rule `Dosage.IsWellFormed` enforces:
  ```csharp
  3. dosage - a single number and unit per serving using ONLY one of: mg, mcg, µg, g, ml, tsp, tbsp, tab, IU (e.g., '500mg', '15µg', '1000 IU'). If the label lists multiple equivalent units (such as both IU and µg/mg) or qualifiers (such as 'Retinol Equivalent' or 'RE'), output ONLY one number and unit (e.g., '500µg', never '1667iu 500µg' or '500µg RE').
  ```

### 3B. `VitaTrack.Tests/SupplementLabelParserTests.cs`
- Add test method `ExtractNutrientsAsync_PromptSpecifiesSingleRecognizedDosageUnit` asserting that the `userPrompt` passed to `ILlmClient.PostChatAsync` contains `"mg, mcg, µg, g, ml, tsp, tbsp, tab, IU"` and `"ONLY one number and unit"`.
- Add `- unit: SupplementLabelParserTests.ExtractNutrientsAsync_PromptSpecifiesSingleRecognizedDosageUnit` to `storymap.yaml` under `LLM-1` -> story `"Scrape manufacturer URL and extract nutrients from the connected service"`.

### 3C. Gate & Commit 3
1. `dotnet format VitaTrack.sln`
2. `./format-check.sh`
3. `dotnet test VitaTrack.sln -c Release`
4. `python3 scripts/generate-factory-dashboard.py && python3 scripts/generate-factory-dashboard.py --check`
5. Commit:
   ```bash
   git add VitaTrack.Core/Features/LlmEnrichment/SupplementLabelParser.cs VitaTrack.Tests/SupplementLabelParserTests.cs storymap.yaml docs/factory/dashboard.html docs/plans/2026-10-17-opencode-go-enrichment.md
   git commit -m "fix: instruct LLM extraction prompt to emit a single recognized dosage unit"
   ```

---

## Escalation Triggers

Stop, leave the working tree clean, and escalate in your final message if:
- Any complete type exceeds the 300-line split trigger (`FileSizeTests`).
- `TechnicalDebtRegisterTests`, `DebtRegisterShapeTests`, `StoryMapConsistencyTests`, `ClaimCommentTests`, or `PlanTickTests` fails after your edits and cannot be fixed within 2 attempts.
- Any E2E test in `CI=true ./test-e2e.sh` fails after 2 attempts.
