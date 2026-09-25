# Debt paydown — TD-003 (`Dosage` parse authority + free-text unit leak) and TD-006 (layering guard vacuity)

**Branch:** `refactor/dosage-vo-layering-guard` (base `3a43bcf` on `main`).
**Executor:** space-bunny-free (cheap, `$0`/1M). **Controller:** this session.
**Goal:** close the two open debt entries in the register by (A) retiring
`DosageParser` as the sole parse authority, fixing the live free-text-unit leak, and
rewriting TD-003 to state what actually shipped; (B) retargeting the layering
architecture test so it stops passing vacuously and can be observed red.

**No ADR, no slice boundary, no schema change.** Storage stays `SupplementNutrients.Dosage TEXT NOT NULL`
(`Data/DbInit.cs:40`); no migration, no rollback surface. `Dosage`/`Unit` stay in
`VitaTrack.Core/Primitives/` — a new `TypeConverter` is deliberately **not** added (no
caller; Ford/Richards "no speculative scaffolding", `BIBLIOGRAPHY.md:73`).

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

    Unit.Parse("3 tablets")   -> Symbol == "tablets", IsDefined == true
    Unit.Parse("one tablet")  -> Symbol == "one tablet"
    Unit.Parse("500 mg with food") -> Symbol == "mg with food"

`NutrientAggregationVisitor.cs:39-46` admits any `IsDefined` unit into the report, and
`NutrientReport.cshtml:56,77,86,108,112` renders it as the unit suffix. `NormalizeDosage`
deliberately leaves these strings alone (`DosageParserTests.cs:72-74`). **A user who types
"3 tablets" sees "3 tablets" in the Nutrient Report's unit column.** No test covers free-text
dosage in `ReportingServiceTests` (only `"mg"` / `"IU, µg"` at `:136,152`).

Fix: `Unit.Parse` accepts only a token that canonicalizes to one of the six canonical
symbols; anything else is `default` (undefined). Persisted and displayed nutrient strings are
untouched — this changes only the *report read path*.

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

**A1. `Unit` owns unit parsing.** `Unit.Parse` gains a private `UnitTokenPattern`
(`[^\d.]+`, moved from `DosageParser.cs:17`) and an `internal static Canonicalize(string)`
(the moved `CanonicalUnit` switch). `Parse` trims, canonicalizes, and returns `default`
unless the result equals one of the six canonical symbols (`mg`, `µg`, `g`, `ml`, `tsp`, `IU`).
Alias behavior is **unchanged**: `mcg`/`ug`/`μg`→`µg`, `iu`→`IU`, `MG`→`mg` — all as today.
The only behavior change is the rejection of unrecognized tokens.

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

**A6. Tests first (TDD), and the error edges get the most assertions** (DL-001).
- `VitaTrack.Tests/Primitives/DosageTests.cs` (exists, 5 assertions) gains: the full
  `TryParse` truth table; the `IsDefined` matrix — `""`→false, `"0"`→false, `"0mg"`→true,
  `"500 mg"`→true, `"3 tablets"`→true-with-undefined-unit (pin it, so the zero/absent
  asymmetry is executable rather than folklore); every `Normalize` case migrated from
  `DosageParserTests` (including `"500mcg"`→`"500µg"` **with no space**, `"500 mg with food"`
  unchanged, `"2 x 500mg"` unchanged); and **`ToString` under `de-DE`** — set
  `CultureInfo.CurrentCulture` in a `try/finally`, assert `Dosage.Parse("1.5 mg").ToString()`
  is `"1.5 mg"`. That test is red before A3 and green after.
- `VitaTrack.Tests/Primitives/UnitTests.cs` (**new**): the alias table; the six statics;
  `ToString`; and the **rejection table** — `"3 tablets"`, `"one tablet"`, `"500 mg with food"`,
  `""`, `null` all yield `!IsDefined`, while `"mg"`/`"mcg"`/`"iu"`/`"MG"` do not.
- `VitaTrack.Tests/ReportingServiceTests.cs` (owned by slice RP; content change only) gains the
  regression: a `SupplementNutrient` whose `Dosage` is `"3 tablets"` contributes **no** unit to
  the report — assert the report's unit set does not contain `"tablets"`.
- `VitaTrack.Tests/LlmServiceTests.cs` is **not edited**; it passing unmodified is the
  proof that A4 preserved behavior.

**A7. `FileSizeTests` learns about `struct`s.** Add `|struct` to `TypePattern` (`:93`).
Dry-run this step against the gate: after the edit, `dotnet test VitaTrack.ArchitectureTests`
must be green. If it goes red, that is a **finding to report** (a VO has genuinely exceeded
300 lines) — do **not** revert the regex to force green.

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

## Task sequence — two dispatches, one branch

Dispatch 1 = Part B. Dispatch 2 = Part A. Order matters only in that Part B leaves the arch
suite healthy before Part A touches it.

