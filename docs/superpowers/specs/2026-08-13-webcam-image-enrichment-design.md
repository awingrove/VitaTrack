# Webcam / Image-Based Supplement Enrichment

## Problem

Creating a supplement today supports one auto-fill source: scrape the manufacturer URL
and parse the cleaned HTML via an LLM (`LlmService.EnrichSupplementAsync`). Users
without a clean product-page URL — or who prefer to read the physical packaging —
have no auto-fill option. We want a second source: capture two photos of the product
(front label + back nutrition facts) via the user's webcam or file upload, send both
to a vision-capable LLM, and return nutrients in the same `LlmResult` shape the
URL-scrape path already produces. The downstream nutrient editor and DB persistence
are unaffected.

## Decisions (from brainstorming)

- **Sources supported:** webcam capture + file upload (both available in each slot).
- **Vision model config:** add `VisionModel` (+ optional `VisionApiKey` /
  `VisionBaseUrl`) to `VitaTrackOptions`; fall back to `Model` / `ApiKey` / `BaseUrl`
  when unset. Current default `Model` (`kimi-k2.7-code`) is text-only; vision path
  needs a separate model (e.g. `gpt-4o-mini`, `gemini-2.0-flash`).
- **Image persistence:** discard after parse. No DB schema change, no filesystem
  storage, no retention surface.
- **Scope:** add image capture to both Create and Edit flows (symmetric with the
  existing URL-scrape path on Edit).
- **Source picker UX:** radio toggle (`URL scrape` / `Photos` / `None`) at the top
  of the form. Selecting one reveals its inputs and hides the others. `None`
  preserves the manual-entry path.
- **Capture UX:** two named slots — "Front (product name & manufacturer)" and
  "Back (nutrition facts)". Each slot has live webcam preview + Capture button +
  file-picker fallback + Retake. Either or both slots may be populated; the LLM
  parses whatever it receives.
- **Approach:** Approach 1 — two public methods on `ILlmService`, shared private
  helpers extracted into a new `LlmChatClient` to keep `LlmService.cs` under the
  300-line cap.

## Current Flow (reference)

1. `GET /Supplement/Create` — empty form with `ManufacturerUrl` field.
2. HTMX POST to `/Supplement/Enrich` (`SupplementController.cs:33`) →
   `LlmService.EnrichSupplementAsync` (`LlmService.cs:23`) scrapes URL,
   cleans HTML, sends to `v1/chat/completions`, parses JSON response into
   `LlmResult{Nutrients, SwapSuggestion, ExtractionError, NutritionJson}`.
3. Controller persists supplement + nutrients, returns `_NutrientEditor`
   partial into `#nutrient-editor-container`.
4. Edit flow mirrors this via `SupplementController.Edit` (`:92`) and
   `Review.cshtml` merge.

## Target Flow (image path)

1. `GET /Supplement/Create` — form now renders `_SourcePicker` partial with
   radio toggle. Default selection: `URL scrape` (existing behavior unchanged).
2. User selects `Photos` radio → JS hides `ManufacturerUrl` field, shows two
   capture slots (front/back).
3. User captures or uploads both images (front and back optional; either
   or both may be populated; the LLM parses whatever it receives). JS
   holds two Blobs in memory.
4. User fills Name / Brand / DailyDose / Cost (same as today), clicks Save.
5. HTMX POST `/Supplement/Enrich` with `hx-encoding="multipart/form-data"`,
   FormData carries text fields + `frontImage` + `backImage` Blob entries.
6. Controller branches on `source == "image"`, reads `IFormFile? frontImage,
   IFormFile? backImage`, wraps each in `SupplementImage { Bytes, MediaType,
   Label }`, calls `LlmService.EnrichSupplementFromImagesAsync(supplement,
   images)`.
7. New method builds OpenAI chat.completions request:
   `messages[0].content = [{type:"text", text:<prompt>}, {type:"image_url",
   image_url:{url:"data:image/jpeg;base64,..."}} × N]`, posts to vision
   model. Response parser shared with URL path via `LlmChatClient`.
