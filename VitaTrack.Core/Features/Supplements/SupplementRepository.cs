using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using VitaTrack.Core.Features.Dosing;
using VitaTrack.Core.Features.Nutrients;

namespace VitaTrack.Core.Features.Supplements;

public class SupplementRepository(
    IDbConnection db,
    ISupplementNutrientRepository nutrientRepository,
    IPrescribedDoseRepository prescribedDoseRepository) : ISupplementRepository
{
    private readonly IDbConnection _db = db;
    private readonly ISupplementNutrientRepository _nutrientRepository = nutrientRepository;
    private readonly IPrescribedDoseRepository _prescribedDoseRepository = prescribedDoseRepository;

    public async Task<IReadOnlyList<Supplement>> GetAllAsync()
    {
        const string sql = "SELECT Id, Name, Brand, DailyDose, ManufacturerUrl, NutritionJson, SwapSuggestion, Cost, ServingsPerBottle FROM Supplements ORDER BY Name";
        var rows = await _db.QueryAsync<Supplement>(sql);
        return rows.ToList();
    }

    public async Task<Supplement?> GetByIdAsync(int id)
    {
        const string sql = @"
SELECT Id, Name, Brand, DailyDose, ManufacturerUrl, NutritionJson, SwapSuggestion, Cost, ServingsPerBottle
FROM Supplements WHERE Id = @Id";
        return await _db.QuerySingleOrDefaultAsync<Supplement>(sql, new { Id = id });
    }

    public async Task<int> AddAsync(Supplement supplement)
    {
        const string sql = @"
INSERT INTO Supplements (Name, Brand, DailyDose, ManufacturerUrl, NutritionJson, SwapSuggestion, Cost, ServingsPerBottle)
VALUES (@Name, @Brand, @DailyDose, @ManufacturerUrl, @NutritionJson, @SwapSuggestion, @Cost, @ServingsPerBottle);
SELECT last_insert_rowid();";
        return await _db.ExecuteScalarAsync<int>(sql, supplement);
    }

    public async Task UpdateAsync(Supplement supplement)
    {
        const string sql = @"
UPDATE Supplements
SET Name = @Name, Brand = @Brand, DailyDose = @DailyDose,
    ManufacturerUrl = @ManufacturerUrl,
    NutritionJson = @NutritionJson,
    SwapSuggestion = @SwapSuggestion,
    Cost = @Cost,
    ServingsPerBottle = @ServingsPerBottle
WHERE Id = @Id";
        await _db.ExecuteAsync(sql, supplement);
    }

    public async Task<int> DeleteAsync(int id)
    {
        await _nutrientRepository.DeleteBySupplementIdsAsync([id]);
        await _prescribedDoseRepository.DeleteBySupplementIdsAsync([id]);
        const string sql = "DELETE FROM Supplements WHERE Id = @Id";
        return await _db.ExecuteAsync(sql, new { Id = id });
    }

    public async Task<int> DeleteAsync(IEnumerable<int> ids)
    {
        var idList = ids.ToList();
        if (idList.Count == 0) return 0;
        await _nutrientRepository.DeleteBySupplementIdsAsync(idList);
        await _prescribedDoseRepository.DeleteBySupplementIdsAsync(idList);
        const string sql = "DELETE FROM Supplements WHERE Id IN @Ids";
        return await _db.ExecuteAsync(sql, new { Ids = idList });
    }
}