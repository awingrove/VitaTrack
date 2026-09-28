using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VitaTrack.Core.Features.ServiceConnections;

namespace VitaTrack.Tests.Features.ServiceConnections;

[TestClass]
public class ServiceConnectionRepositoryTests : SqliteTestBase
{
    // The shared test base stays slice-agnostic, so the slice's repository is built here.
    private readonly ServiceConnectionRepository _repository;

    public ServiceConnectionRepositoryTests()
    {
        _repository = new ServiceConnectionRepository(Connection);
    }

    [TestMethod]
    public async Task SaveAsync_ThenGetActive_ReturnsRow()
    {
        var connection = new ServiceConnection
        {
            Service = "opencode",
            BaseUrl = "https://api.example.com",
            ApiKey = "sk-secret",
            Model = "gpt-test",
            Variant = "high",
            MaxTokens = 4096,
            Temperature = 0.7,
            Verification = "verified",
            VerifiedAt = new DateTimeOffset(2026, 9, 26, 12, 0, 0, TimeSpan.FromHours(1)),
            IsActive = true,
            CreatedAt = new DateTimeOffset(2026, 9, 26, 11, 0, 0, TimeSpan.FromHours(1)),
            UpdatedAt = new DateTimeOffset(2026, 9, 26, 12, 0, 0, TimeSpan.FromHours(1))
        };

        var id = await _repository.SaveAsync(connection);

        Assert.IsTrue(id > 0);
        var active = await _repository.GetActiveAsync();
        Assert.IsNotNull(active);
        Assert.AreEqual(id, active!.Id); // null warning is wrong: asserted not null on the line above
        Assert.AreEqual(connection.Service, active.Service);
        Assert.AreEqual(connection.BaseUrl, active.BaseUrl);
        Assert.AreEqual(connection.ApiKey, active.ApiKey);
        Assert.AreEqual(connection.Model, active.Model);
        Assert.AreEqual(connection.Variant, active.Variant);
        Assert.AreEqual(connection.MaxTokens, active.MaxTokens);
        Assert.AreEqual(connection.Temperature, active.Temperature);
        Assert.AreEqual(connection.Verification, active.Verification);
        Assert.AreEqual(connection.VerifiedAt, active.VerifiedAt);
        Assert.IsTrue(active.IsActive);
        Assert.AreEqual(connection.CreatedAt, active.CreatedAt);
        Assert.AreEqual(connection.UpdatedAt, active.UpdatedAt);
        // DateTimeOffset equality compares the instant only, so the stored UTC offset
        // needs its own assertion: these rows went out at +01:00.
        Assert.AreEqual(TimeSpan.FromHours(1), active.VerifiedAt.GetValueOrDefault().Offset);
        Assert.AreEqual(TimeSpan.FromHours(1), active.CreatedAt.Offset);
    }

    [TestMethod]
    public async Task SaveAsync_SecondActiveConnection_DeactivatesTheFirst()
    {
        var firstId = await _repository.SaveAsync(new ServiceConnection
        {
            Service = "opencode",
            BaseUrl = "https://first.example.com",
            ApiKey = "sk-first",
            IsActive = true
        });

        var secondId = await _repository.SaveAsync(new ServiceConnection
        {
            Service = "opencode",
            BaseUrl = "https://second.example.com",
            ApiKey = "sk-second",
            IsActive = true
        });

        Assert.AreNotEqual(firstId, secondId);
        var active = await _repository.GetActiveAsync();
        Assert.IsNotNull(active);
        Assert.AreEqual(secondId, active!.Id); // null warning is wrong: asserted not null on the line above

        var all = await _repository.GetAllAsync();
        var first = AssertRowsContains(all, firstId);
        Assert.IsFalse(first.IsActive);
        var second = AssertRowsContains(all, secondId);
        Assert.IsTrue(second.IsActive);
    }