8. Same `LlmResult` returns → controller runs identical persistence +
   `_NutrientEditor` partial path. Images discarded server-side after parse.
9. Edit flow: `SupplementController.Edit` branches identically; Review page
   receives merged nutrients as today.

## Architecture

Single `ILlmService`, two public methods. No new service abstraction, no
strategy factory (YAGNI per AGENTS.md "pragmatic MVC").

```
SupplementController.Enrich (POST)
   ├─ source == "url"    → LlmService.EnrichSupplementAsync(supplement)             [existing, untouched]
   └─ source == "image"  → LlmService.EnrichSupplementFromImagesAsync(supplement,
                                                                           images)   [new]

SupplementController.Edit (POST) — same branch
```

Both methods return the same `LlmResult`; controller's downstream code
(`_suppRepo.AddAsync` → `_nutrientService.AddAsync` → return `_NutrientEditor`
partial) is identical, `SupplementController.cs:40-56`.

### Line-budget refactor

`LlmService.cs` is at 284 lines today (near the 300-line hard limit,
AGENTS.md). Adding the new method + private request/response helpers will
exceed the cap. Extract request-building + response-parsing into a new
`LlmChatClient` (`VitaTrack.Infrastructure/Services/LlmChatClient.cs`,
≈80 lines, `internal static`). Both `EnrichSupplementAsync` and
`EnrichSupplementFromImagesAsync` call `LlmChatClient.SendAsync(...)`, which
takes the message payload and returns a parsed `LlmResult`. `LlmService.cs`
shrinks to a thin orchestrator ≈150 lines.

## Components & Files

### New files

| Path | Purpose | Approx lines |
|------|---------|--------------|
| `VitaTrack.Infrastructure/Services/LlmChatClient.cs` | Extracted request-build + response-parse. `internal static class` with `static async Task<LlmResult> SendAsync(HttpClient http, VitaTrackOptions opts, object[] messages, string model)`. Both enrichment methods call it. | 80 |
| `VitaTrack.Infrastructure/Models/SupplementImage.cs` | POCO `{ byte[] Bytes; string MediaType; string Label }`. | 10 |
| `VitaTrack.Web/wwwroot/js/supplement-source-toggle.js` | Radio toggle, webcam `<video>` + canvas capture, file `<input type=file>`, retake, FormData assembly. External `.js` per AGENTS.md CSP rule. | 120 |
| `VitaTrack.Web/Views/Supplement/_SourcePicker.cshtml` | Partial rendering radio + two capture slots. Reused in Create and Edit to avoid duplication. | 30 |
| `e2e-tests/playwright/tests/supplement-image-enrich.spec.js` | Vision flow E2E, env-gated on `LLM_API_KEY` + `VISION_MODEL`. | 80 |

### Modified files

| Path | Change |
|------|--------|
| `VitaTrack.Infrastructure/Services/ILlmService.cs` | +1 method: `Task<LlmResult> EnrichSupplementFromImagesAsync(Supplement, IReadOnlyList<SupplementImage> images)`. → 12 lines. |
| `VitaTrack.Infrastructure/Services/LlmService.cs` | Refactor `ExtractNutrientsWithLlmAsync` to call `LlmChatClient.SendAsync`; add `EnrichSupplementFromImagesAsync`. File shrinks (helpers extracted) — ends ≈150 lines. |
| `VitaTrack.Infrastructure/VitaTrackOptions.cs` | Add `string? VisionModel`, `string? VisionApiKey`, `string? VisionBaseUrl` (nullable; fall back to `Model`/`ApiKey`/`BaseUrl` when null). → ≈16 lines. |
| `VitaTrack.Web/appsettings.json` + `appsettings.Test.json` | Add `VisionModel: ""`, `VisionApiKey: ""`, `VisionBaseUrl: ""` templates (empty = fallback to non-vision values). |
| `VitaTrack.Web/Controllers/SupplementController.cs` | `Enrich` and `Edit` actions accept `IFormFile? frontImage, IFormFile? backImage` + `string? source`; branch by `source`. Shared `_NutrientEditor` return path. +≈20 lines each action. |
| `VitaTrack.Web/Views/Supplement/Create.cshtml` + `Edit.cshtml` | Render `_SourcePicker` partial, add `enctype="multipart/form-data"` + `hx-encoding="multipart/form-data"`, include `<script src="/js/supplement-source-toggle.js"></script>`. +≈5 lines each. |
| `VitaTrack.Web/Program.cs` | Append `blob:` to `img-src` (`'self' data: https: blob:`) so `<img data-thumb>` object URLs render. No `MaxRequestBodySize` change needed (Kestrel default 30MB handles 2 JPEGs). |
| `storymap.yaml` | See "Storymap updates" below. |

