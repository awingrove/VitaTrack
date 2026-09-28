using System.Threading;
using System.Threading.Tasks;

namespace VitaTrack.Core.Features.ServiceConnections;

/// <summary>
/// The interaction seam for <see cref="ServiceConnectionRepository"/> and its tests:
/// callers go through the interface, tests fake the base.
/// </summary>
public interface IServiceConnectionRepository
{
    Task<ServiceConnection?> GetActiveAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ServiceConnection>> GetAllAsync(CancellationToken ct = default);
    /// <summary>
    /// Inserts when <c>Id == 0</c> (CreatedAt/UpdatedAt default to now, else the caller's
    /// values are kept), updates otherwise (CreatedAt is immutable, UpdatedAt becomes now).
    /// Returns the row's Id, or <c>0</c> when the update matched no row — the deactivation
    /// is rolled back in that case, so the active connection survives.
    /// </summary>
    Task<int> SaveAsync(ServiceConnection connection, CancellationToken ct = default);
    /// <summary>True when a row was deleted, false when the id was unknown.</summary>
    Task<bool> DeleteAsync(int id, CancellationToken ct = default);
}
