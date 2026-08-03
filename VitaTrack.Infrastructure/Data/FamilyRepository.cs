using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using VitaTrack.Infrastructure.Models;

namespace VitaTrack.Infrastructure.Data;

public class FamilyRepository(IDbConnection db) : IFamilyRepository
{
    private readonly IDbConnection _db = db;

    public async Task<IReadOnlyList<FamilyMember>> GetAllAsync()
    {
        const string sql = "SELECT Id, Name, DisplayName, AvatarUrl FROM FamilyMembers";
        var rows = await _db.QueryAsync<FamilyMember>(sql);
        return rows.ToList();
    }

    public async Task<FamilyMember?> GetByIdAsync(int id)
    {
        const string sql = "SELECT Id, Name, DisplayName, AvatarUrl FROM FamilyMembers WHERE Id = @Id";
        return await _db.QuerySingleOrDefaultAsync<FamilyMember>(sql, new { Id = id });
    }

    public async Task<int> AddAsync(FamilyMember member)
    {
        const string sql = @"
INSERT INTO FamilyMembers (Name, DisplayName, AvatarUrl)
VALUES (@Name, @DisplayName, @AvatarUrl);
SELECT last_insert_rowid();";
        return await _db.ExecuteScalarAsync<int>(sql, member);
    }

    public async Task UpdateAsync(FamilyMember member)
    {
        const string sql = @"
UPDATE FamilyMembers
SET Name = @Name, DisplayName = @DisplayName, AvatarUrl = @AvatarUrl
WHERE Id = @Id";
        await _db.ExecuteAsync(sql, member);
    }

    public async Task<int> DeleteAsync(int id)
    {
        await _db.ExecuteAsync("DELETE FROM PrescribedDoses WHERE FamilyMemberId = @Id", new { Id = id });
        const string sql = "DELETE FROM FamilyMembers WHERE Id = @Id";
        return await _db.ExecuteAsync(sql, new { Id = id });
    }
}