### No DB change

Images discarded after parse; `Supplements` schema untouched.

## Frontend UX detail

### Radio toggle (3 options)

- `URL scrape` (default; preserves current behavior) → shows existing
  `ManufacturerUrl` input, hides image slots.
- `Photos` → hides `ManufacturerUrl` input, shows two capture slots.
- `None` → hides both; submit saves supplement with no enrichment (manual
  nutrient entry path already works via `_NutrientEditor`).

Toggle is pure JS class swap (`hidden` Bootstrap class), no server roundtrip.

### Each capture slot markup

```html
<div class="card mb-3" data-slot="front">
  <div class="card-header">Front (product name & manufacturer)</div>
  <div class="card-body">
    <div class="ratio ratio-4x3" data-preview-wrap>
      <video data-webcam autoplay playsinline muted></video>
      <img data-thumb hidden>
    </div>
    <div class="d-flex gap-2 mt-2">
      <button type="button" class="btn btn-success" data-capture>Capture</button>
      <button type="button" class="btn btn-outline-secondary" data-start-webcam>Enable webcam</button>
      <label class="btn btn-outline-secondary mb-0">
        Upload file <input type="file" accept="image/jpeg,image/png" hidden data-file-input>
      </label>
      <button type="button" class="btn btn-outline-danger" data-retake hidden>Retake</button>
    </div>
    <p class="text-muted small mb-0 mt-2" data-status>No image yet</p>
  </div>
</div>
```

Back slot identical with `data-slot="back"`, label "Back (nutrition facts)".

### JS behavior (`supplement-source-toggle.js`)

- On load: query
  `navigator.mediaDevices.getUserMedia({video:{facingMode:'environment'}})`.
  If granted, wire `<video>` srcObject. If denied/unavailable (no webcam),
  hide "Enable webcam" btn, leave "Upload file" — user can still submit
  images.
- `data-capture` click → draw `<video>` frame to offscreen `<canvas>` at
  native resolution (cap at 1920px longest edge to bound payload),
  `canvas.toBlob('image/jpeg', 0.85)` → store in `Map<slot, Blob>`, swap
  `<img data-thumb>` src to object URL, hide `<video>`, show Retake.
- `data-file-input` change → read `File` → same Blob map. Validate MIME ∈
  {image/jpeg, image/png}, size ≤ 10MB each (guardrail; Kestrel allows
  30MB). On invalid, set `data-status` text, don't store.
- `data-retake` click → clear slot Blob, swap back to `<video>`, reset
  status.
- Multipart submit: JS attaches `htmx:configRequest` listener → builds
  `FormData` from form + appends Blob entries keyed `frontImage` /
  `backImage`. HTMX sends as `multipart/form-data` automatically when
  `hx-encoding` set, no manual XHR needed.

### CSP

- `<video>` srcObject is from `getUserMedia` — not subject to `media-src`
  (live stream, no URL).
- `<img data-thumb>` src is `blob:` URL — `blob:` is **not** in current
  `img-src 'self' data: https:`. **Requires Program.cs update: append
  `blob:` to `img-src`** (`'self' data: https: blob:`). Small additive CSP
  change, still restrictive.

### Form attributes (Create.cshtml + Edit.cshtml)

