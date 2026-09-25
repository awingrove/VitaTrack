# Debt paydown — TD-003 (`Dosage` parse authority + free-text unit leak), TD-006 (layering guard vacuity), TD-011 (unenforced dosage shape)

**Branch:** `refactor/dosage-vo-layering-guard` (base `3a43bcf` on `main`).
**Executor:** space-bunny-free (cheap, `$0`/1M). **Controller:** this session.
**Goal:** close the open debt entries in the register by (A) retiring `DosageParser` as the
sole parse authority, fixing the live free-text-unit leak, and rewriting TD-003 to state what
actually shipped; (B) retargeting the layering architecture test so it stops passing vacuously
and can be observed red; (C) enforcing the dosage shape at both write surfaces, which is how
TD-011 is paid.

**No ADR, no slice boundary, no schema change.** Storage stays `SupplementNutrients.Dosage TEXT NOT NULL`
(`Data/DbInit.cs:40`); no migration, no rollback surface.

**On TD-011 and the `TypeConverter`:** the register's literal paydown was to retype
`SupplementNutrient.Dosage` from `string` to `Dosage`. That is **superseded, not done**, by
decision recorded at Part C: the raw-string + `ParsedDosage` pair already shipped
(`SupplementNutrient.cs:31`) and *is* the right shape; the part that was actually missing was
**enforcement**, not the type. Consequences, all verified:

- `SupplementNutrient` is the form model on the CRUD surface —
  `Views/SupplementNutrient/Create.cshtml:5` and `Edit.cshtml:5` are `@model SupplementNutrient`,
  bound by `Create(SupplementNutrient nutrient)` / `Edit(int id, SupplementNutrient nutrient)`.
  A `readonly record struct` property would therefore have needed a `[TypeConverter]`:
  `SimpleTypeModelBinder` has no other path for an immutable struct, and the failure mode is a
  ModelState error, not a silent misparse.
- The entity is passed to Dapper **as the parameter object**
  (`SupplementNutrientRepository.cs:61`, `:74`), so a VO property cannot bind from a `TEXT`
  column without a private row DTO or a `SqlMapper.TypeHandler`.
- `Views/SupplementNutrient/Index.cshtml:83` renders `@nutrient.Dosage`; a VO property renders
  `ToString()` (`"500 mg"`, spaced) instead of the stored `"500mg"`, changing what users see and
  moving ~12 e2e exact-string selectors.

None of that cost is paid here, because none of it buys enforcement. Part C buys enforcement
without it.


## Why the register text is being rewritten, not just closed

`technical-debt.md:9-17` describes work that has already shipped. Verified current state:

- `VitaTrack.Core/Primitives/Dosage.cs:8-25` — `readonly record struct Dosage` (`Amount` + `Unit`, `Parse`, `IsDefined`, `ToString`). Shipped in `0053d5e`.
- `VitaTrack.Core/Features/Nutrients/SupplementNutrient.cs:31` — `public Dosage ParsedDosage => Primitives.Dosage.Parse(Dosage);`
- `VitaTrack.Core/Features/Reporting/NutrientAggregationVisitor.cs:38-49` — the report already reads `ParsedDosage`, not the string. `ReportingService` adopted `Unit` (`:29,100-102`).
- So the entry's *What* ("callers recombine amount + unit by hand") and the "pending the Nutrients slice work" note at `:17` are both stale.

What the entry's **Interest** line still names — "unit typos pass silently" — is a **live
defect**, and it is what this change pays.

## The live defect (TD-003, paydown item 1)

`DosageParser.UnitPattern` is `[^\d.]+` (`DosageParser.cs:17`), and `CanonicalUnit`'s
fall-through arm returns the token unchanged (`:38`). Therefore:

    Unit.Parse("3 capsules")       -> Symbol == "capsules",      IsDefined == true
    Unit.Parse("one tablet")       -> Symbol == "one tablet",    IsDefined == true
    Unit.Parse("500 mg with food") -> Symbol == "mg with food",  IsDefined == true

`NutrientAggregationVisitor.cs:39-46` admits any `IsDefined` unit into the report, and
`NutrientReport.cshtml:56,77,86,108,112` renders it as the unit suffix. `NormalizeDosage`
deliberately leaves these strings alone (`DosageParserTests.cs:72-74`). **A user who types
"3 capsules" sees "3 capsules" in the Nutrient Report's unit column.** No test covers free-text
dosage in `ReportingServiceTests` (only `"mg"` / `"IU, µg"` at `:136,152`).

`"3 tablets"` is deliberately **not** the example: under A1 it becomes a legitimate `3 tab`.
The defect is a token that is not a unit at all, not the presence of a count-noun.

