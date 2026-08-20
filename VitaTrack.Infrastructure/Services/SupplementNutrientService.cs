using Microsoft.Extensions.Logging;
using VitaTrack.Infrastructure.Data;
using VitaTrack.Infrastructure.Models;

namespace VitaTrack.Infrastructure.Services;

public class SupplementNutrientService(
    ISupplementNutrientRepository nutrientRepo,
    ILogger<SupplementNutrientService> logger) : ISupplementNutrientService
{
    private readonly ISupplementNutrientRepository _nutrientRepo = nutrientRepo;
    private readonly ILogger<SupplementNutrientService> _logger = logger;

    public async Task<ReplaceNutrientsResult> ReplaceAsync(
        int supplementId,
        IEnumerable<SupplementNutrientDto> nutrients)
    {
        var existing = await _nutrientRepo.GetBySupplementIdAsync(supplementId);
        foreach (var n in existing)
        {
            await _nutrientRepo.DeleteAsync(n.Id);
        }

        return await PersistHierarchyAsync(supplementId, nutrients);
    }

    public async Task<ReplaceNutrientsResult> AddAsync(
        int supplementId,
        IEnumerable<SupplementNutrientDto> nutrients)
    {
        return await PersistHierarchyAsync(supplementId, nutrients);
    }

    public async Task<ReplaceNutrientsResult> PersistHierarchyAsync(
        int supplementId,
        IEnumerable<SupplementNutrientDto> roots)
    {
        var saved = new List<SupplementNutrient>();
        var failures = new List<NutrientFailure>();

        foreach (var root in roots.Where(r => !string.IsNullOrWhiteSpace(r.GenericName)))
        {
            if (string.IsNullOrWhiteSpace(root.Dosage))
            {
                failures.Add(new NutrientFailure(root.GenericName, "Top-level nutrient requires a dosage"));
                continue;
            }

            var parentId = await _nutrientRepo.AddAsync(new SupplementNutrient
            {
                SupplementId = supplementId,
                GenericName = root.GenericName,
                SpecificForm = root.SpecificForm,
                Dosage = root.Dosage
            });
            saved.Add(await _nutrientRepo.GetByIdAsync(parentId));

            if (root.Children != null)
            {
                foreach (var c in root.Children.Where(x => !string.IsNullOrWhiteSpace(x.GenericName)))
                {
                    var child = new SupplementNutrient
                    {
                        SupplementId = supplementId,
                        GenericName = c.GenericName,
                        SpecificForm = c.SpecificForm,
                        Dosage = c.Dosage ?? string.Empty,
                        ParentNutrientId = parentId
                    };
                    await _nutrientRepo.AddAsync(child);
                    saved.Add(child);
                }
            }
        }

        return new ReplaceNutrientsResult(saved, failures);
    }
}