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
/// insert/update, in one transaction — so a save that fails cannot demote the
/// connection that is live right now.
/// </summary>
public class ServiceConnectionRepository(IDbConnection db) : IServiceConnectionRepository
{
    /// <summary>Round-trip ("o") ISO-8601, written and read back unchanged.</summary>
    private const string RoundTripFormat = "o";

    private readonly IDbConnection _db = db;

    public async Task<ServiceConnection?> GetActiveAsync(CancellationToken ct = default)
    {
        const string sql = @"
SELECT Id, Service, BaseUrl, ApiKey, Model, Variant, MaxTokens, Temperature, Verification, VerifiedAt, IsActive, CreatedAt, UpdatedAt
FROM ServiceConnections
WHERE IsActive = 1
LIMIT 1;";
        var row = await _db.QuerySingleOrDefaultAsync<ServiceConnectionRow>(WithCancellation(sql, ct));
        return row is null ? null : ToConnection(row);
    }

    public async Task<IReadOnlyList<ServiceConnection>> GetAllAsync(CancellationToken ct = default)
    {
        const string sql = @"
SELECT Id, Service, BaseUrl, ApiKey, Model, Variant, MaxTokens, Temperature, Verification, VerifiedAt, IsActive, CreatedAt, UpdatedAt
FROM ServiceConnections
ORDER BY UpdatedAt DESC, Id DESC;";
        var rows = await _db.QueryAsync<ServiceConnectionRow>(WithCancellation(sql, ct));
        var connections = new List<ServiceConnection>();
        foreach (var row in rows) connections.Add(ToConnection(row));
        return connections;
    }

    public Task<int> SaveAsync(ServiceConnection connection, CancellationToken ct = default) =>
        WriteAsync(
            transaction => connection.Id == 0
                ? InsertAsync(connection, transaction, ct)
                : UpdateAsync(connection, transaction, ct),
            ct);

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        const string sql = "DELETE FROM ServiceConnections WHERE Id = @Id;";
        var rows = await _db.ExecuteAsync(WithCancellation(sql, new { Id = id }, ct));
        return rows > 0;
    }

    /// <summary>Runs one write batch in a transaction. The deactivation and the
    /// write either both land or neither does — an unknown id must not leave the
    /// user with no active connection and no save.</summary>
    private async Task<int> WriteAsync(Func<IDbTransaction, Task<int>> write, CancellationToken ct)
    {
        var wasClosed = _db.State == ConnectionState.Closed;
        if (wasClosed) _db.Open();

        using var transaction = _db.BeginTransaction();
        try
        {
            var id = await write(transaction);
            if (id == IServiceConnectionRepository.NoRowWritten)
            {
                transaction.Rollback();
                return IServiceConnectionRepository.NoRowWritten;
            }

            transaction.Commit();
            return id;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
        finally
        {
            if (wasClosed) _db.Close();
        }
    }

    private async Task<int> InsertAsync(ServiceConnection connection, IDbTransaction transaction, CancellationToken ct)
    {
        const string sql = @"
UPDATE ServiceConnections SET IsActive = 0 WHERE IsActive = 1;
INSERT INTO ServiceConnections (Service, BaseUrl, ApiKey, Model, Variant, MaxTokens, Temperature, Verification, VerifiedAt, IsActive, CreatedAt, UpdatedAt)
VALUES (@Service, @BaseUrl, @ApiKey, @Model, @Variant, @MaxTokens, @Temperature, @Verification, @VerifiedAt, @IsActive, @CreatedAt, @UpdatedAt);
SELECT last_insert_rowid();";
        // One clock read for both stamps, so a caller who supplies neither gets
        // CreatedAt == UpdatedAt; a caller who supplies both keeps them verbatim.
        var now = DateTimeOffset.UtcNow;
        var createdAt = connection.CreatedAt == default ? now : connection.CreatedAt;
        var updatedAt = connection.UpdatedAt == default ? now : connection.UpdatedAt;
        var parameters = SaveParameters(connection, createdAt: createdAt, updatedAt: updatedAt);
        return await _db.ExecuteScalarAsync<int>(new CommandDefinition(sql, parameters, transaction, cancellationToken: ct));
    }

    /// <summary>CreatedAt is deliberately absent from the SET clause: it is immutable
    /// once written, so a caller-built update record cannot blank or back-date it.
    /// UpdatedAt is unconditionally now, never the caller's value, so it advances on
    /// every update and <c>GetAllAsync</c>'s recency order means something.</summary>
    private async Task<int> UpdateAsync(ServiceConnection connection, IDbTransaction transaction, CancellationToken ct)
    {
        // SELECT changes() is the trailing statement on purpose: a command's own affected-row
        // count covers the whole batch, so it cannot say whether *this* update matched a row.
        const string sql = @"
UPDATE ServiceConnections SET IsActive = 0 WHERE IsActive = 1;
UPDATE ServiceConnections
SET Service = @Service, BaseUrl = @BaseUrl, ApiKey = @ApiKey, Model = @Model, Variant = @Variant,
    MaxTokens = @MaxTokens, Temperature = @Temperature, Verification = @Verification, VerifiedAt = @VerifiedAt,
    IsActive = @IsActive, UpdatedAt = @UpdatedAt
WHERE Id = @Id;
SELECT changes();";
        var parameters = SaveParameters(connection, createdAt: null, updatedAt: DateTimeOffset.UtcNow);
        var updatedRows = await _db.ExecuteScalarAsync<int>(new CommandDefinition(sql, parameters, transaction, cancellationToken: ct));
        return updatedRows == 0 ? IServiceConnectionRepository.NoRowWritten : connection.Id;
    }

    private static CommandDefinition WithCancellation(string sql, CancellationToken ct) => new(sql, cancellationToken: ct);

    private static CommandDefinition WithCancellation(string sql, object parameters, CancellationToken ct) =>
        new(sql, parameters, cancellationToken: ct);

    /// <summary>Parameters for both write branches. <paramref name="createdAt"/> is null
    /// on update: CreatedAt is immutable, so it is absent from the update SET clause and
    /// Dapper never binds the parameter. The stamps are named at every call site so the
    /// two same-typed values cannot be transposed.</summary>
    private static object SaveParameters(ServiceConnection connection, DateTimeOffset? createdAt, DateTimeOffset updatedAt) => new
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
        VerifiedAt = connection.VerifiedAt?.ToString(RoundTripFormat, CultureInfo.InvariantCulture),
        connection.IsActive,
        CreatedAt = createdAt?.ToString(RoundTripFormat, CultureInfo.InvariantCulture),
        UpdatedAt = updatedAt.ToString(RoundTripFormat, CultureInfo.InvariantCulture)
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

    /// <summary>Round-trip parse, so the stored UTC offset survives. A value that is not
    /// round-trip ISO-8601 reads as null rather than as a shifted instant.</summary>
    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParseExact(value, RoundTripFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : null;

    /// <summary>NOT NULL date columns are always stamped at write time here; a parse
    /// failure means a row written by hand, and a loud error beats a silently wrong date.</summary>
    private static DateTimeOffset ParseRequiredDate(long id, string value, string column) =>
        ParseDate(value)
        ?? throw new InvalidOperationException($"ServiceConnections row {id} has a {column} that is not a round-trip ISO-8601 value.");

    /// <summary>Raw row shape: dates are TEXT, IsActive and MaxTokens are INTEGER,
    /// Temperature is REAL.</summary>
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