Fix: `Unit.Canonicalize` returns `null` for any token outside the eight recognized symbols
(A1), and `Unit.Parse` maps that `null` to `default` (undefined). Persisted and displayed
nutrient strings are untouched — this changes only the *report read path*.

## Current-state facts (verified against the working tree on this branch — do not re-derive)

- `VitaTrack.Core/DosageParser.cs` (57 lines) — `ParseAmount` (`:9`), `ParseUnit` (`:19`),
  `CanonicalizeUnit` (`:28`) → private `CanonicalUnit` (`:30-39`), `NormalizeDosage` (`:45`).
  Regexes: `AmountPattern` `[\d]+\.?\d*` (`:7`), `UnitPattern` `[^\d.]+` (`:17`),
  `DosageShape` `^(?<prefix>\s*)(?<amount>\d+(?:\.\d+)?)(?<gap>\s*)(?<unit>\D+)$` (`:41-43`).
  Nothing throws; every failure is a silent `0` / `string.Empty` / pass-through.
- `VitaTrack.Core/Primitives/Unit.cs` (31 lines) — `Parse` (`:24-28`) delegates to
  `DosageParser.ParseUnit` + `CanonicalizeUnit`. Six statics at `:17-22`. `ToString()` (`:30`).
- `VitaTrack.Core/Primitives/Dosage.cs` (26 lines) — `Parse` (`:21-22`) delegates to
  `DosageParser.ParseAmount` + `Unit.Parse`. `ToString()` (`:24-25`) is
  `Amount.ToString("0.##")` — **current-culture-sensitive**, and not a persistence format
  (nothing writes `ToString()` output to the DB).
- **Every** `DosageParser` member has exactly these callers, and no more:

  | Member | Callers |
  |---|---|
  | `ParseUnit` | `Primitives/Unit.cs:26` only |
  | `CanonicalizeUnit` | `Primitives/Unit.cs:27` only |
  | `ParseAmount` | `Primitives/Dosage.cs:22`, `Features/LlmEnrichment/LlmService.cs:65`, `:71` |
  | `NormalizeDosage` | `Data/DbInit.cs:109`, `Features/Nutrients/SupplementNutrientRepository.cs:56`, `:66` |

  → `DosageParser` is fully drainable; the file can be deleted.
- `LlmService.cs:65,71` wants the **amount only** (for the unitless `NutritionJson` map) and
  today gets `ParseAmount` → `0` on unparseable input. That tolerance must be preserved.
- `VitaTrack.ArchitectureTests/ServicesLayeringTests.cs` (37 lines) — NetArchTest 1.3.2,
  single filter `.That().ResideInNamespace("VitaTrack.Core.Services")` (`:22`), 3 bans
  (`:23-25`). `VitaTrack.Core/Services` **does not exist on disk**; the rule selects 0 types and
  `IsSuccessful == True`. The field is misnamed `InfrastructureAssembly` (`:12`) and the
  doc comment (`:14-17`) still says "business logic lives in Services".
- Retargeted naively (Core minus `Data` minus `Primitives`) **5 legitimate types fail**:
  `Features.Supplements.SupplementRepository`, `Features.Nutrients.SupplementNutrientRepository`,
  `Features.Dosing.PrescribedDoseRepository`, `Features.Family.FamilyRepository` (each
  `using System.Data;` + `using Dapper;` at `:2`/`:5`), and `VitaTrack.Core.ServiceCollectionExtensions`
  (`:1-2,22,42,48,94`). **No slice service references Dapper/Sqlite today** — the only
  `IDbConnection|Dapper` hit under `Features/` is a doc comment at `Features/Nutrients/SupplementNutrient.cs:28`.
- `VitaTrack.ArchitectureTests/FileSizeTests.cs:93` —
  `TypePattern = new(@"(?:record\s+)?(?:class|interface)\s+(\w+)")` — **omits `struct`**, so
  `Dosage`, `Unit`, `Money`, `DoseMultiplier`, `DosePeriod` are exempt from the 300-line
  complete-type limit. `NoCsFile_` (`:22`) still catches them per file.
- `VitaTrack.ArchitectureTests` is **not** a `ShardOwnershipTests` scanned root
  (roots are `VitaTrack.Web/Controllers`, `VitaTrack.Core`, `VitaTrack.Web/Views`,
  `VitaTrack.Web/wwwroot/js`, `VitaTrack.Tests` — `ShardOwnershipTests.cs:145-149`) → a new
  fixture file there needs no manifest change.
  `VitaTrack.Tests` **is** a scanned root → new files under `VitaTrack.Tests/Primitives/` are
  covered by the `VitaTrack.Tests/Primitives/**` allowlist glob; a **deleted** file's
  allowlist line must be removed in the same commit (a path resolving to zero files is an error).
