# Project Plan for VitaTrack

## Overview
This project aims to develop a comprehensive health tracking application that will help users manage their nutrient intake, monitor prescribed doses, track costs, and make informed supplement choices.

## Terminology
- **Supplement**: A product (e.g., "NatureMade Vitamin C") that has a **serving** (e.g., "2 tablets", "5ml") and optionally a **manufacturer URL**.
- **Supplement Nutrient**: A specific nutrient within a supplement, described by its **generic name** (e.g., "Zinc"), **specific form** (e.g., "Zinc Picolinate"), and **dosage** (e.g., "5mg"). Each supplement can have one or more nutrients.
- **Prescribed Dose**: A mapping from a **family member** to a **supplement** with a prescribed **dosage** (e.g., "500 mg"), **frequency per day**, and **instructions**.

## Tech Stack
- **Backend**: ASP.NET Core MVC (.NET 10), Dapper, SQLite (`VitaTrack.db`)
- **Frontend**: Razor views + Bootstrap 5 + vanilla JavaScript
- **LLM**: Any OpenAI-compatible API (OpenRouter, OpenAI, local servers), AngleSharp for HTML parsing
- **Unit tests**: MSTest + in-memory SQLite + Moq
- **E2E tests**: Playwright (Chromium), auto-provisioned web server

---

## Features — Status

| # | Feature | Status | Notes |
|---|---------|--------|-------|
| 1 | **Family Members** | ✅ Done | Full CRUD, 3 seeded members |
| 2 | **Supplements** | ✅ Done | Full CRUD, LLM enrichment via Review workflow |
| 3 | **Supplement Nutrients** | ✅ Done | Full CRUD per supplement, LLM auto-populates from manufacturer URL |
| 4 | **Prescribed Doses** | ✅ Done | Full CRUD, dropdowns, `FrequencyPerDay`, 3 seeded doses |
| 5 | **Cost Tracking** | ✅ Done | `Cost` field on Supplement; dedicated Cost Report view with per-supplement and per-member breakdown |
| 6 | **Daily Nutrient Reporting** | ✅ Done | Per-family-member matrix, grand totals, monthly cost estimate |
| 7 | **LLM Supplement Enrichment** | ✅ Done | Scrapes manufacturer URL → cleans HTML → LLM API → structured nutrients → Review → save |

---

## Implementation Plan

### Phase 1 — Research & Architecture [DONE]
- ✅ Design the application architecture (layered MVC: Controllers → Services → Repositories)
- ✅ Set up project structure, SQLite, Dapper, Bootstrap 5

### Phase 2 — CRUD & Core Features [DONE]

| Task | Status | Tests |
|------|--------|-------|
| Family Members CRUD | ✅ | 3 unit tests (in-memory SQLite) |
| Supplements CRUD | ✅ | 3 unit tests (in-memory SQLite) |
| Supplement Nutrients CRUD | ✅ | 4 unit tests (in-memory SQLite) |
| Prescribed Doses CRUD | ✅ | Covered by other repository patterns |
| Daily Nutrient Reporting | ✅ | Per-member nutrient matrix with grand totals |
| Cost-per-supplement breakdown | ✅ | Monthly cost estimate shown in report |

### Phase 3 — LLM Integration [DONE]

| Task | Status | Tests |
|------|--------|-------|
| OpenRouter API integration | ✅ | 5 unit tests (mocked HTTP) + 1 Playwright real-API integration test |
| HTML scraping & cleaning | ✅ | AngleSharp strips scripts/styles, extracts main content |
| Structured nutrient extraction | ✅ | Prompt-based JSON output, markdown code block stripping |
| Manual review workflow | ✅ | Editable nutrient table (add/remove rows), Review → ConfirmCreate/ConfirmEdit |
| Error handling (no URL, no key, fetch failure, malformed response) | ✅ | All covered by unit + integration tests |

### Phase 4 — Testing & QA [DONE]

| Task | Status |
|------|--------|
| Unit tests (repositories) | ✅ 10 tests — in-memory SQLite, seedData disabled for isolation |
| Unit tests (LLM service) | ✅ 5 tests — mocked HTTP for all scenarios |
| Playwright E2E (navigation) | ✅ 3 tests — home page title, navigation to family page |
| Playwright E2E (supplement nutrient CRUD) | ✅ 6 tests — display, add, edit, delete, multi-supplement, serving info |
| Playwright E2E (LLM UI flow) | ✅ 3 tests — Review page, manual add/save, remove & save (no real API) |
| Playwright E2E (LLM real API) | ✅ 1 test — real product URL → nutrients extracted → persisted (skips without API key) |
| Playwright E2E (nutrient report) | ✅ 4 tests — page load, supplements table, no-data/message, navigation link |
| Playwright E2E (prescribed doses) | ✅ 5 tests — index, create, edit, delete, navigation |
| Playwright E2E (cost report) | ✅ 4 tests — page load, supplement costs, member costs, grand total |
| **All test features covered** | ✅ |

---

## Bugs Fixed (2026-07-28)

| Bug | Root Cause | Fix |
|-----|-----------|-----|
| **All Playwright E2E tests failing (0/26)** | Playwright's `webServer.env` does not propagate env vars to `dotnet run` child process; app used default `VitaTrack.db` instead of `VitaTrack.Test.db` | Created `appsettings.Test.json` with test connection string; used `--environment Test` flag in `dotnet run` command |
| **CostReport page crashing (500)** | `RuntimeBinderException` — Razor's `dynamic` context cannot access named fields (`Item1`, `Item2`) on `ValueTuple` types | Changed `ReportingController` to project tuples into anonymous objects before passing to `ViewData` |
| **Error page cascading failure** | Non-dev environment's `UseExceptionHandler("/Home/Error")` redirected to non-existent `/Home/Error`, producing bare 500 | Added `HomeController.Error()` action and `Views/Home/Error.cshtml` view |
| **CostReport/NutrientReport empty** | No `PrescribedDoses` seed data; reports filter on active doses | Added 3 seeded `PrescribedDose` records in `DbInit.EnsureCreated` |

---

## Remaining Work

### 🟢 Nice-to-have
| Task | Description |
|------|-------------|
| **Delete confirmation modals** | Plain delete links work — modals are a polish item |

---

## Test Summary (Current)

| Layer | Count | Framework | Details |
|-------|-------|-----------|---------|
| **Unit** | **15** | MSTest + Moq | Repos: in-memory SQLite. Services: mocked HTTP. |
| **E2E Playwright** | **26** | Playwright (Chromium) | 3 home, 4 cost report, 4 nutrient report, 5 prescribed doses, 6 supplement nutrients, 3 LLM UI flow, 1 LLM integration |
| **Total** | **41** | | All passing ✅ |

---

## Architecture Notes

### Playwright E2E Test Infrastructure
- **DB Isolation**: `global-setup.js` deletes `VitaTrack.Test.db` before each run. The server starts with `--environment Test` which loads `appsettings.Test.json` pointing to `VitaTrack.Test.db`.
- **Server Startup**: `webServer.command` uses `dotnet run --urls http://localhost:5000 --environment Test`. Playwright's `webServer.env` is NOT used for connection strings (unreliable with `dotnet run`).
- **Parallel Tests**: Tests run with `fullyParallel: true` (4 workers). Tests that mutate shared DB state use dynamic assertions (e.g., `.last()`, relative counts) to handle cross-worker contamination.

---

## Timeline
- Start Date: 2026-02-15
- Completed: 2026-07-28
- Current status: **100% complete** — all CRUD + LLM + reporting + cost tracking done, all tests passing