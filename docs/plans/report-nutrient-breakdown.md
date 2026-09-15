# Plan: Click-to-expand nutrient totals in Per Family Member report

Branch: `feature/report-nutrient-units` (continues work committed in `9538242`).

## Goal

In the Per Family Member matrix on `/Reporting/NutrientReport`, clicking a
member's nutrient total (e.g. `500.00 mg`) expands a detail row beneath the
nutrient row listing the supplements that make up that total, each with its
contributing amount. The supplement name in each line links straight to
`/Supplement/Edit/{id}`.

## Confirmed design decisions

- **Expansion UI:** detail `<tr>` beneath the nutrient row, full-width
  (`colspan`), toggled with Bootstrap collapse attributes
  (`data-bs-toggle="collapse"`). No new endpoint, no JS file, CSP-safe
  (DESIGN.md bans inline handlers; HTMX lazy-load is overkill for this data
  volume).
- **Line format:** `Supplement (Brand) — 1000.00 mg (×2)` — daily amount
  (dosage × multiplier, same basis as the total), multiplier shown only when
  > 1, unit omitted when the nutrient's dosage has none.
- **Clickable cells:** only non-zero totals. Zero cells stay plain text.
- **Collapse behavior:** independent toggles; several breakdowns can be open
  at once for side-by-side comparison.
- **Supplement link:** plain anchor to `GET /Supplement/Edit/{id}` (existing,
  reachable action — no new endpoint, `UiReachabilityTests` unaffected).

## Data flow

`ReportingService.GetNutrientReportDataAsync` already loops
`activeDoses × nutrients` summing `memberTotals[memberId][genericName]`.
Extend that loop to also record, per (member, nutrient), the contributing
supplement and `ParseAmount(Dosage) × multiplier`. Aggregate per supplement
within a (member, nutrient) pair (two active doses of the same supplement sum
into one line; multiplier shown only when all contributing doses share the
same multiplier — otherwise omitted). Zero-amount contributions (blend
children with empty dosage) are dropped.

Transport follows the existing pattern: service builds it, controller
serializes to `ViewData`, view deserializes. Shape mirrors `MemberData`:
parallel list indexed by member → `Dictionary<nutrient, List<row>>` where row
is `{ supplementId, supplementName, brand, amount, multiplier }` (anonymous
objects through `ViewData` — never ValueTuples).

## View structure

For nutrient row *n*, member column *m*:

```html
<!-- total cell -->
<td>
  <button type="button" class="btn btn-link p-0"
          data-bs-toggle="collapse" data-bs-target="#bd-@n-@m"
          aria-expanded="false" aria-controls="bd-@n-@m">500.00 mg</button>
</td>
<!-- detail rows, rendered right after the nutrient row, one per member -->
<tr class="collapse" id="bd-@n-@m">
  <td colspan="1 + memberCount">
    <a asp-controller="Supplement" asp-action="Edit"
       asp-route-id="@id">Supplement (Brand)</a> — 500.00 mg
  </td>
</tr>
```

Bootstrap's `.collapse:not(.show) { display: none }` restores the natural
`table-row` display when shown — known-good pattern for `<tr>` targets.
Detail rows render only for (nutrient, member) pairs with contributions.
Zero cells render the formatted number as plain text. Table is not sortable
today (`table-sort.js` untouched).

## Edge cases

- Multiple active doses of the same supplement for one member → one summed
  line per supplement (see aggregation rule above).
- Blend children (empty dosage) contribute 0 → filtered out.
- Nutrient dosage without a unit → line shows amount only.
- Supplement deleted between render and click → `Edit` returns 404; acceptable.
- Excluded/expired doses never enter the loop → never appear as contributions.

## Tasks

- [x] **Service:** track contributions per (member, nutrient) during the dose
      loop; aggregate per supplementId; drop zero-amount entries; emit in
      `NutrientReportData`.
- [x] **Model + controller:** `ContributionRow`-shaped data on
      `NutrientReportData`; serialize as `ViewData["MemberContributions"]`.
- [x] **View:** clickable totals (`btn btn-link p-0` + collapse attributes)
      for non-zero cells; hidden detail `<tr>` per nutrient row; supplement
      anchors to Edit.
- [x] **Unit tests:** contributions sum to the displayed total; multiplier
      applied and surfaced; same-supplement doses aggregate; expired doses
      excluded from contributions.
- [x] **E2E:** click a total → detail row appears listing supplement line;
      click supplement name → lands on Edit page for that supplement (arrive
      by click, resolve ids via DOM — no hardcoded ids).
- [x] **Storymap:** add story under RP-1 referencing the new e2e test.
- [x] **Verify:** `dotnet build`, full `dotnet test`, `format-check.sh`,
      browser smoke of expand/collapse and the Edit jump.
