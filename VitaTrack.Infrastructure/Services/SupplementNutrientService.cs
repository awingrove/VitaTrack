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

        return await PersistHierarchyAsync(supplementId, NormalizeFlatToHierarchy(nutrients));
    }

    private static IEnumerable<SupplementNutrientDto> NormalizeFlatToHierarchy(
        IEnumerable<SupplementNutrientDto> flat)
    {
        var ordered = flat.ToList();
        var roots = new List<SupplementNutrientDto>();

        for (var i = 0; i < ordered.Count; i++)
        {
            var dto = ordered[i];
            if (dto.ParentNutrientId.HasValue)
            {
                var parentIndex = dto.ParentNutrientId.Value;
                if (parentIndex >= 0 && parentIndex < ordered.Count)
                {
                    var parent = ordered[parentIndex];
                    parent.Children ??= new List<SupplementNutrientDto>();
                    if (dto.Children is not null)
                    {
                        parent.Children.AddRange(dto.Children);
                    }
                    parent.Children.Add(dto);
                    continue;
                }
            }

            roots.Add(dto);
        }

        return roots;
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

            int parentId;
            try
            {
                parentId = await _nutrientRepo.AddAsync(new SupplementNutrient
                {
                    SupplementId = supplementId,
                    GenericName = root.GenericName,
                    SpecificForm = string.IsNullOrWhiteSpace(root.SpecificForm) ? "Blend" : root.SpecificForm,
                    Dosage = root.Dosage
                });
                var parent = await _nutrientRepo.GetByIdAsync(parentId);
                if (parent is not null)
                {
                    saved.Add(parent);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist top-level nutrient {Name}", root.GenericName);
                failures.Add(new NutrientFailure(root.GenericName, ex.Message));
                continue;
            }

            if (root.Children != null)
            {
                foreach (var c in root.Children.Where(x => !string.IsNullOrWhiteSpace(x.GenericName)))
                {
                    var child = new SupplementNutrient
                    {
                        SupplementId = supplementId,
                        GenericName = c.GenericName,
                        SpecificForm = string.IsNullOrWhiteSpace(c.SpecificForm) ? "N/A" : c.SpecificForm,
                        Dosage = c.Dosage ?? string.Empty,
                        ParentNutrientId = parentId
                    };
                    try
                    {
                        await _nutrientRepo.AddAsync(child);
                        saved.Add(child);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to persist child nutrient {Name}", c.GenericName);
                        failures.Add(new NutrientFailure(c.GenericName, ex.Message));
                    }
                }
            }
        }

        return new ReplaceNutrientsResult(saved, failures);
    }
}