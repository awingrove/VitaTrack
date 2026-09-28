using System.Data;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Dapper;

namespace VitaTrack.Core.Features.ServiceConnections;

/// <summary>
/// Dapper persistence for <see cref="ServiceConnection"/>. Enforces the
/// at-most-one-active invariant: every save first issues the single
/// <c>UPDATE ... SET IsActive = 0 WHERE IsActive = 1</c> deactivation, then the
/// insert/update, in the same parameterised command batch.
/// </summary>
public class ServiceConnectionRepository(IDbConnection db) : IServiceConnectionRepository
{
    private readonly IDbConnection _db = db;

    public async Task<ServiceConnection?> GetActiveAsync(CancellationToken ct = default)
    {
        const string sql = @"
SELECT Id, Service, BaseUrl, ApiKey, Model, Variant, MaxTokens, Temperature, Verification, VerifiedAt, IsActive, CreatedAt, UpdatedAt
FROM ServiceConnections
WHERE IsActive = 1
LIMIT 1;";
        var row = await _db.QuerySingleOrDefaultAsync<ServiceConnectionRow>(Abortable(sql, ct));
        return row is null ? null : ToConnection(row);
    }

    public async Task<IReadOnlyList<ServiceConnection>> GetAllAsync(CancellationToken ct = default)
    {
        const string sql = @"
SELECT Id, Service, BaseUrl, ApiKey, Model, Variant, MaxTokens, Temperature, Verification, VerifiedAt, IsActive, CreatedAt, UpdatedAt
FROM ServiceConnections
ORDER BY UpdatedAt DESC, Id DESC;";
        var rows = await _db.QueryAsync<ServiceConnectionRow>(Abortable(sql, ct));
        var connections = new List<ServiceConnection>();
        foreach (var row in rows) connections.Add(ToConnection(row));
        return connections;
    }

    public async Task<int> SaveAsync(ServiceConnection connection, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        // Stamps are caller-controlled and defaulted to now: rows saved without
        // stamps get CreatedAt == UpdatedAt == now; rows that carry them are
        // persisted verbatim so a loaded record round-trips unchanged.
        var createdAt = connection.CreatedAt == default ? now : connection.CreatedAt;
        var updatedAt = connection.UpdatedAt == default ? now : connection.UpdatedAt;
        if (connection.Id == 0)
        {
            const string sql = @"
UPDATE ServiceConnections SET IsActive = 0 WHERE IsActive = 1;
INSERT INTO ServiceConnections (Service, BaseUrl, ApiKey, Model, Variant, MaxTokens, Temperature, Verification, VerifiedAt, IsActive, CreatedAt, UpdatedAt)
VALUES (@Service, @BaseUrl, @ApiKey, @Model, @Variant, @MaxTokens, @Temperature, @Verification, @VerifiedAt, @IsActive, @CreatedAt, @UpdatedAt);
SELECT last_insert_rowid();";
            return await _db.ExecuteScalarAsync<int>(new CommandDefinition(sql, SaveParameters(connection, updatedAt, createdAt), cancellationToken: ct));
        }

        const string updateSql = @"
UPDATE ServiceConnections SET IsActive = 0 WHERE IsActive = 1;
UPDATE ServiceConnections
SET Service = @Service, BaseUrl = @BaseUrl, ApiKey = @ApiKey, Model = @Model, Variant = @Variant,
    MaxTokens = @MaxTokens, Temperature = @Temperature, Verification = @Verification, VerifiedAt = @VerifiedAt,
    IsActive = @IsActive, CreatedAt = @CreatedAt, UpdatedAt = @UpdatedAt
WHERE Id = @Id;";
        await _db.ExecuteAsync(new CommandDefinition(updateSql, SaveParameters(connection, updatedAt, createdAt), cancellationToken: ct));
        return connection.Id;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        const string sql = "DELETE FROM ServiceConnections WHERE Id = @Id;";
        var rows = await _db.ExecuteAsync(new CommandDefinition(sql, new { Id = id }, cancellationToken: ct));
        return rows > 0;
    }

    private static CommandDefinition Abortable(string sql, CancellationToken ct) => new(sql, cancellationToken: ct);

    private static object SaveParameters(ServiceConnection connection, DateTimeOffset updatedAt, DateTimeOffset createdAt) => new
    {
        connection.Id,
        connection.Service,
        connection.BaseUrl,
        connection.ApiKey,
        connection.Model,
        connection.Variant,
        connection.MaxTokens,
        connection.Temperature,
        connection.Verification,
        VerifiedAt = connection.VerifiedAt?.ToString("o", CultureInfo.InvariantCulture),
        connection.IsActive,
        CreatedAt = createdAt.ToString("o", CultureInfo.InvariantCulture),
        UpdatedAt = updatedAt.ToString("o", CultureInfo.InvariantCulture)
    };

    private static ServiceConnection ToConnection(ServiceConnectionRow row) => new()
    {
        Id = (int)row.Id,
        Service = row.Service,
        BaseUrl = row.BaseUrl,
        ApiKey = row.ApiKey,
        Model = row.Model,
        Variant = row.Variant,
        MaxTokens = (int)row.MaxTokens,
        Temperature = row.Temperature,
        Verification = row.Verification,
        VerifiedAt = ParseDate(row.VerifiedAt),
        IsActive = row.IsActive != 0,
        CreatedAt = ParseRequiredDate(row.Id, row.CreatedAt, nameof(ServiceConnection.CreatedAt)),
        UpdatedAt = ParseRequiredDate(row.Id, row.UpdatedAt, nameof(ServiceConnection.UpdatedAt))
    };

    private static DateTimeOffset? ParseDate(string? value) =>
        string.IsNullOrEmpty(value)
            ? null
            : DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    /// <summary>NOT NULL date columns are always stamped at write time here; a parse
    /// failure means a row written by hand, and a loud error beats a silently wrong date.</summary>
    private static DateTimeOffset ParseRequiredDate(long id, string value, string column) =>
        ParseDate(value)
        ?? throw new InvalidOperationException($"ServiceConnections row {id} has a {column} that is not a round-trip ISO-8601 value.");

    /// <summary>Raw row shape: dates are TEXT, IsActive and MaxTokens are INTEGER,
    /// Temperature is REAL. The rank projection lets SQLite compute recency.</summary>
    private sealed record ServiceConnectionRow(
        long Id,
        string Service,
        string BaseUrl,
        string ApiKey,
        string? Model,
        string? Variant,
        long MaxTokens,
        double Temperature,
        string Verification,
        string? VerifiedAt,
        long IsActive,
        string CreatedAt,
        string UpdatedAt);
}
