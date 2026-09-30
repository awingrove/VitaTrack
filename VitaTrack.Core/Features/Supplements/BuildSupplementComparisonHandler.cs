using VitaTrack.Core.Features.Nutrients;

namespace VitaTrack.Core.Features.Supplements;

/// <summary>
/// Resolves ordered supplement ids for the comparison page: unknown ids drop
/// silently, requested order survives, and fewer than two survivors returns
/// <c>null</c> so the controller can redirect to the list. The 5-selection cap
/// is client-side only; this handler applies no cap of its own.
/// </summary>
public sealed class BuildSupplementComparisonHandler(
    ISupplementRepository supplementRepo,
    ISupplementNutrientRepository nutrientRepo)
{
    private readonly ISupplementRepository _supplementRepo = supplementRepo;
    private readonly ISupplementNutrientRepository _nutrientRepo = nutrientRepo;

    /// <summary>Returns null when fewer than two of orderedIds resolve to a supplement.</summary>
    public async Task<ComparisonGrid?> BuildAsync(IReadOnlyList<int> orderedIds)
    {
        var resolved = new List<Supplement>(orderedIds.Count);
        foreach (var id in orderedIds)
        {
            var supplement = await _supplementRepo.GetByIdAsync(id);
            if (supplement != null) resolved.Add(supplement);
        }
        if (resolved.Count < 2) return null;

        var nutrients = await _nutrientRepo.GetBySupplementIdsAsync(resolved.Select(s => s.Id));
        var nutrientsBySupplementId = nutrients
            .GroupBy(n => n.SupplementId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<SupplementNutrient>)g.ToList());
        return SupplementComparisonBuilder.Build(resolved, nutrientsBySupplementId);
    }
}
