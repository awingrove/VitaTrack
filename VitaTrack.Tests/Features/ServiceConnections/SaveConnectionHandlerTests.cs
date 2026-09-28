using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using VitaTrack.Core.Features.ServiceConnections;

namespace VitaTrack.Tests.Features.ServiceConnections;

/// <summary>
/// The handler runs against the real repository over in-memory SQLite: "the second
/// connect deactivates the first" is a repository behaviour the handler must neither
/// duplicate nor undo, and a mock could only re-assert whatever the mock did. The one
/// exception is the unwritten-row path, which a real repository cannot produce from
/// the handler's own inputs — the repository is stubbed there.
/// </summary>
[TestClass]
public class SaveConnectionHandlerTests : SqliteTestBase
{
    private readonly ServiceConnectionRepository _repository;
    private readonly SaveConnectionHandler _handler;

    public SaveConnectionHandlerTests()
    {
        _repository = new ServiceConnectionRepository(Connection);
        _handler = new SaveConnectionHandler(_repository);
    }

    [TestMethod]
    public async Task HandleAsync_ValidConnect_SavesAndActivates()
    {
        var result = await _handler.HandleAsync(Request("https://api.example.com", "sk-secret"));

        Assert.IsTrue(result.Succeeded, result.Error);
        Assert.IsNotNull(result.Id);
        var saved = AssertRowsContains(await _repository.GetAllAsync(), result.Id!.Value);
        Assert.AreEqual(SaveConnectionHandler.ServiceName, saved.Service);
        Assert.AreEqual("https://api.example.com", saved.BaseUrl);
        Assert.AreEqual("sk-secret", saved.ApiKey);
        Assert.IsTrue(saved.IsActive, "the connection just saved is the active one");
        Assert.AreEqual(saved.Id, (await _repository.GetActiveAsync())!.Id); // null warning is wrong: the row just saved is active
    }

    [TestMethod]
    public async Task HandleAsync_RejectsBlankBaseUrlOrKey()
    {
        var blankUrl = await _handler.HandleAsync(Request("   ", "sk-secret"));
        var blankKey = await _handler.HandleAsync(Request("https://api.example.com", "  "));

        Assert.IsFalse(blankUrl.Succeeded);
        Assert.AreEqual(ConnectServiceRequest.BaseUrlRequired, blankUrl.Error);
        Assert.IsFalse(blankKey.Succeeded);
        Assert.AreEqual(ConnectServiceRequest.ApiKeyRequired, blankKey.Error);
        Assert.AreEqual(0, (await _repository.GetAllAsync()).Count, "a rejected request must not write a row");
    }

    [TestMethod]
    public async Task HandleAsync_SecondConnect_DeactivatesThePrevious()
    {
        var first = await _handler.HandleAsync(Request("https://first.example.com", "sk-first"));
        var second = await _handler.HandleAsync(Request("https://second.example.com", "sk-second"));

        // The handler does not deactivate anything itself: it writes a new active row
        // and lets the repository demote the old one. This is Review Focus #3 seen
        // through the handler — the invariant holds, whatever does the demoting.
        Assert.AreNotEqual(first.Id, second.Id, "a second connect writes its own row, keeping the previous one on record");
        var rows = await _repository.GetAllAsync();
        // Asserted per row: GetActiveAsync's LIMIT 1 would pass either way.
        Assert.IsFalse(AssertRowsContains(rows, first.Id!.Value).IsActive, "the previous connection is demoted");
        Assert.IsTrue(AssertRowsContains(rows, second.Id!.Value).IsActive);
        Assert.AreEqual(second.Id, (await _repository.GetActiveAsync())!.Id); // null warning is wrong: the row just saved is active
    }

    [TestMethod]
    public async Task HandleAsync_Reconnect_PreservesTheExistingModel()
    {
        var first = await _handler.HandleAsync(Request("https://api.example.com", "sk-first", model: "gpt-4o-mini"));

        // Same form with the model field left blank: the model the user already chose
        // must reach the new row, or reconnecting silently drops their setting.
        var reconnect = await _handler.HandleAsync(Request("https://api.example.com", "sk-rotated"));

        Assert.AreNotEqual(first.Id, reconnect.Id);
        var rows = await _repository.GetAllAsync();
        Assert.AreEqual(2, rows.Count);
        var saved = AssertRowsContains(rows, reconnect.Id!.Value);
        Assert.AreEqual("gpt-4o-mini", saved.Model, "a blank model field keeps the model already in use");
        Assert.AreEqual("sk-rotated", saved.ApiKey, "the key the form did supply is still written");
    }