    [TestMethod]
    public async Task SaveAsync_UpdatesExistingRow_WhenIdIsNonZero()
    {
        var original = new ServiceConnection
        {
            Service = "opencode",
            BaseUrl = "https://api.example.com",
            ApiKey = "sk-original",
            IsActive = true
        };
        var id = await _repository.SaveAsync(original);

        var updated = original with { Id = id, ApiKey = "sk-rotated" };
        var savedId = await _repository.SaveAsync(updated);

        Assert.AreEqual(id, savedId);
        var all = await _repository.GetAllAsync();
        Assert.AreEqual(1, all.Count, "update must not insert a new row");

        var row = AssertRowsContains(all, id);
        Assert.AreEqual("sk-rotated", row.ApiKey);
    }

    [TestMethod]
    public async Task GetActiveAsync_WithNoRows_ReturnsNull()
    {
        var active = await _repository.GetActiveAsync();
        Assert.IsNull(active);
    }

    [TestMethod]
    public async Task DeleteAsync_RemovesTheRow_AndGetActiveThenReturnsNull()
    {
        var id = await _repository.SaveAsync(new ServiceConnection
        {
            Service = "opencode",
            BaseUrl = "https://api.example.com",
            ApiKey = "sk-secret",
            IsActive = true
        });

        var deleted = await _repository.DeleteAsync(id);

        Assert.IsTrue(deleted);
        Assert.IsNull(await _repository.GetActiveAsync());
        Assert.AreEqual(0, (await _repository.GetAllAsync()).Count);
        Assert.IsFalse(await _repository.DeleteAsync(id), "deleting a missing row must report false");
    }

    [TestMethod]
    public async Task SaveAsync_StampsCreatedAtAndUpdatedAt()
    {
        var id = await _repository.SaveAsync(new ServiceConnection
        {
            Service = "opencode",
            BaseUrl = "https://api.example.com",
            ApiKey = "sk-secret",
            IsActive = true
        });

        var stored = AssertRowsContains(await _repository.GetAllAsync(), id);
        Assert.AreNotEqual(default, stored.CreatedAt, "CreatedAt must be stamped on insert");
        Assert.AreNotEqual(default, stored.UpdatedAt, "UpdatedAt must be stamped on insert");
        Assert.AreEqual(stored.CreatedAt, stored.UpdatedAt, "a fresh insert stamps both stamps");
    }

    [TestMethod]
    public async Task SaveAsync_UpdateById_DeactivatesTheOtherActiveRow()
    {
        var firstId = await _repository.SaveAsync(NewConnection("https://first.example.com", "sk-first"));
        var secondId = await _repository.SaveAsync(NewConnection("https://second.example.com", "sk-second"));

        var reactivatedId = await _repository.SaveAsync(
            NewConnection("https://first.example.com", "sk-first-rotated") with { Id = firstId });

        Assert.AreEqual(firstId, reactivatedId);
        var rows = await _repository.GetAllAsync();
        // Asserted on the individual rows: GetActiveAsync's LIMIT 1 would pass either way.
        Assert.IsTrue(AssertRowsContains(rows, firstId).IsActive, "the updated row becomes the active one");
        Assert.IsFalse(AssertRowsContains(rows, secondId).IsActive, "the update must demote the previously active row");
    }

    [TestMethod]
    public async Task SaveAsync_UpdateById_KeepsCreatedAtImmutableAndAdvancesUpdatedAt()
    {
        var stamped = new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);
        var id = await _repository.SaveAsync(NewConnection("https://api.example.com", "sk-original") with
        {
            CreatedAt = stamped,
            UpdatedAt = stamped
        });
        var beforeUpdate = AssertRowsContains(await _repository.GetAllAsync(), id);

        // A caller record that omits CreatedAt and carries a stale UpdatedAt is the
        // loaded-row case: neither stamp may survive an update.
        var rotatedId = await _repository.SaveAsync(NewConnection("https://api.example.com", "sk-rotated") with
        {
            Id = id,
            UpdatedAt = stamped
        });

