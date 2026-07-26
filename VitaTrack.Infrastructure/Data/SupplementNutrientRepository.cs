using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using VitaTrack.Infrastructure.Models;

namespace VitaTrack.Infrastructure.Data
{
    public class SupplementNutrientRepository : ISupplementNutrientRepository
    {
        private readonly IDbConnection _db;
        public SupplementNutrientRepository(IDbConnection db) => _db = db;

        public async Task<IReadOnlyList<SupplementNutrient>> GetBySupplementIdAsync(int supplementId)
        {
            const string sql = @"
                SELECT Id, SupplementId, GenericName, SpecificForm, Dosage
                FROM SupplementNutrients
                WHERE SupplementId = @SupplementId";
            var rows = await _db.QueryAsync<SupplementNutrient>(sql, new { SupplementId = supplementId });
            return rows.ToList();
        }

        public async Task<SupplementNutrient?> GetByIdAsync(int id)
        {
            const string sql = @"
                SELECT Id, SupplementId, GenericName, SpecificForm, Dosage
                FROM SupplementNutrients WHERE Id = @Id";
            return await _db.QuerySingleOrDefaultAsync<SupplementNutrient>(sql, new { Id = id });
        }

        public async Task<int> AddAsync(SupplementNutrient nutrient)
        {
            const string sql = @"
                INSERT INTO SupplementNutrients (SupplementId, GenericName, SpecificForm, Dosage)
                VALUES (@SupplementId, @GenericName, @SpecificForm, @Dosage);
                SELECT last_insert_rowid();";
            return await _db.ExecuteScalarAsync<int>(sql, nutrient);
        }

        public async Task UpdateAsync(SupplementNutrient nutrient)
        {
            const string sql = @"
                UPDATE SupplementNutrients
                SET GenericName = @GenericName,
                    SpecificForm = @SpecificForm,
                    Dosage = @Dosage
                WHERE Id = @Id";
            await _db.ExecuteAsync(sql, nutrient);
        }

        public async Task<int> DeleteAsync(int id)
        {
            const string sql = "DELETE FROM SupplementNutrients WHERE Id = @Id";
            return await _db.ExecuteAsync(sql, new { Id = id });
        }
    }
}