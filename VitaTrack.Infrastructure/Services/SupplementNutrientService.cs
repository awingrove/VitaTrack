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

        return await PersistAsync(supplementId, nutrients);
    }

    public async Task<ReplaceNutrientsResult> AddAsync(
        int supplementId,
        IEnumerable<SupplementNutrientDto> nutrients)
    {
        return await PersistAsync(supplementId, nutrients);
    }

    private async Task<ReplaceNutrientsResult> PersistAsync(
        int supplementId,
        IEnumerable<SupplementNutrientDto> nutrients)
    {
        var saved = new List<SupplementNutrient>();
        var failures = new List<NutrientFailure>();

        foreach (var n in nutrients.Where(x => !string.IsNullOrWhiteSpace(x.GenericName)))
        {
            try
            {
                var entity = new SupplementNutrient
                {
                    SupplementId = supplementId,
                    GenericName = n.GenericName,
                    SpecificForm = n.SpecificForm,
                    Dosage = n.Dosage
                };
                await _nutrientRepo.AddAsync(entity);
                saved.Add(entity);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist nutrient {GenericName} for supplement {SupplementId}", n.GenericName, supplementId);
                failures.Add(new NutrientFailure(n.GenericName, ex.Message));
            }
        }

        return new ReplaceNutrientsResult(saved, failures);
    }
}