        Assert.AreEqual(id, rotatedId);
        var updated = AssertRowsContains(await _repository.GetAllAsync(), id);
        Assert.AreEqual("sk-rotated", updated.ApiKey);
        Assert.AreEqual(stamped, beforeUpdate.CreatedAt);
        Assert.AreEqual(stamped, beforeUpdate.UpdatedAt);
        Assert.AreEqual(stamped, updated.CreatedAt, "CreatedAt is immutable once written");
        Assert.IsTrue(updated.UpdatedAt > stamped, "UpdatedAt advances to now on every update, never to the caller's value");
    }

    [TestMethod]
    public async Task SaveAsync_WhenTheWriteFails_RollsBackTheDeactivation()
    {
        var liveId = await _repository.SaveAsync(NewConnection("https://live.example.com", "sk-live"));

        // Service is NOT NULL and the deactivation runs first and unconditionally, so
        // only a transaction keeps the live row active when the insert then fails.
        var rejected = NewConnection("https://broken.example.com", "sk-broken") with
        {
            Service = null! // the null warning is wrong: forcing the NOT NULL violation is the point
        };

        await Assert.ThrowsExceptionAsync<SqliteException>(() => _repository.SaveAsync(rejected));

        var live = await _repository.GetActiveAsync();
        Assert.IsNotNull(live);
        Assert.AreEqual(liveId, live!.Id); // null warning is wrong: asserted not null on the line above
        Assert.AreEqual(1, (await _repository.GetAllAsync()).Count, "the failed insert must leave no row behind");
    }

    [TestMethod]
    public async Task SaveAsync_WithUnknownId_ReportsNoRowWrittenAndKeepsTheActiveRow()
    {
        var liveId = await _repository.SaveAsync(NewConnection("https://live.example.com", "sk-live"));

        var savedId = await _repository.SaveAsync(
            NewConnection("https://stale.example.com", "sk-stale") with { Id = 987_654 });

        Assert.AreEqual(0, savedId, "an update that matched no row must not report the id as saved");
        var live = await _repository.GetActiveAsync();
        Assert.IsNotNull(live);
        Assert.AreEqual(liveId, live!.Id); // null warning is wrong: asserted not null on the line above
        Assert.AreEqual(1, (await _repository.GetAllAsync()).Count, "the stale id must not insert a row");
    }

    [TestMethod]
    public async Task SaveAsync_InactiveRow_DeactivatesTheCurrentActive_LeavingNoneActive()
    {
        var activeId = await _repository.SaveAsync(NewConnection("https://first.example.com", "sk-first"));

        // The deactivation is unconditional and precedes every write, so saving a row with
        // IsActive = false leaves the table with NO active connection. Task 2's handler and
        // Task 5's settings UI must not take this path; it is documented, not accidental.
        var inactiveId = await _repository.SaveAsync(
            NewConnection("https://second.example.com", "sk-second", isActive: false));

        var rows = await _repository.GetAllAsync();
        Assert.IsFalse(AssertRowsContains(rows, activeId).IsActive, "the previous active row is demoted");
        Assert.IsFalse(AssertRowsContains(rows, inactiveId).IsActive);
        Assert.IsNull(await _repository.GetActiveAsync(), "an inactive save leaves nothing active");
    }

    private static ServiceConnection NewConnection(string baseUrl, string apiKey, bool isActive = true) => new()
    {
        Service = "opencode",
        BaseUrl = baseUrl,
        ApiKey = apiKey,
        IsActive = isActive
    };

    private static ServiceConnection AssertRowsContains(IReadOnlyList<ServiceConnection> rows, int id)
    {
        var row = rows.SingleOrDefault(c => c.Id == id);
        Assert.IsNotNull(row, $"row {id} must exist");
        return row!; // null warning is wrong: asserted not null on the line above
    }
}
