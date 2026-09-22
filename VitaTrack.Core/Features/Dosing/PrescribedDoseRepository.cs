using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Dapper;

namespace VitaTrack.Core.Features.Dosing;

public class PrescribedDoseRepository(IDbConnection db) : IPrescribedDoseRepository
{
    private readonly IDbConnection _db = db;

    public async Task<IReadOnlyList<PrescribedDose>> GetAllAsync()
    {
        const string sql = @"
                SELECT pd.Id, pd.FamilyMemberId, pd.SupplementId, pd.StartDate, pd.EndDate,
                       pd.Multiplier, pd.Instructions,
                       fm.DisplayName as FamilyMemberName,
                       s.Name as SupplementName,
                       s.Brand as SupplementBrand
                FROM PrescribedDoses pd
                LEFT JOIN FamilyMembers fm ON pd.FamilyMemberId = fm.Id
                LEFT JOIN Supplements s ON pd.SupplementId = s.Id";
        return (await _db.QueryAsync<PrescribedDose>(sql)).ToList();
    }

    public async Task<IReadOnlyList<PrescribedDose>> GetByFamilyMemberIdAsync(int familyMemberId)
    {
        const string sql = @"
                SELECT pd.Id, pd.FamilyMemberId, pd.SupplementId, pd.StartDate, pd.EndDate,
                       pd.Multiplier, pd.Instructions,
                       fm.DisplayName as FamilyMemberName,
                       s.Name as SupplementName,
                       s.Brand as SupplementBrand
                FROM PrescribedDoses pd
                LEFT JOIN FamilyMembers fm ON pd.FamilyMemberId = fm.Id
                LEFT JOIN Supplements s ON pd.SupplementId = s.Id
                WHERE pd.FamilyMemberId = @FamilyMemberId";
        return (await _db.QueryAsync<PrescribedDose>(sql, new { FamilyMemberId = familyMemberId })).ToList();
    }

    public async Task<PrescribedDose?> GetByIdAsync(int id)
    {
        const string sql = @"
                SELECT pd.Id, pd.FamilyMemberId, pd.SupplementId, pd.StartDate, pd.EndDate,
                       pd.Multiplier, pd.Instructions,
                       fm.DisplayName as FamilyMemberName,
                       s.Name as SupplementName,
                       s.Brand as SupplementBrand
                FROM PrescribedDoses pd
                LEFT JOIN FamilyMembers fm ON pd.FamilyMemberId = fm.Id
                LEFT JOIN Supplements s ON pd.SupplementId = s.Id
                WHERE pd.Id = @Id";
        return await _db.QuerySingleOrDefaultAsync<PrescribedDose>(sql, new { Id = id });
    }

    public async Task<int> AddAsync(PrescribedDose prescribedDose)
    {
        const string sql = @"
                INSERT INTO PrescribedDoses (FamilyMemberId, SupplementId, StartDate, EndDate, Multiplier, Instructions)
                VALUES (@FamilyMemberId, @SupplementId, @StartDate, @EndDate, @Multiplier, @Instructions);
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
                    Multiplier = @Multiplier,
                    Instructions = @Instructions
                WHERE Id = @Id";
        await _db.ExecuteAsync(sql, prescribedDose);
    }

    public async Task<int> DeleteAsync(int id)
    {
        const string sql = "DELETE FROM PrescribedDoses WHERE Id = @Id";
        return await _db.ExecuteAsync(sql, new { Id = id });
    }

    public async Task DeleteBySupplementIdsAsync(IEnumerable<int> supplementIds)
    {
        var idList = supplementIds.ToList();
        if (idList.Count == 0) return;
        await _db.ExecuteAsync("DELETE FROM PrescribedDoses WHERE SupplementId IN @Ids", new { Ids = idList });
    }
}
