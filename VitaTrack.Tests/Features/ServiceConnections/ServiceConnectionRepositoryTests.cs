using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VitaTrack.Core.Features.ServiceConnections;

namespace VitaTrack.Tests.Features.ServiceConnections;

[TestClass]
public class ServiceConnectionRepositoryTests : SqliteTestBase
{

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

        var id = await Repository.SaveAsync(connection);

        Assert.IsTrue(id > 0);
        var active = await Repository.GetActiveAsync();
        Assert.IsNotNull(active);
        Assert.AreEqual(id, active!.Id);
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
    }

    [TestMethod]
    public async Task SaveAsync_SecondActiveConnection_DeactivatesTheFirst()
    {
        var firstId = await Repository.SaveAsync(new ServiceConnection
        {
            Service = "opencode",
            BaseUrl = "https://first.example.com",
            ApiKey = "sk-first",
            IsActive = true
        });

        var secondId = await Repository.SaveAsync(new ServiceConnection
        {
            Service = "opencode",
            BaseUrl = "https://second.example.com",
            ApiKey = "sk-second",
            IsActive = true
        });

        Assert.AreNotEqual(firstId, secondId);
        var active = await Repository.GetActiveAsync();
        Assert.IsNotNull(active);
        Assert.AreEqual(secondId, active!.Id);

        var all = await Repository.GetAllAsync();
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
        var id = await Repository.SaveAsync(original);

        var updated = original with { Id = id, ApiKey = "sk-rotated" };
        var savedId = await Repository.SaveAsync(updated);

        Assert.AreEqual(id, savedId);
        var all = await Repository.GetAllAsync();
        Assert.AreEqual(1, all.Count, "update must not insert a new row");

        var row = AssertRowsContains(all, id);
        Assert.AreEqual("sk-rotated", row.ApiKey);
    }

    [TestMethod]
    public async Task GetActiveAsync_WithNoRows_ReturnsNull()
    {
        var active = await Repository.GetActiveAsync();
        Assert.IsNull(active);
    }

    [TestMethod]
    public async Task DeleteAsync_RemovesTheRow_AndGetActiveThenReturnsNull()
    {
        var id = await Repository.SaveAsync(new ServiceConnection
        {
            Service = "opencode",
            BaseUrl = "https://api.example.com",
            ApiKey = "sk-secret",
            IsActive = true
        });

        var deleted = await Repository.DeleteAsync(id);

        Assert.IsTrue(deleted);
        Assert.IsNull(await Repository.GetActiveAsync());
        Assert.AreEqual(0, (await Repository.GetAllAsync()).Count);
        Assert.IsFalse(await Repository.DeleteAsync(id), "deleting a missing row must report false");
    }

    [TestMethod]
    public async Task SaveAsync_StampsCreatedAtAndUpdatedAt()
    {
        var id = await Repository.SaveAsync(new ServiceConnection
        {
            Service = "opencode",
            BaseUrl = "https://api.example.com",
            ApiKey = "sk-secret",
            IsActive = true
        });

        var stored = AssertRowsContains(await Repository.GetAllAsync(), id);
        Assert.AreNotEqual(default, stored.CreatedAt, "CreatedAt must be stamped on insert");
        Assert.AreNotEqual(default, stored.UpdatedAt, "UpdatedAt must be stamped on insert");
        Assert.AreEqual(stored.CreatedAt, stored.UpdatedAt, "a fresh insert stamps both stamps");
    }

    private static ServiceConnection AssertRowsContains(IReadOnlyList<ServiceConnection> rows, int id)
    {
        var row = rows.SingleOrDefault(c => c.Id == id);
        Assert.IsNotNull(row, $"row {id} must exist");
        return row!;
    }
}