- `shards.yaml` allowlist lines to delete when the files go: `:232`
  `VitaTrack.Core/DosageParser.cs`, `:242` `VitaTrack.Tests/DosageParserTests.cs`.
  `Primitives/**` (`:235`) and `VitaTrack.Tests/Primitives/**` (`:243`) already cover the new
  homes — no other manifest change in this change.
- `Dapper 2.1.79` is a `PackageReference` of `VitaTrack.Core` (`VitaTrack.Core.csproj:11`) and
  the arch test project references Core (`.csproj:19`) → a Dapper-using fixture in the arch
  project compiles **without a csproj change**. Step 0 must confirm; fallback is one
  `PackageReference` to `VitaTrack.ArchitectureTests.csproj`.
- `storymap.yaml` contains **no** reference to `DosageParserTests`, `ServicesLayeringTests`,
  `FileSizeTests`, or `CrossSliceSqlTests` (verified) → no story-map repointing required.
- Guards that stay green by construction: `CrossSliceSqlTests` (only reads slice `core:` lists
  and drops `*` patterns; no slice `core:` list changes), `UiReachabilityTests` (scans
  `Views/` + `Controllers/`; none change), `RepositoryNamingTests`, `EcosystemGuardrailTests`.
- No view, controller, JS, e2e spec, or CSV path changes. CSV import carries no nutrient
  dosage (`Features/Supplements/CsvImportService.cs:8`).

## Design (pre-decided)

### Part A — TD-003

**A1. `Unit` owns unit parsing, and the recognized set has exactly one home.** `Unit.Parse` gains
a private `UnitTokenPattern` (`[^\d.]+`, moved from `DosageParser.cs:17`) and a new
`internal static string? Canonicalize(string token)`. **Its signature changes**: it returns
`null` for an unrecognized token instead of passing the token through. That is what retires
`DosageParser.cs:38`'s `_ => unit` fall-through, and it means membership in the recognized set
is *defined* by this one function rather than by a list that could drift away from the switch
arms. `Parse` trims, calls `Canonicalize`, and returns `default` on `null`.
`Dosage.IsWellFormed` (C1) calls the same function, so the two can never disagree.

The **eight** canonical symbols, and the aliases that fold into them:

| Symbol | Statics | Aliases accepted (case-folded) |
|---|---|---|
| `mg` | `Milligram` | `MG` |
| `µg` (U+00B5) | `Microgram` | `μg` (U+03BC), `ug`, `mcg` |
| `g` | `Gram` | `G` |
| `ml` | `Milliliter` | `mL` |
| `tsp` | `Teaspoon` | `TSP` |
| `tbsp` | `Tablespoon` | `TBSP` |
| `tab` | `Tablet` | `tablet`, `tablets` |
| `IU` | `InternationalUnit` | `iu`, `Iu` |

Three decisions are baked in here and must not be re-litigated by the executor:

- `tbsp` is its **own** symbol and is **not** converted to `ml`. 1 tbsp ≈ 15 ml is an
  equivalence, not an identity; converting discards the form the user wrote. `tsp` is not
  converted to `ml` today either, so this is consistent.
- `tablet`/`tablets` both canonicalize to the invariant `tab`, not to each other. `Unit` is a
  `record struct` and `Dosage.ToString()` is `Amount + " " + Symbol`, so the symbol must be
  invariant with respect to the amount; a count-noun that varied with the number would render
  "3 tablet" or "1 tablets". The cost is accepted: the rendered text is "3 tab", and
  `NutrientAggregationVisitor` buckets by `Unit`, so `tablet` and `tablets` aggregate together
  rather than splitting into two unit rows.
- **No bare-number default.** `Dosage.Parse("1")` yields `Amount = 1`, `Unit` undefined — exactly
  as today. A lone "1" is not reinterpreted as one tablet. The tablet use case is served by
  typing "3 tablets" (→ `3 tab`), and the free-text per-day field is `Supplements.DailyDose`,
  which this change does not touch. The default was considered and rejected: silently
  relabelling a bare "500" as 500 tablets is a worse failure than leaving it unitless.

The only other behavior change is the rejection of unrecognized tokens.


**A2. `Dosage` owns amount parsing, a tolerant `TryParse`, and `Normalize`.**
`Dosage` gains `private static readonly AmountPattern` (`DosageParser.cs:7`),
`public static bool TryParse(string?, out Dosage)` (false when no digit run / `decimal.TryParse`
fails; unit may still be undefined), and `public static string Normalize(string?)` — the moved
`NormalizeDosage` body **verbatim**, including its `DosageShape` regex and its
spacing-preserving rebuild. `Normalize` is deliberately *not* `Parse(x).ToString()`: the latter
forces one space and trims trailing zeros, which would rewrite every persisted row.

