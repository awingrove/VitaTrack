using VitaTrack.Core.Features.Nutrients;
using VitaTrack.Core.Primitives;

namespace VitaTrack.Core.Features.Reporting;

/// <summary>
/// Named traversal for a supplement's blend tree during nutrient-report aggregation.
/// Visits each <see cref="SupplementNutrient"/> row of the flat list from
/// <c>ISupplementNutrientRepository.GetBySupplementIdAsync</c> exactly once — root rows
/// (<c>ParentNutrientId</c> is null) then their children. Every row with a defined
/// dosage contributes under its own <c>GenericName</c>, blends included. This class
/// owns the unit-collection and amount-accumulation policy (today's flat-loop rules)
/// so blend aggregation has exactly one future place to change.
/// </summary>
internal sealed class NutrientAggregationVisitor(
    Dictionary<string, HashSet<Unit>> nutrientUnits,
    Dictionary<int, Dictionary<string, decimal>> memberTotals,
    Dictionary<int, Dictionary<string, Dictionary<int, ContributionBucket>>> memberContributions)
{
    private readonly Dictionary<string, HashSet<Unit>> _nutrientUnits = nutrientUnits;
    private readonly Dictionary<int, Dictionary<string, decimal>> _memberTotals = memberTotals;
    private readonly Dictionary<int, Dictionary<string, Dictionary<int, ContributionBucket>>> _memberContributions = memberContributions;

    /// <summary>Accumulates one active dose's nutrient rows into the report aggregates.
    /// Ensures the member has a totals entry even when the supplement has no nutrient rows.</summary>
    public void Visit(int familyMemberId, int supplementId, decimal dailyFrequency, IReadOnlyList<SupplementNutrient> rows)
    {
        _memberTotals.TryAdd(familyMemberId, []);

        foreach (var row in rows.Where(r => r.ParentNutrientId is null))
            Accumulate(familyMemberId, supplementId, dailyFrequency, row);
        foreach (var row in rows.Where(r => r.ParentNutrientId is not null))
            Accumulate(familyMemberId, supplementId, dailyFrequency, row);
    }

    private void Accumulate(int familyMemberId, int supplementId, decimal dailyFrequency, SupplementNutrient n)
    {
        var dosage = n.ParsedDosage;
        if (dosage.Unit.IsDefined)
        {
            if (!_nutrientUnits.TryGetValue(n.GenericName, out var units))
            {
                units = [];
                _nutrientUnits[n.GenericName] = units;
            }
            units.Add(dosage.Unit);
        }

        var dailyAmount = dosage.Amount * dailyFrequency;

        var totals = _memberTotals[familyMemberId];
        totals[n.GenericName] = totals.GetValueOrDefault(n.GenericName) + dailyAmount;

        if (dailyAmount == 0m) return;

        if (!_memberContributions.TryGetValue(familyMemberId, out var byNutrient))
        {
            byNutrient = [];
            _memberContributions[familyMemberId] = byNutrient;
        }

        if (!byNutrient.TryGetValue(n.GenericName, out var bySupplement))
        {
            bySupplement = [];
            byNutrient[n.GenericName] = bySupplement;
        }

        if (bySupplement.TryGetValue(supplementId, out var bucket))
        {
            bucket.Amount += dailyAmount;
            bucket.Multiplier = bucket.Multiplier == dailyFrequency ? dailyFrequency : null;
            bucket.NutrientIds.Add(n.Id);
        }
        else
        {
            bySupplement[supplementId] = new ContributionBucket
            {
                Amount = dailyAmount,
                Multiplier = dailyFrequency,
                NutrientIds = [n.Id]
            };
        }
    }
}

// Mutable aggregation holder for one (member, nutrient, supplement) cell:
// summed daily amount, merged dose multiplier, and the SupplementNutrient
// row ids that produced the amount (single id → amount links to its editor).
internal sealed class ContributionBucket
{
    public decimal Amount { get; set; }
    public decimal? Multiplier { get; set; }
    public HashSet<int> NutrientIds { get; init; } = [];
}
