using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using VitaTrack.Infrastructure.Models;

namespace VitaTrack.Infrastructure.Data
{
    public class PrescribedDoseRepository : IPrescribedDoseRepository
    {
        private readonly IDbConnection _db;
        public PrescribedDoseRepository(IDbConnection db) => _db = db;

        public async Task<IReadOnlyList<PrescribedDose>> GetAllAsync()
        {
            const string sql = @"
                SELECT pd.Id, pd.FamilyMemberId, pd.SupplementId, pd.StartDate, pd.EndDate, 
                       pd.Dosage, pd.Instructions, pd.FrequencyPerDay,
                       fm.DisplayName as FamilyMemberName,
                       s.Name as SupplementName
                FROM PrescribedDoses pd
                LEFT JOIN FamilyMembers fm ON pd.FamilyMemberId = fm.Id
                LEFT JOIN Supplements s ON pd.SupplementId = s.Id";
            return (await _db.QueryAsync<PrescribedDose>(sql)).ToList();
        }

        public async Task<PrescribedDose?> GetByIdAsync(int id)
        {
            const string sql = @"
                SELECT pd.Id, pd.FamilyMemberId, pd.SupplementId, pd.StartDate, pd.EndDate, 
                       pd.Dosage, pd.Instructions, pd.FrequencyPerDay,
                       fm.DisplayName as FamilyMemberName,
                       s.Name as SupplementName
                FROM PrescribedDoses pd
                LEFT JOIN FamilyMembers fm ON pd.FamilyMemberId = fm.Id
                LEFT JOIN Supplements s ON pd.SupplementId = s.Id
                WHERE pd.Id = @Id";
            return await _db.QuerySingleOrDefaultAsync<PrescribedDose>(sql, new { Id = id });
        }

        public async Task<int> AddAsync(PrescribedDose prescribedDose)
        {
            const string sql = @"
                INSERT INTO PrescribedDoses (FamilyMemberId, SupplementId, StartDate, EndDate, Dosage, Instructions, FrequencyPerDay)
                VALUES (@FamilyMemberId, @SupplementId, @StartDate, @EndDate, @Dosage, @Instructions, @FrequencyPerDay);
                SELECT last_insert_rowid();";
            return await _db.ExecuteScalarAsync<int>(sql, prescribedDose);
        }

        public async Task UpdateAsync(PrescribedDose prescribedDose)
        {
            const string sql = @"
                UPDATE PrescribedDoses
                SET FamilyMemberId = @FamilyMemberId,
                    SupplementId = @SupplementId,
                    StartDate = @StartDate,
                    EndDate = @EndDate,
                    Dosage = @Dosage,
                    Instructions = @Instructions,
                    FrequencyPerDay = @FrequencyPerDay
                WHERE Id = @Id";
            await _db.ExecuteAsync(sql, prescribedDose);
        }

        public async Task<int> DeleteAsync(int id)
        {
            const string sql = "DELETE FROM PrescribedDoses WHERE Id = @Id";
            return await _db.ExecuteAsync(sql, new { Id = id });
        }
    }
}