**A3. `Dosage.ToString()` becomes culture-invariant.** `Amount.ToString("0.##", CultureInfo.InvariantCulture)`.
Output is byte-identical under the test/CI culture, so no existing assertion changes; under a
comma-decimal locale it stops writing `"1,5"`. This is the DL-001 "value objects must not lie"
rule (`technical-debt.md:136-138`) applied to a formatter.

**A4. Call sites re-point, behavior preserved.**
- `Data/DbInit.cs:109` → `Dosage.Normalize((string)row.Dosage)`
- `Features/Nutrients/SupplementNutrientRepository.cs:56,66` → `Dosage.Normalize(nutrient.Dosage)`
- `Features/LlmEnrichment/LlmService.cs:65,71` →
  `Dosage.TryParse(nutrient.Dosage, out var dose) ? dose.Amount : 0m` (same `0` fallback as today)

**A5. `DosageParser.cs` and `DosageParserTests.cs` are deleted.** No shim, no
`[Obsolete]` — a one-method static class that no longer parses is a name that lies.
`DosageParserTests.cs` currently holds **20 assertions across 11 methods** (`grep -c 'Assert\.'`),
not 18 — all 20 must survive the move with their intent intact.

**A6. Tests first, and the error edges get the most assertions** (DL-001).

> **Authorized contract change.** `VitaTrack.Tests/Primitives/UnitTests.cs` already exists
> (6 methods, 14 assertions, added in `0bfbb92`) — it is **not** a new file. Its
> `Parse_UnknownUnitPassesThrough` (`:42-47`) asserts
> `Unit.Parse("nonsense").Symbol == "nonsense"`, i.e. it pins **exactly the pass-through behavior
> A1 removes**. Deleting it and replacing it with the rejection table below is **authorized by
> this design**, not a test weakened to make a build pass: rejecting unrecognized tokens is the
> approved contract, and that assertion encodes the defect. The other five methods use only
> recognized tokens and stay untouched. Do not re-license the pass-through under another name.

- `VitaTrack.Tests/Primitives/DosageTests.cs` (exists, 7 assertions / 4 methods) gains: the full
  `TryParse` truth table; the `IsDefined` matrix — `""`→false, `"0"`→false, `"0mg"`→true,
  `"500 mg"`→true, `"3 capsules"`→true-with-undefined-unit and `"3 tablets"`→true-with-`tab`
  (pin both, so the zero/absent asymmetry is executable rather than folklore, and so nobody
  later mistakes a count-noun for an unrecognized token); every `Normalize` case migrated from
  `DosageParserTests` (including `"500mcg"`→`"500µg"` **with no space**, `"500 mg with food"`
  unchanged, `"2 x 500mg"` unchanged); and **`ToString` under `de-DE`** — set
  `CultureInfo.CurrentCulture` in a `try/finally`, assert `Dosage.Parse("1.5 mg").ToString()`
  is `"1.5 mg"`. That test is red before A3 and green after.
- `VitaTrack.Tests/Primitives/UnitTests.cs` (**extend, do not recreate**): the **acceptance**
  table — all eight canonical symbols plus every alias in the A1 table resolve `IsDefined == true`
  and carry the expected `Symbol` (so `tablet`, `tablets` and `tab` all yield `tab`; the existing
  `Parse_PassesThroughKnownUnits` becomes a misnomer once the fall-through is retired, so rename
  it — e.g. `Parse_RecognizesCanonicalSymbols` — and extend it with `tbsp`/`TBSP` and `tab`); the
  **rejection** table — `"3 capsules"`, `"one tablet"`, `"500 mg with food"`, `"50 mg/kg"`,
  `"20%DV"`, `""`, `null` all yield `!IsDefined`; and a test pinning the accepted set to exactly
  the eight symbols of the A1 table, so a later unit addition cannot land in the switch without a
  test noticing.
- `VitaTrack.Tests/ReportingServiceTests.cs` (owned by slice RP; content change only) gains the
  regression, **both directions** — this is the test that proves the fix and pins the
  count-noun decision at the same time:
  - a `SupplementNutrient` whose `Dosage` is `"3 capsules"` contributes **no** unit to the
    report — assert the report's unit set contains neither `"capsules"` nor `"3 capsules"`;
  - a `SupplementNutrient` whose `Dosage` is `"3 tablets"` contributes the unit `tab` — assert
    it is present, so a future "simplification" that drops count-nouns from the recognized set
    fails here rather than silently re-breaking a legitimate input.
- `VitaTrack.Tests/LlmServiceTests.cs` is **not edited**; it passing unmodified is the
  proof that A4 preserved behavior.