    [TestMethod]
    public async Task HandleAsync_Reconnect_WithANewModel_ReplacesIt()
    {
        await _handler.HandleAsync(Request("https://api.example.com", "sk-first", model: "gpt-4o-mini"));

        var reconnect = await _handler.HandleAsync(Request("https://api.example.com", "sk-rotated", model: "gpt-4o"));

        var saved = AssertRowsContains(await _repository.GetAllAsync(), reconnect.Id!.Value);
        Assert.AreEqual("gpt-4o", saved.Model, "an explicitly supplied model wins over the carried-forward one");
    }

    [TestMethod]
    public async Task HandleAsync_FirstConnect_LeavesTheModelUnset()
    {
        var result = await _handler.HandleAsync(Request("https://api.example.com", "sk-secret"));

        var saved = AssertRowsContains(await _repository.GetAllAsync(), result.Id!.Value);
        Assert.IsNull(saved.Model, "a blank model field on a first connect stores no model, not an empty string");
    }

    [TestMethod]
    public async Task HandleAsync_TrimsWhitespaceFromFreeTextModel()
    {
        var result = await _handler.HandleAsync(
            Request("  https://api.example.com  ", "  sk-secret  ", model: "  gpt-4o-mini  "));

        var saved = AssertRowsContains(await _repository.GetAllAsync(), result.Id!.Value);
        Assert.AreEqual("gpt-4o-mini", saved.Model);
        Assert.AreEqual("https://api.example.com", saved.BaseUrl);
        Assert.AreEqual("sk-secret", saved.ApiKey);
    }

    [TestMethod]
    public async Task HandleAsync_SetsVerificationToUnverified_BecauseNoProbeExistsYet()
    {
        // A row that claims to be verified, written straight through the repository:
        // nothing in the app can do this yet, so the handler must not inherit it.
        await _repository.SaveAsync(new ServiceConnection
        {
            Service = SaveConnectionHandler.ServiceName,
            BaseUrl = "https://api.example.com",
            ApiKey = "sk-first",
            Verification = "verified",
            VerifiedAt = new DateTimeOffset(2026, 9, 26, 12, 0, 0, TimeSpan.Zero),
            IsActive = true
        });

        var result = await _handler.HandleAsync(Request("https://api.example.com", "sk-secret"));

        var saved = AssertRowsContains(await _repository.GetAllAsync(), result.Id!.Value);
        Assert.AreEqual(ServiceConnection.Unverified, saved.Verification);
        Assert.IsNull(saved.VerifiedAt, "nothing verified this connection, so it has no verification time");
    }

    [TestMethod]
    public async Task HandleAsync_WhenNoRowIsWritten_ReportsFailureInsteadOfSuccess()
    {
        // 0 is the repository's "no row was written" answer — a stale id, rolled back
        // so the live connection survives. A real repository cannot be made to answer
        // that from the handler's own inputs, so the repository is stubbed.
        var repository = new Mock<IServiceConnectionRepository>();
        repository.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceConnection { Id = 987_654, Service = SaveConnectionHandler.ServiceName });
        repository.Setup(r => r.SaveAsync(It.IsAny<ServiceConnection>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        var handler = new SaveConnectionHandler(repository.Object);

        var result = await handler.HandleAsync(Request("https://api.example.com", "sk-secret"));

        Assert.IsFalse(result.Succeeded, "an unwritten row is not a saved connection");
        Assert.IsNull(result.Id, "a failed save has no id to report");
        Assert.IsNotNull(result.Error);
    }

    private static ConnectServiceRequest Request(string baseUrl, string apiKey, string? model = null) =>
        new() { BaseUrl = baseUrl, ApiKey = apiKey, Model = model };

    private static ServiceConnection AssertRowsContains(IReadOnlyList<ServiceConnection> rows, int id)
    {
        var row = rows.SingleOrDefault(c => c.Id == id);
        Assert.IsNotNull(row, $"row {id} must exist");
        return row!; // null warning is wrong: asserted not null on the line above
    }
}
