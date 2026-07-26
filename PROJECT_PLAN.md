# Project Plan for VitaTrack

## Overview
This project aims to develop a comprehensive health tracking application that will help users manage their nutrient intake, monitor prescribed doses, track costs, and make informed supplement choices.

## Terminology
- **Supplement**: A product (e.g., "NatureMade Vitamin C") that has a **serving** (e.g., "2 tablets", "5ml") and optionally a **manufacturer URL**.
- **Supplement Nutrient**: A specific nutrient within a supplement, described by its **generic name** (e.g., "Zinc"), **specific form** (e.g., "Zinc Picolinate"), and **dosage** (e.g., "5mg"). Each supplement can have one or more nutrients.
- **Prescribed Dose**: A mapping from a **family member** to a **supplement** with a prescribed **dosage** (e.g., "500 mg"), **frequency per day**, and **instructions**.

## Tech Stack
- **Backend**: ASP.NET Core MVC (.NET 10), Dapper, SQLite (`VitaTrack.db`)
- **Frontend**: Razor views + Bootstrap 5 + HTMX
- **LLM**: OpenRouter API (`openai/gpt-4o-mini`), AngleSharp for HTML parsing
- **Unit tests**: MSTest + in-memory SQLite + Moq
- **E2E tests**: Playwright (Chromium), auto-provisioned web server

---

## Features — Status

| # | Feature | Status | Notes |
|---|---------|--------|-------|
| 1 | **Family Members** | ✅ Done | Full CRUD, 3 seeded members |
| 2 | **Supplements** | ✅ Done | Full CRUD, LLM enrichment via Review workflow |
| 3 | **Supplement Nutrients** | ✅ Done | Full CRUD per supplement, LLM auto-populates from manufacturer URL |
| 4 | **Prescribed Doses** | ✅ Done | Full CRUD, dropdowns, `FrequencyPerDay` |
| 5 | **Cost Tracking** | △ Partial | `Cost` field on Supplement; no spending reports yet |
| 6 | **Daily Nutrient Reporting** | △ Partial | Controller exists, aggregates nutrient data; no date filter or per-family-member breakdown |
| 7 | **LLM Supplement Enrichment** | ✅ Done | Scrapes manufacturer URL → cleans HTML → OpenRouter API → structured nutrients → Review → save |

---

## Implementation Plan

### Phase 1 — Research & Architecture [DONE]
- ✅ Design the application architecture (layered MVC: Controllers → Services → Repositories)
- ✅ Set up project structure, SQLite, Dapper, BootStrap 5, HTMX

### Phase 2 — CRUD & Core Features

| Task | Status | Tests |
|------|--------|-------|
| Family Members CRUD | ✅ | 3 unit tests (in-memory SQLite) |
| Supplements CRUD | ✅ | 3 unit tests (in-memory SQLite) |
| Supplement Nutrients CRUD | ✅ | 4 unit tests (in-memory SQLite) |
| Prescribed Doses CRUD | ✅ | Covered by other repository patterns |
| ↓ ***Daily Nutrient Reporting** *** | **NEXT** | — |
| Nutrient report UI with date filter | △ Pending | Need Playwright test |
| Per-family-member nutrient breakdown | △ Pending | Need Playwright test |
| Cost-per-supplement breakdown | △ Pending | Need Playwright test |
| Actual daily intake = dosage × frequency × nutrient amount | △ Pending | — |

### Phase 3 — LLM Integration [DONE]

| Task | Status | Tests |
|------|--------|-------|
| OpenRouter API integration | ✅ | 5 unit tests (mocked HTTP) + 1 Playwright real-API integration test |
| HTML scraping & cleaning | ✅ | AngleSharp strips scripts/styles, extracts main content |
| Structured nutrient extraction | ✅ | Prompt-based JSON output, markdown code block stripping |
| Manual review workflow | ✅ | Editable nutrient table (add/remove rows), Review → ConfirmCreate/ConfirmEdit |
| Error handling (no URL, no key, fetch failure, malformed response) | ✅ | All covered by unit + integration tests |

### Phase 4 — Testing & QA [In Progress]

| Task | Status |
|------|--------|
| Unit tests (repositories) | ✅ 10 tests — in-memory SQLite, seedData disabled for isolation |
| Unit tests (LLM service) | ✅ 5 tests — mocked HTTP for all scenarios |
| Playwright E2E (navigation) | ✅ 5 tests — home, family, navigation links |
| Playwright E2E (nutrient CRUD) | ✅ 6 tests — display, add, edit, delete, multi-supplement, serving info |
| Playwright E2E (LLM UI flow) | ✅ 3 tests — Review page, manual add/save, remove & save (no real API) |
| Playwright E2E (LLM real API) | ✅ 1 test — real product URL → 24 nutrients extracted → persisted |
| ↓ **Test remaining features** | **NEXT** |
| Nutrient report E2E tests | △ Pending |
| Cost tracking E2E tests | △ Pending |
| Prescribed Dose E2E tests | △ Pending |

---

## Remaining Work (Prioritised)

### 🔴 Priority 1 — Daily Nutrient Reporting
| Task | Description |
|------|-------------|
| **Date range filter** | Add date picker to `Views/Reporting/NutrientReport.cshtml`, filter prescribed doses by date |
| **Per-family-member breakdown** | Group nutrients by family member using prescribed doses |
| **Actual intake calculation** | Multiply `SupplementNutrient.Dosage` × `PrescribedDose.FrequencyPerDay` for real daily values |
| **Cost column** | Show supplement cost contribution per day/month |
| **Playwright tests** | E2E tests for the report rendering with seeded data |

### 🟡 Priority 2 — Cost Tracking
| Task | Description |
|------|-------------|
| **Cost report** | New view showing total cost per supplement per month |
| **Per-family-member cost** | Group spending by family member |
| **Playwright tests** | E2E tests for cost report |

### 🟢 Priority 3 — UX Polish
| Task | Description |
|------|-------------|
| **Prescribed Dose Index** | Already JOINs FamilyMember & Supplement names — verify display works |
| **Navigation improvements** | Add reporting & prescribed dose links to navbar |
| **Delete confirmation modals** | Replace plain delete links with Bootstrap modals |

---

## Test Summary (Current)

| Layer | Count | Framework | Details |
|-------|-------|-----------|---------|
| **Unit** | **15** | MSTest + Moq | Repos: in-memory SQLite. Services: mocked HTTP. |
| **E2E Playwright** | **15** | Playwright (Chromium) | 5 navigation, 6 CRUD, 3 UI flow, 1 real API integration |
| **Total** | **30** | | All passing ✅ |

---

## Timeline
- Start Date: 2026-02-15
- Revised Completion: 2026-08-15  
- Current status: **~80% complete** — all CRUD + LLM done, reporting + cost tracking remain