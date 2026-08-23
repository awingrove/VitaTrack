# Report Cost Semantics Fix

## Context
Review found: `Supplement.Cost` is a bottle price (e.g. 60 tablets), but `ReportingService` treats it as a per-serving figure (`monthlyCost = Cost * FrequencyPerDay`). Nutrient math also uses raw `FrequencyPerDay` while cost path clamps `<=0 -> 1`. Reports have zero unit tests; report E2E specs only arrive by deep-URL `goto()`.

## Decisions
- New column `Supplements.ServingsPerBottle REAL NULL` — how many servings one bottle contains.
- Monthly cost per dose = `(Cost / ServingsPerBottle) * FrequencyPerDay * 30`. Excluded from cost figures when Cost or Servings missing/<=0.
- Frequency clamp unified: `dailyFrequency = max(FrequencyPerDay, 1)` used for BOTH nutrients and cost (matches existing cost behavior).
- Out of scope (known remaining gaps): nutrient-unit-aware totals (mg/IU/mcg summed raw), blend parent-vs-child double counting, £ hard-code.

## Tasks
- [x] RED: `ReportingServiceTests` — monthly formula, frequency clamp consistency (watched failing: `no column named ServingsPerBottle`)
- [x] GREEN: ServingsPerBottle through model/DbInit migration+seed/repositories/Create+Edit forms+CSV import
- [x] GREEN: ReportingService + views use new formula (NutrientReport total + per-supplement Estimated Monthly Cost column, CostReport Cost-per-Serving/Monthly)
- [x] Characterization tests: DosageParser (`DosageParserTests`), ReportingService date-filter/exclusion behavior
- [x] E2E: nav-click arrival test per report surface; blend badge test on nutrient index
- [x] storymap.yaml: refs for RP-1/RP-2 new tests, MS-7 blend-index display story, CSV servings test; last_updated 2026-08-23
- [x] Verify: dotnet test green (87+8), report/csv/nutrient e2e specs green, format-check OK

## Session Notes
- Subagent tooling broken this session (model error) — all work inline.
- TDD skill active. Coverage tests on existing code are characterization (pass immediately); fix-tests watched failing first.
- CSV header now `Name,Brand,DailyDose,ManufacturerUrl,Cost,ServingsPerBottle`; ServingsPerBottle optional per row.
- Full e2e run: only failure was real-API LLM spec with local key set (external API returned error alert) — environmental, CI skips without secret.