**A7. `FileSizeTests` learns about `struct`s.** Add `|struct` to `TypePattern` (`:93`), then run
the arch suite. **Dry-run result, already verified — do not re-derive: 0 violations with *and*
without the change.** The five structs that become countable are `Money` 53, `DoseMultiplier` 42,
`Unit` 31, `Dosage` 26, `DosePeriod` 22 lines — max 53 against a 300 limit, so no allowlist
entry is needed. If the arch suite nonetheless goes red, that is a **finding to report** (a VO
has genuinely exceeded 300 lines) — do **not** revert the regex, and do **not** add an allowlist
entry to force green.

**Also verified for this dispatch:** no nutrient dosage literal anywhere (seed, unit test, e2e
spec) contains a compound or unrecognized unit. The free-text literals that do exist —
`'1 capsule'`, `'1 tablet'`, `'1 scoop (5g)'`, `'1 serving'` — are all `Supplements.DailyDose`,
and `Supplement` has no `ParsedDosage` (only `SupplementNutrient` does,
`SupplementNutrient.cs:31`), so they never reach `Unit.Parse`. This dispatch must not change them.

### Part B — TD-006

**B1. Retarget the rule.** `ServicesLayeringTests` filter becomes
`ResideInNamespace("VitaTrack.Core")` + `DoNotResideInNamespace("VitaTrack.Core.Data")` +
`DoNotResideInNamespace("VitaTrack.Core.Primitives")` +
`DoNotHaveNameEndingWith("Repository")` + `DoNotHaveName("ServiceCollectionExtensions")`,
keeping the same three bans. The name-suffix carve-out is exactly the legitimate class
(`RepositoryNamingTests.cs:27-41` already draws this line); `Primitives` stays **inside** the
net, because a value object must never touch the DB.
Rename `InfrastructureAssembly` → `CoreAssembly` and rewrite the doc comment to name slices
instead of the retired `VitaTrack.Core.Services` namespace.

**B2. Non-vacuity assertion — the actual root cause.** Assert the selection contains
`ReportingService` by name (exact; a bare `Count > 0` misses partial vacuity). This is what
lets the next slice conversion silently shrink the net to nothing without anyone noticing.

**B3. Negative-path proof — the repo has none for any guardrail.** Add
`VitaTrack.ArchitectureTests/Fakes/SneakyDapperService.cs`: a class whose method touches
`Dapper.SqlMapper` and `System.Data.IDbConnection`. Add a test that runs the **same three bans**
over `Types.InAssembly(typeof(SneakyDapperService).Assembly).That().ResideInNamespace("VitaTrack.ArchitectureTests.Fakes")`
and asserts `IsSuccessful == false` with `FailingTypeNames` containing `SneakyDapperService`.
A rule never observed red is indistinguishable from a rule that cannot go red
(`BIBLIOGRAPHY.md:71-73`).

**B4. Same guard on the twin.** Add the non-vacuity sentinel to
`WebLayerDependencyTests` (8 controllers today, so not currently vacuous, but structurally
exposed). Rule-level, no fake needed.

### Part C — TD-011, enforced rather than retyped

Every write to `SupplementNutrients`, exhaustively (verified by grepping
`_nutrientRepo.AddAsync|UpdateAsync` and `ISupplementNutrientService` call sites):

| # | Write | Reached from | Gated by | Enforcement point |
|---|---|---|---|---|
| 1 | `SupplementNutrientController.Create` POST `:51` | the entity-bound form | `ModelState.IsValid` `:47` | **C2** |
| 2 | `SupplementNutrientController.Edit` POST `:90` | the entity-bound form | `ModelState.IsValid` `:86` | **C2** |
| 3 | `SupplementNutrientService.PersistHierarchyAsync` `:82` (root), `:116` (child) | `SupplementController` `:53`, `:67`, `:161`, `:172` — LLM enrich + Review save | `NutrientFailure` list `:69` | **C3** |

`SupplementController.cs:118` (`existing.Dosage = llmNutrient.Dosage`) is **not** a fifth write:
it mutates a `SupplementNutrientDto` in a local list bound to `ViewData["ExtractedNutrients"]`
for the Review view, and reaches SQLite only later through `PersistHierarchyAsync`. C3 covers it.

**C1. One predicate, shared.** `public static bool Dosage.IsWellFormed(string? raw)` on the
value object — the single source of truth both surfaces call:

- `null`, `""`, whitespace → **true**. A blank dosage is legal for a blend child; the
  "required" rules already exist separately and must not be folded in here.
- no digit run → **false** (`"one tablet"`, `"abc"`).
- a non-digit run is present but `Unit.Canonicalize` returns `null` for it → **false**
  (`"3 capsules"`, `"500 mg with food"`, `"5mg x2"`). Note `"1 tablet"` **passes** — `tablet`
  canonicalizes to `tab`; it is `"one tablet"` that fails, on the missing amount.
