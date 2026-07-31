# Plan: Auto-discover Manufacturer URL

## Status
Draft — ready for implementation

## User Flow
1. User enters Name + Brand on Create supplement form
2. Clicks [Find URL] button (HTMX POST)
3. Loading spinner appears
4. App searches DuckDuckGo for `"{name}" {brand} supplement facts`
5. Top result URL populates ManufacturerUrl field
6. User reviews/edits URL, then submits
7. Existing LLM enrichment flow runs

## New Files
| File | Purpose |
|------|---------|
| `Infrastructure/Services/IUrlDiscoveryService.cs` | Interface: `Task<string?> FindUrlAsync(string name, string brand)` |
| `Infrastructure/Services/DuckDuckGoSearchService.cs` | Scrapes DuckDuckGo HTML, parses with AngleSharp, filters junk |
| `VitaTrack.Tests/UrlDiscoveryServiceTests.cs` | Unit tests with mocked HTTP |

## Modified Files
| File | Change |
|------|--------|
| `VitaTrack.Web/Controllers/SupplementController.cs` | Add `[HttpPost] FindUrl` action |
| `VitaTrack.Web/Views/Supplement/Create.cshtml` | Add HTMX "Find URL" button next to ManufacturerUrl field |
| `VitaTrack.Web/Views/Supplement/Edit.cshtml` | Add HTMX "Find URL" button next to ManufacturerUrl field |
| `VitaTrack.Infrastructure/ServiceCollectionExtensions.cs` | Register `IUrlDiscoveryService` |

## Technical Details

### Search
- **URL:** `https://html.duckduckgo.com/html/?q="Vitamin+C"+NatureMade+supplement+facts`
- **HTTP client:** Use existing "scraper" client
- **Parser:** AngleSharp (already in project)

### Result Filtering
- Parse all `<a class="result__a">` links from DuckDuckGo results
- Skip domains: youtube.com, facebook.com, twitter.com, reddit.com, wikipedia.org, tiktok.com, instagram.com
- Prefer HTTPS URLs
- Return first valid URL after filtering

### HTMX Button (Create.cshtml + Edit.cshtml)
```html
<div class="form-group">
    <label asp-for="ManufacturerUrl" class="control-label"></label>
    <div class="input-group">
        <input asp-for="ManufacturerUrl" class="form-control" />
        <button type="button" class="btn btn-outline-secondary"
                hx-post="/Supplement/FindUrl"
                hx-vals='js:{"name": document.getElementById("Name").value, "brand": document.getElementById("Brand").value}'
                hx-target="#ManufacturerUrl"
                hx-swap="value"
                hx-indicator="#find-url-spinner">
            Find URL
        </button>
        <span id="find-url-spinner" class="spinner-border spinner-border-sm htmx-indicator" role="status"></span>
    </div>
</div>
```

### Controller Action
```csharp
[HttpPost]
public async Task<IActionResult> FindUrl(string name, string brand)
{
    var url = await _urlDiscoveryService.FindUrlAsync(name, brand);
    return Content(url ?? "");
}
```

## Decisions
1. **Rate limiting** — No cooldown for now; DuckDuckGo is forgiving for low volume. Add cache later if needed.
2. **Result quality** — Return top result only; user can review and edit before submitting.
3. **Edit flow** — Yes, add "Find URL" to Edit page too (same HTMX pattern).
4. **API alternative** — Stick with DuckDuckGo (free, no key). Can swap to Bing API later if reliability is an issue.
5. **Search scope** — Top result only for simplicity. Multi-result picker is a future enhancement.

## Testing
- **Unit tests:** Mock HTTP with sample DuckDuckGo HTML responses; verify URL extraction and filtering
- **E2E tests:** Verify button triggers search and URL field gets populated

## Acceptance Criteria
- [ ] "Find URL" button appears next to ManufacturerUrl on Create and Edit pages
- [ ] Clicking button shows loading spinner
- [ ] Search result URL populates the field
- [ ] No results found → field stays empty, no error
- [ ] User can override/auto-edit the found URL
- [ ] Existing LLM enrichment still works with the auto-discovered URL
- [ ] Unit tests pass for URL discovery service