```html
<form id="create-supplement-form"
      hx-post="/Supplement/Enrich"
      hx-encoding="multipart/form-data"
      hx-indicator="#enrich-spinner"
      hx-target="#nutrient-editor-container"
      hx-swap="innerHTML">
```

At bottom of Create, include:

```html
<partial name="_SourcePicker" />
<partial name="_NutrientEditorContainer" />
<script src="/js/supplement-source-toggle.js"></script>
```

External `<script src>` per AGENTS.md CSP rule — htmx re-executes on
partial swap so wires survive re-render.

## Vision LLM request format

OpenAI-compatible `v1/chat/completions`. Message content is an array
(text + image_url parts):

```json
{
  "model": "<VisionModel or fallback Model>",
  "messages": [
    { "role": "system",
      "content": "You are a supplement label parser. Extract structured nutrient information from supplement packaging images. Return ONLY valid JSON. Do NOT include markdown formatting, code blocks, or any text outside the JSON." },
    { "role": "user",
      "content": [
        { "type": "text", "text": "<extraction prompt — same field list as URL path>" },
        { "type": "image_url",
          "image_url": { "url": "data:image/jpeg;base64,..." } },
        { "type": "image_url",
          "image_url": { "url": "data:image/jpeg;base64,..." } }
      ] }
  ],
  "max_tokens": 16384,
  "temperature": 1.0
}
```

`reasoning_effort` added when set (same as URL path). Response JSON shape
identical to URL path — `LlmChatClient.SendAsync` parses the same
`{nutrients:[...], swapSuggestion:"..."}` structure.

## Error handling

Mirror existing `LlmService` error paths:

- No images provided → `ExtractionError = "No images provided"`, no API
  call.
- `VisionApiKey` / `VisionBaseUrl` missing AND `ApiKey` / `BaseUrl`
  missing → `ExtractionError = "LLM API key not configured"`, no API call.
- Image exceeds 10MB client-side guardrail → JS sets status text, drops
  the file. Server-side: validate `IFormFile.Length` ≤ 10MB, return
  `ExtractionError` if exceeded (defense in depth).
- LLM returns non-2xx or empty content → same error strings as URL path.
- Malformed JSON in content → same parser path (existing
  `cleanedContent` strip + `JsonSerializer.Deserialize`).
- Webcam permission denied → JS hides capture button, leaves file upload;
  no server error.
- Any image-dependent failure must leave the supplement savable with
  manual nutrient entry (the existing no-enrichment path).

## Testing strategy

### Unit tests (`VitaTrack.Tests`)

`LlmServiceTests.cs` extension (mock `HttpClient` via `HttpMessageHandler`,
mirrors existing tests):

- `EnrichFromImages_HappyPath_ReturnsNutrients` — both images sent,
  mocked API returns valid JSON, asserts Nutrients populated + request
  body contains `image_url` parts with `data:` URLs.
- `EnrichFromImages_NoImages_ReturnsError` — empty list, no API call.
- `EnrichFromImages_NoApiKey_ReturnsError` — both ApiKey + VisionApiKey
  null, no API call.
- `EnrichFromImages_VisionModelConfig_UsedWhenSet` — request body's
  `model` field equals `VisionModel` when set, falls back to `Model`
  when null.
- `EnrichFromImages_MalformedResponse_ReturnsError` — API returns
  non-JSON content, asserts ExtractionError set, Nutrients empty.
- `EnrichFromImages_ApiError_ReturnsError` — non-2xx response, asserts
  ExtractionError set.

`SupplementControllerTests.cs`:

- `Enrich_ImageSource_CallsEnrichFromImages` — mocked `ILlmService`,
  asserts correct method invoked, `IFormFile` passed through.
- `Enrich_UrlSource_CallsEnrichSupplement` — existing path still works.

### E2E tests (Playwright)

- `supplement-image-enrich.spec.js` — webcam capture happy path; gated
  on `LLM_API_KEY` + `VISION_MODEL` env (skip pattern mirrors existing
  `supplement-llm-integration.spec.js:7-8`).
