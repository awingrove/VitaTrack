using System.Data;
using Dapper;

namespace VitaTrack.ArchitectureTests.Fakes;

/// <summary>
/// A deliberate layering violation. It exists solely so the Core layering bans can be
/// observed going red: a guardrail that has never failed is indistinguishable from one that
/// cannot fail. Never reference this type from anything except the negative-path
/// architecture test that asserts the bans reject it.
/// </summary>
internal sealed class SneakyDapperService
{
    public IEnumerable<int> ReadRows(IDbConnection connection) => SqlMapper.Query<int>(connection, "SELECT 1");
}