**Dispatch 1 (Part B, ~3 files, 1 commit)**
1. Step 0: confirm `Dapper` compiles in the arch project without a csproj edit (build a
   one-line usage). If it does not, add the `PackageReference` and say so in the report.
2. Write the failing test first: the negative-path test (B3) must be **red** before the
   retarget if it is run against the old filter, and green after. Then rewrite the filter (B1),
   add the sentinel (B2), add the Web sentinel (B4).
3. Gate: `dotnet test VitaTrack.ArchitectureTests` green; `Services_DoNotDependOnDataAssemblies`
   must still select ~60 types, not 0.
4. Commit: `test: retarget layering guard off the retired VitaTrack.Core.Services namespace (TD-006)`

**Dispatch 2 (Part A, ~10 files, 3 commits)**
1. Commit 1 — `test+fix`: the new `UnitTests.cs` rejection table and the `de-DE` `ToString` test,
   red first, then A1–A3 green. Nothing deleted yet.
2. Commit 2 — `refactor`: re-point the 3 call sites (A4), delete `DosageParser.cs` +
   `DosageParserTests.cs`, remove `shards.yaml:232` and `:242` **in the same commit** (the
   pre-commit hook runs `ShardOwnershipTests` on every commit — DL-002 defect a), migrate the
   18 `DosageParserTests` assertions into `Primitives/DosageTests.cs` + `Primitives/UnitTests.cs`.
3. Commit 3 — `test` + `docs`: the `ReportingServiceTests` regression for the free-text unit
   leak, the `FileSizeTests` `|struct` fix, and the `AGENTS.md` updates below.
4. Close-out (controller, not the executor): register edits + dashboard regeneration, per
   "Register and docs" below.

## Register and docs (same change, per the post-mortem rule)

- `docs/factory/technical-debt.md`: move **TD-003** and **TD-006** to `## Closed entries` with
  a `Closed 2026-09-25:` bullet stating what shipped. TD-003's closed text must say the VO and
  `ParsedDosage` already shipped earlier, that `DosageParser` is now deleted, and that the
  full string→`Dosage` retyping of `SupplementNutrient.Dosage` (MVC binder, Dapper type
  handler, invariant persistence format, blank-vs-zero policy) is **deferred, not done**.

  The three section headings stay in this exact order — the dashboard parser validates it
  (`e8427b3`): `## Open entries`, `## Closed entries`, `## Defect log`. New entries use the
  exact shape of TD-003, one flat `- **Label:** …` bullet per label, wrapped at the same width:

      ### TD-011 — <title>
      - **Where:** `path/File.cs:12`
      - **What:** <one sentence>
      - **Interest:** <what it costs per change>
      - **Paydown:** <the concrete fix>

  Add new open entries, in that exact format, for the debt this change
  *identifies but does not pay*:
  - **TD-011** — `SupplementNutrient.Dosage` is still `string`; full VO adoption deferred (with
    the four decisions it needs).
  - **TD-012** — no architecture test asserts a non-empty selection; `WebLayerDependencyTests`
    now has one, `CrossSliceSqlTests` and `ShardOwnershipTests` still do not (the latter two go
    vacuous if the manifest empties; `SHELL` is already `core: []`, `shards.yaml:216`).
  - **TD-013** — `CrossSliceSqlTests.LoadCore` silently drops wildcard `core:` patterns
    (`:145`), which would quietly disable the ADR-0006 invariant for a slice.
  - **TD-014** — the register itself has no machine check (DL-004's unresolved half).
  - **TD-015** — stale `VitaTrack.Core/Services` references are back in the live docs — a TD-007
    regression: `VitaTrack.Core/AGENTS.md:8`, `:12`, `:70`, root `AGENTS.md:17`,
    `docs/ArchitectureReview.md:27`.
- `AGENTS.md:48` (the "Dosage Unit Normalization" bullet) — re-point from
  `DosageParser.NormalizeDosage` to `Dosage.Normalize` / `Unit.Parse`, and state the new
  unrecognized-unit rule.
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
6. `./test-e2e.sh` — expected green with **no** e2e change; run it anyway at the end, because
   A1 changes what the report renders and no e2e asserts the free-text case today.

The pre-commit hook (`./scripts/install-pre-commit-hook.sh`) gates format + arch tests on every
commit. `dotnet test` and `./test-e2e.sh` are also runnable via `./test-unit.sh` / `./test-e2e.sh`.

## Out of scope

- **Re**typing `SupplementNutrient.Dosage` to `Dosage` (TD-011). No MVC model binder, no
  `TypeConverter`, no Dapper type handler, no persistence-format change, no e2e selector rewrites.
- Any change to what is persisted or displayed for a nutrient's dosage string. `Normalize`
  keeps its exact spacing-preserving behavior; `ToString` is display-only and has no DB caller.
- Free-text dosage **cleanup** — a user who typed "3 tablets" still sees "3 tablets" in the
  nutrient list. Only the report's unit column stops treating it as a unit.
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
- A register edit breaks `test-generate-factory-dashboard.py` (parser format is strict).
