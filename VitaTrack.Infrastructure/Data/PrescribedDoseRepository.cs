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
                       fm.Name as FamilyMember_Name, fm.DisplayName as FamilyMember_DisplayName,
                       s.Name as Supplement_Name, s.Brand as Supplement_Brand
                FROM PrescribedDoses pd
                LEFT JOIN FamilyMembers fm ON pd.FamilyMemberId = fm.Id
                LEFT JOIN Supplements s ON pd.SupplementId = s.Id";
            var rows = await _db.QueryAsync<PrescribedDose, FamilyMember, Supplement, PrescribedDose>(
                sql,
                (pd, fm, s) => {
                    pd.FamilyMember = fm;
                    pd.Supplement = s;
                    return pd;
                },
                splitOn: "FamilyMember_Name,Supplement_Name");
            return rows.ToList();
        }

        public async Task<PrescribedDose?> GetByIdAsync(int id)
        {
            const string sql = @"
                SELECT pd.Id, pd.FamilyMemberId, pd.SupplementId, pd.StartDate, pd.EndDate, 
                       pd.Dosage, pd.Instructions, pd.FrequencyPerDay,
                       fm.Name as FamilyMember_Name, fm.DisplayName as FamilyMember_DisplayName,
                       s.Name as Supplement_Name, s.Brand as Supplement_Brand
                FROM PrescribedDoses pd
                LEFT JOIN FamilyMembers fm ON pd.FamilyMemberId = fm.Id
                LEFT JOIN Supplements s ON pd.SupplementId = s.Id
                WHERE pd.Id = @Id";
            var result = await _db.QueryAsync<PrescribedDose, FamilyMember, Supplement, PrescribedDose>(
                sql,
                (pd, fm, s) => {
                    pd.FamilyMember = fm;
                    pd.Supplement = s;
                    return pd;
                },
                new { Id = id },
                splitOn: "FamilyMember_Name,Supplement_Name");
            return result.FirstOrDefault();
        }

        public async Task<int> AddAsync(PrescribedDose prescribedDose)
        {
            const string insertSql = @"
                INSERT INTO PrescribedDoses (FamilyMemberId, SupplementId, StartDate, EndDate, Dosage, Instructions, FrequencyPerDay)
                VALUES (@FamilyMemberId, @SupplementId, @StartDate, @EndDate, @Dosage, @Instructions, @FrequencyPerDay);";
            await _db.ExecuteAsync(insertSql, prescribedDose);
            return await _db.ExecuteScalarAsync<int>("SELECT last_insert_rowid();");
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