- Augment `supplement-crud.spec.js` with source-toggle behavior tests
  (no API key needed — UI state only): radio toggles between
  URL/Photos/None, relevant inputs show/hide.
- Augment `supplement-llm-flow.spec.js` with "no URL, no images → no
  API call" assertion when `None` selected.

Playwright E2E caveat: webcam is not available in headless Chromium
without a fake video device. Webcam-capture E2E tests must either (a) be
skipped in CI with a `test.skip` when `navigator.mediaDevices` is
unavailable, or (b) use Playwright's `--use-fake-device-for-media-stream`
launch arg + `--use-fake-ui-for-media-stream` (grant permission
automatically). The file-upload path is fully testable in headless
Chromium via `setInputFiles` — that becomes the primary E2E coverage;
webcam tests are best-effort.

## Storymap updates

Update happens additively — existing entries untouched.

### A. `Manage Supplements > Add a supplement` (after current stories)

```yaml
        stories:
          - title: Pick auto-fill source (URL scrape vs photos vs none)
            status: todo
            priority: high
            tests:
              - e2e: supplement-source-toggle shows/hides inputs by radio
          - title: Capture front label via webcam and/or file upload
            status: todo
            priority: high
            tests:
              - e2e: supplement-webcam-capture front slot
              - e2e: supplement-file-upload front slot
          - title: Capture back nutrition-facts panel via webcam and/or file upload
            status: todo
            priority: high
            tests:
              - e2e: supplement-webcam-capture back slot
              - e2e: supplement-file-upload back slot
```

### B. `Manage Supplements > Edit a supplement`

```yaml
        stories:
          - title: Update supplement details and re-enrich via LLM
            status: done   # existing — unchanged
            priority: high
          - title: Re-enrich a supplement from new photos on edit
            status: todo
            priority: medium
            tests:
              - e2e: supplement-edit re-enrich from images
```

### C. `LLM Enrichment` activity (new task before "Error handling")

```yaml
      - name: Auto-extract nutrients from images
        stories:
          - title: Send front+back images to vision-capable LLM, parse nutrients
            status: todo
            priority: high
            tests:
              - unit: 5 (mocked vision HTTP)
              - e2e: 1 (real vision API, skips without key)
          - title: Use configurable VisionModel separate from text-scrape Model
            status: todo
            priority: medium
            tests:
              - unit: LlmServiceTests.VisionModelConfig_UsedWhenSet
```

### D. `LLM Enrichment > Error handling` (add stories)

```yaml
          - title: Gracefully handle missing vision model config
            status: todo
            priority: low
            tests:
              - unit: LlmServiceTests.NoVisionModel_ReturnsError
          - title: Gracefully handle image parse failure
            status: todo
            priority: low
            tests:
              - unit: LlmServiceTests.ImageParseFailure_ReturnsError
          - title: Gracefully handle no images provided
            status: todo
            priority: low
            tests:
              - unit: LlmServiceTests.NoImages_ReturnsError
              - e2e: supplement-image-enrich missing both slots
```

Update `meta.last_updated` to the release date at implementation time.

## Open questions to track during implementation

- Whether `hx-encoding="multipart/form-data"` on an HTMX form reliably
  carries `IFormFile` bindings in ASP.NET Core MVC (verify in a smoke
  test before relying on it; falls back to manual `FormData` + `fetch`
  POST to a dedicated endpoint if HTMX multipart proves flaky).
- Whether the boot-of-app DB seed should mention the new image path in
  any seed supplement (likely no — seed supplements carry ManufacturerUrl).
- Whether to surface a hint in the Create form telling the user to grant
  webcam permission in the browser prompt (yes, via the `data-status`
  text on the slot).

## Out of scope

- Barcode scanning (would be a third source; addressed in a future
  story).
- Persisting images for re-extraction or audit (explicit decision:
  discard after parse).
- Mobile-specific capture UX tuning (PWA, multi-camera selection).
- Streaming/partial LLM responses (existing API uses non-streaming).