- otherwise → **true**, including **amount-only** (`"500"`, `"1.5"`, `"1"`) and `"0mg"`.

Amount-only stays legal on purpose: that is today's behavior, and the defect is a *wrong* unit,
not a missing one. Rejecting `"500"` would be a new restriction this change has no mandate for,
and per the A1 decision a bare number is **not** reinterpreted as tablets. `"0mg"` must stay
valid — it is a seed row (`Data/DbInit.cs:149`) and the app's own data may not fail the app's
own new rule.

**Known limitation, accepted by decision:** compound units are rejected. `"50 mg/kg"` extracts
the token `mg/kg`, which canonicalizes to `null`, so `IsWellFormed` is false and the form errors.
`Unit` models a *single* unit, so accepting a compound would mean letting a string back into
`Unit` — re-opening the defect this change exists to close. Potency-per-kg and `%DV` are genuine
supplement-label values, so this limitation is recorded in TD-011's closed entry and in the
`AGENTS.md` dosage bullet, where a future agent will look before "fixing" it. It is a decision,
not an open debt entry.

**C2. CRUD surface → `SupplementNutrient.IValidatableObject`.** `Validate` (`:35-54`) already
yields "Top-level nutrients require a dosage." for a blank dosage. Add, inside the
`ParentNutrientId == null` branch and **after** the blank check, a shape check. Add a
shape-only check for the `ParentNutrientId != null` branch, which today validates nothing —
reachable via a crafted form body on Edit, and the same defect reaches the report through blend
children (seed child dosage `'200mg'`, `Data/DbInit.cs:171-172`). This is the location the
repo mandates: "Conditional rules … live in the model's `IValidatableObject.Validate`, not in
views/controllers" (`VitaTrack.Web/AGENTS.md`), and `ModelState.IsValid` fires it at `:47`/`:86`.

**C3. LLM + Review surface → `SupplementNutrientService.PersistHierarchyAsync`.** Add a shape
failure to the root path immediately after the existing blank check (`:73-77`), and a
shape-only check to the child path (`:104-124`), which today validates nothing. Both use the
existing `NutrientFailure(name, reason)` mechanism and render through the existing
`BuildExtractionError` / `ExtractedNutrients` path — no new reporting channel.

**C4. Tests.**
- `VitaTrack.Tests/Primitives/DosageTests.cs` — the full `IsWellFormed` truth table, including
  every rejection case above and the `"0mg"`/`"500"` acceptances.
- `VitaTrack.Tests/SupplementNutrientValidationTests.cs` — the same rejections surfaced as
  `ValidationResult`s, plus a blend child with a bad dosage rejected while a blank child is not.
- `VitaTrack.Tests/SupplementNutrientServiceTests.cs` — new `NutrientFailure` cases for the root
  path and the child path.
- Fixtures that must remain **valid** (they will fail loudly if the predicate is wrong):
  `SupplementControllerTests.cs:205`, `:208`, `:209` (`"1"`, `"500mcg"`),
  `SupplementControllerUpdateNutrientsTests.cs:44`, `:56`.
- `VitaTrack.Tests/DosageParserTests.cs` assertions are migrated in Part A, not extended here.

## Task sequence — three dispatches, one branch

**Order is load-bearing: B → A → C.** Part C's predicate is defined in terms of
`Unit.Canonicalize` returning `null` for an unrecognized token, which is only true after A1.
Running C before A would produce a predicate that accepts `"3 capsules"`.

**Dispatch 1 (Part B, 3 files, 1 commit)**
1. Step 0: confirm `Dapper` compiles in the arch project without a csproj edit (build a
   one-line usage). If it does not, add the `PackageReference` and say so in the report.
2. Write the failing test first: the negative-path test (B3) must be **red** before the
   retarget if it is run against the old filter, and green after. Then rewrite the filter (B1),
   add the sentinel (B2), add the Web sentinel (B4).
3. Gate: `dotnet test VitaTrack.ArchitectureTests` green; `Services_DoNotDependOnDataAssemblies`
   must still select ~60 types, not 0.
4. Commit: `test: retarget layering guard off the retired VitaTrack.Core.Services namespace (TD-006)`

**Dispatch 2 (Part A, 12 files touched, 3 commits)**
1. Commit 1 — `test+fix`: the new `UnitTests.cs` rejection table and the `de-DE` `ToString` test,
   red first, then A1–A3 green. Nothing deleted yet, and `Normalize` deliberately stays put so
   no duplicate implementation exists even for one commit.
