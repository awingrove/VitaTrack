# Plan: Bug-Focused Test Coverage Round

**Goal:** Raise *meaningful* coverage by adding tests that catch real bugs, not pad
percentages. Unit tests target untested branches in security/error paths and business
logic; E2E tests verify a user can complete story-map journeys end-to-end with no
dead-ends or unexpected 500s.

**Current state (main, `./coverage-check.sh`):** Infrastructure 78.93% line / 71.66% branch.
Floor is 65%. After this round, ratchet floor to **85** (actual = 88.5% line / 83% branch).

**Status:** DONE. All unit (U1–U7) + E2E (E1–E5) added; suite green (123 unit/arch... actually
121 unit + 8 arch = 129 across the two test projects, all passing). Coverage raised to 88.53% line.

**Bug found & fixed during E4:** `SupplementNutrient.GenericName` required-ness was not enforced
on the bind surface. Root cause: `Program.cs` `SuppressImplicitRequiredAttributeForNonNullableReferenceTypes`
neuters the explicit `[Required]`, and the model's `IValidatableObject.Validate` did not check
`GenericName`. Fixed by adding the required-`GenericName` rule to `SupplementNutrient.Validate`
(the AGENTS-sanctioned home for conditional required rules). Verified by unit test
`SupplementNutrientValidationTests` + E2E `nutrient-validation-surfaces.spec.js`.

---

## Unit tests (VitaTrack.Tests) — target uncovered bug-prone branches

### U1. `UrlSafetyValidator` — SSRF guard (76.7% L, 61.8% B) [SECURITY]
File: `Services/UrlSafetyValidator.cs`. Uncovered: malformed URI, non-https scheme,
loopback/private IP, DNS-failure default-deny.
Tests (`UrlSafetyValidatorTests.cs`, `[Theory]`):
- `https://example.com` → safe (allow)
- `http://example.com`, `ftp://x` → unsafe (scheme)
- `not a url` → unsafe (parse fail)
- `https://127.0.0.1/`, `https://192.168.0.1/`, `https://10.0.0.1/`,
  `https://172.16.0.1/`, `https://169.254.169.254/` (cloud metadata) → unsafe (SSRF)
- DNS-failure branch: covered by a host that does not resolve in CI? Note: `Dns.GetHostAddresses`
  is static/internal — if flaky, accept uncovered and document; otherwise test via
  `IsPrivateOrReserved` on loopback IPv6 literals (`::1`, `fe80::`).

### U2. `LlmClient` error paths (65% L) — user-facing failure handling
Uncovered: non-2xx, empty `choices`, empty/whitespace `content`.
Tests (extend `LlmClientHeaderTests.cs` or new `LlmClientTests.cs`):
- API returns 4xx/5xx → `LlmCompletion` with error message, no throw
- 200 with `choices: []` → "No response from LLM"
- 200 with empty `content` → "Empty response from LLM"
- happy path already covered by `LlmServiceTests`

### U3. `SupplementNutrientService` — hierarchy persistence (AddAsync 0%, failure branches)
Uncovered: `AddAsync` (61-63), nested-grandchild (44-46), top-level-requires-dosage
(74-78), persist-exception (96-100, 120-124).
Tests (`SupplementNutrientServiceTests.cs` additions):
- `AddAsync` with root + nested children persists both levels
- root with child that itself has children → grandchildren flattened under parent
- top-level nutrient with blank dosage → `ReplaceNutrientsResult` records failure, not throw
- repository `AddAsync` throws → failure recorded, siblings still attempted

### U4. `SupplementLabelParser` branches (90.5% L, 33% B on 24-25)
- empty/whitespace label → returns empty nutrient list, no throw
- reasoning-effort absent path still parses

### U5. `CsvImportService` edges (89% L; lines 70,98-157)
- row missing optional `ServingsPerBottle` → parses with default
- row with bad numeric dosage → row error, not crash
- duplicate supplement names → handled/error as designed
- whitespace-only fields → rejected with message

### U6. `HtmlScraperService` failure/cleaning (61-78% L)
- fetch returns null → handled
- fetch throws → handled, returns null
- page cleans to empty → empty string, no throw

### U7. `DbInit` seed integrity (51% L)
- `EnsureCreated` on fresh in-memory DB → family members, supplements, nutrients,
  prescribed doses all seeded (counts > 0). Guards against report-breaking empty seed.

---

## E2E tests (e2e-tests/playwright) — no-hindrance user journeys

### E1. Full daily loop (cross-feature integration)
Family member → Supplement (with nutrient) → Prescribed dose for that member →
Nutrient Report shows member + nutrient totals; Cost Report shows cost. Verifies the
whole story-map chain wires together and reports reflect real data (not empty).

### E2. Enrich-without-URL inline editor round-trip
Create supplement without URL → add nutrients inline → save → nutrient appears on
nutrient index AND surfaces in Nutrient Report once a dose is prescribed.

### E3. Blend cascade delete (UI)
Create blend with child nutrients → delete blend parent → cascade warning shown →
confirm → children gone from nutrient index. Verifies app-enforced cascade not blocked.

### E4. Validation surfaced on every bind surface (Entry-Point Coverage rule)
Investigate every endpoint that binds the nutrient model (HTMX partial + standalone CRUD
page). For each, an E2E that submits a missing-required field shows a validation error,
not a 500. Add a test per surface.

### E5. Supplement delete cascade (UI)
Delete a supplement that has nutrients + prescribed doses → confirm → no FK error,
related rows gone. (Family-member cascade already has E2E; supplement side is unit-only.)

---

## Execution order
1. U1 (security) → U2 → U3 → U7 (high bug-risk, fast)
2. U4 → U5 → U6 (edge branches)
3. E1 → E3 → E2 → E5 → E4 (integration, validate no-hindrance)
4. After merge: bump `COVERAGE_THRESHOLD` to 75 in `coverage-check.sh`; update storymap
   `tests:` refs for any new stories/surfaces; run `format-check.sh` + full `dotnet test`.
