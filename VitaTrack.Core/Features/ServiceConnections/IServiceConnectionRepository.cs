using System.Data;
using System.Globalization;
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
    /// <summary>Returns the row's Id: the new id when inserting, the existing id when updating.</summary>
    Task<int> SaveAsync(ServiceConnection connection, CancellationToken ct = default);
    /// <summary>True when a row was deleted, false when the id was unknown.</summary>
    Task<bool> DeleteAsync(int id, CancellationToken ct = default);
}