2. Commit 2 — `refactor: delete DosageParser in favour of Dosage.Normalize`: re-point the 3 call
   sites (A4), delete `DosageParser.cs` + `DosageParserTests.cs`, remove `shards.yaml:232` and
   `:242` **in the same commit** (the pre-commit hook runs `ShardOwnershipTests` on every commit
   — DL-002 defect a), migrate all **20** `DosageParserTests` assertions into
   `Primitives/DosageTests.cs` + `Primitives/UnitTests.cs`.
3. Commit 3 — `test`: the `ReportingServiceTests` regression for the free-text unit leak, and the
   `FileSizeTests` `|struct` fix.

**Dispatch 3 (Part C, 5 files, 2 commits)**
1. Commit 1 — `feat`: `Dosage.IsWellFormed` + its full truth table in `Primitives/DosageTests.cs`,
   red first. No consumer yet.
2. Commit 2 — `feat`: C2 (`SupplementNutrient.IValidatableObject`) + C3
   (`SupplementNutrientService.PersistHierarchyAsync`) + their tests in
   `SupplementNutrientValidationTests.cs` and `SupplementNutrientServiceTests.cs`.
   `Dotnet test VitaTrack.sln` must be green, including the fixtures listed in C4.
3. Close-out (controller, not the executor): register edits + `AGENTS.md` + dashboard
   regeneration, per "Register and docs" below.

## Register and docs (same change, per the post-mortem rule)

- `docs/factory/technical-debt.md`: move **TD-003**, **TD-006** and **TD-011** to
  `## Closed entries`, each with a `Closed 2026-09-25:` bullet stating what shipped.

  - **TD-003** closed text must say the VO and `ParsedDosage` already shipped earlier, that
    `DosageParser` is now deleted, and that `Unit.Parse` now rejects an unrecognized unit so
    free text can no longer become a report unit.
  - **TD-006** closed text: the retarget, the sentinel, and the negative-path proof.
  - **TD-011** closed as **superseded, not delivered**. Its `Where`/`What`/`Interest` bullets
    are kept verbatim from the open entry; the `Closed` bullet must record that the register's
    stated paydown (retyping `SupplementNutrient.Dosage` to `Dosage`) was declined, that the
    shipped raw-string + `ParsedDosage` pair is the shape actually kept, and that the missing
    piece was enforcement — which is what Part C added, at
    `SupplementNutrient.IValidatableObject.Validate` and
    `SupplementNutrientService.PersistHierarchyAsync`. It must also record the two **accepted
    limitations**, so no later agent reads them as oversights: compound units (`50 mg/kg`,
    `20%DV`) are rejected because `Unit` models a single unit; and a bare number is **not**
    reinterpreted as tablets, so `Dosage.Parse("1")` has no unit.

  The three section headings stay in this exact order — the dashboard parser validates it
  (`e8427b3`): `## Open entries`, `## Closed entries`, `## Defect log`. New entries use the
  exact shape of TD-003, one flat `- **Label:** …` bullet per label, wrapped at the same width:

      ### TD-012 — <title>
      - **Where:** `path/File.cs:12`
      - **What:** <one sentence>
      - **Interest:** <what it costs per change>
      - **Paydown:** <the concrete fix>

  Add new open entries, in that format, for the debt this change *identifies but does not pay*.
  Ids continue from TD-011, which now closes:
  - **TD-012** — no architecture test asserts a non-empty selection; `WebLayerDependencyTests`
    now has one, `CrossSliceSqlTests` and `ShardOwnershipTests` still do not (the latter two go
    vacuous if the manifest empties; `SHELL` is already `core: []`, `shards.yaml:216`).
  - **TD-013** — `CrossSliceSqlTests.LoadCore` silently drops wildcard `core:` patterns
    (`:145`), which would quietly disable the ADR-0006 invariant for a slice.
  - **TD-014** — the register itself has no machine check (DL-004's unresolved half).
  - **TD-015** — stale `VitaTrack.Core/Services` references are back in the live docs — a TD-007
    regression: `VitaTrack.Core/AGENTS.md:8`, `:12`, `:70`, root `AGENTS.md:17`,
    `docs/ArchitectureReview.md:27`.
  - **TD-016** — `FileSizeTests` attributes a file's **entire** line count to every type
    declared in it, and is order-blind. Once A7 lands, a file declaring a 5-line struct and a
    300-line class fails on the *struct* key with a nonsensical message. Cheap today (largest
    struct is `Money` at 53 lines), but it will produce a confusing failure the day a large
    controller file also declares a small struct.
- `AGENTS.md:48` (the "Dosage Unit Normalization" bullet) — re-point from
  `DosageParser.NormalizeDosage` to `Dosage.Normalize` / `Unit.Parse`, and state the new
  rule in full: the eight recognized symbols are `mg`, `µg` (U+00B5), `g`, `ml`, `tsp`, `tbsp`,
  `tab`, `IU`; `Unit.Canonicalize` returns `null` for anything else and that null **is** the
  definition of "unrecognized" (`Dosage.IsWellFormed` calls the same function, so the two
  cannot drift); `tablet`/`tablets` fold to `tab`; `tbsp` is not converted to `ml`; compound
  units are rejected by decision; and a bare number carries no unit.
- `AGENTS.md` (Testing Philosophy) — record that a dosage-shape rule is enforced at **two**
  surfaces and that a rule verified on one page proves nothing about the other. The
  "Entry-Point Coverage" bullet already demands per-endpoint tests; name `Dosage.IsWellFormed`
  as the worked example.
- `VitaTrack.Core/AGENTS.md:8`, `:12`, `:70` and root `AGENTS.md:17` — delete the four stale
  `VitaTrack.Core/Services` references (the namespace is retired; `AGENTS.md:8` already says so
  three lines earlier). Also `docs/ArchitectureReview.md:27`.
- `docs/factory/dashboard.html` — **regenerate**, it is CI-freshness-gated
  (`.github/workflows/ci.yml`, `2d786ea`). Any register edit without a regeneration is a red CI.

## Gates (run before claiming done)

1. `dotnet format VitaTrack.sln --verify-no-changes`
2. `dotnet build VitaTrack.sln -c Release`
3. `dotnet test VitaTrack.sln -c Release` — arch + unit. Baseline on this branch: arch 15/15.
4. `python3 scripts/test-generate-factory-dashboard.py` — all green
5. `python3 scripts/generate-factory-dashboard.py --check` — exit 0
6. `./test-e2e.sh` — **no e2e spec is edited, but this gate is now load-bearing twice over**:
   A1 changes what the report renders, and C makes a form that previously saved `"3 capsules"`
   now return a validation error. `e2e-tests/playwright/nutrient-validation-surfaces.spec.js`
   and `supplement-nutrient.spec.js` are the specs most likely to notice. A red e2e that
   contradicts this paragraph is a **finding**, not something to paper over by loosening the
   rule — report it.

The pre-commit hook (`./scripts/install-pre-commit-hook.sh`) gates format + arch tests on every
commit. `dotnet test` and `./test-e2e.sh` are also runnable via `./test-unit.sh` / `./test-e2e.sh`.

## Out of scope

- Retyping `SupplementNutrient.Dosage` to `Dosage` — **declined by decision, not overlooked.**
  It needs a `[TypeConverter]` (the entity is the form model), a private Dapper row DTO or a
  `SqlMapper.TypeHandler` (the entity is the Dapper parameter object), a decision on the
  canonical persistence format, and a blank-vs-zero policy. Those four are design decisions
  with no mechanical answer; the enforcement in Part C buys the guarantee without them.
- Any change to what is persisted or displayed for a nutrient's dosage string. `Normalize`
  keeps its exact spacing-preserving behavior; `ToString` is display-only and has no DB caller.
- Free-text dosage **cleanup** of existing rows. A user who already stored "3 capsules" still
  sees it in the nutrient list; only the report's unit column stops treating it as a unit, and
  only new saves are rejected. Backfilling legacy rows is a data decision, not a refactor.
- `SHELL` having an empty `core:` list; the four duplicated `FindRepoRoot`/YamlDotNet loaders
  in the arch project; a shared arch-test helper refactor.
- No `storymap.yaml` edit (verified: no refs to the touched test classes), no new shard, no
  ledger entry — this is debt paydown, not a shipped slice, so `shard-metrics.yaml` is untouched
  (the dashboard work on 2026-09-24 set the same precedent).

## Escalation

Stop, log the deviation, and make the final message the escalation if:

- A `DosageParser` member has a caller the table above does not list.
- Deleting `DosageParser.cs` requires a manifest change beyond removing `shards.yaml:232`/`:242`.
- The `FileSizeTests` `|struct` edit turns the arch suite red (report the offending type; do not
  revert the regex).
- `Unit.Parse`'s new rejection breaks any currently-green test that asserts an arbitrary token
  becomes a `Unit` — that is a behavior change beyond this change's mandate; report it.
- `Dosage.IsWellFormed` rejects any value that `Data/DbInit.EnsureCreated` seeds, or any fixture
  in the C4 list. The app's own data failing its own new rule is a defect in the predicate.
- An e2e spec fails because a dosage it once saved freely is now rejected, **and** the spec is
  asserting the old permissive behavior as intended. Report; do not re-loosen the rule.
- A register edit breaks `test-generate-factory-dashboard.py` (parser format is strict).
