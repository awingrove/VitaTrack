using System.Linq;
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

        var saved = AssertRowSaved(await _repository.GetAllAsync(), result);
        Assert.AreEqual(ServiceDescriptorRegistry.OpenCodeServiceId, saved.Service);
        Assert.AreEqual("https://api.example.com", saved.BaseUrl);
        Assert.AreEqual("sk-secret", saved.ApiKey);
        Assert.IsTrue(saved.IsActive, "the connection just saved is the active one");
        await AssertActiveIs(saved.Id);
    }

    /// <summary>The <c>Service</c> column stores a descriptor id, never free text, and
    /// this handler is what writes it. A string the registry does not know would write a
    /// row no probe could ever verify, and <c>ServiceCatalogClient</c> would answer
    /// "unverified" to it forever — so the write is refused here, before it happens.</summary>
    [TestMethod]
    public async Task HandleAsync_RejectsUnknownServiceId()
    {
        var unknown = await _handler.HandleAsync(
            Request("https://api.example.com", "sk-secret", service: "not-a-service"));
        var blank = await _handler.HandleAsync(
            Request("https://api.example.com", "sk-secret", service: string.Empty));

        Assert.IsFalse(unknown.Succeeded, "a service the registry does not ship must not be written");
        Assert.AreEqual(ConnectServiceRequest.UnknownService, unknown.Error);
        Assert.IsFalse(blank.Succeeded, "no service chosen is no service id");
        Assert.AreEqual(ConnectServiceRequest.UnknownService, blank.Error);
        Assert.AreEqual(0, (await _repository.GetAllAsync()).Count,
            "an unrecognised service id must not leave a row behind");
    }

    /// <summary>The id arrives from a <c>&lt;select&gt;</c> and a hand-typed request, so
    /// it is matched case-insensitively — and what is stored is the registry's own
    /// spelling. Storing what the user typed would let "OpenCode" and "opencode" become
    /// two different values in one column that <c>ServiceDescriptorRegistry.Find</c>
    /// happens to tolerate and a plain equality check would not.</summary>
    [TestMethod]
    public async Task HandleAsync_StoresTheRegistrysSpellingOfTheChosenService()
    {
        var result = await _handler.HandleAsync(
            Request("https://api.example.com", "sk-secret", service: "OpenCode"));

        var saved = AssertRowSaved(await _repository.GetAllAsync(), result);
        Assert.AreEqual(ServiceDescriptorRegistry.OpenCodeServiceId, saved.Service,
            "the stored id is the registry's, not the request's casing of it");
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
        var firstRow = AssertRowSaved(rows, first);
        var secondRow = AssertRowSaved(rows, second);
        // Asserted per row: GetActiveAsync's LIMIT 1 would pass either way.
        Assert.IsFalse(firstRow.IsActive, "the previous connection is demoted");
        Assert.IsTrue(secondRow.IsActive);
        await AssertActiveIs(secondRow.Id);
    }

    [TestMethod]
    public async Task HandleAsync_Reconnect_StampsTheNewRowInsteadOfCloningTheOldStamps()
    {
        // The row being replaced is seeded with a stamp years in the past rather than
        // written by a first connect. Two `UtcNow` reads microseconds apart would leave
        // the comparison to clock resolution — fine on this box, a coin flip on a clock
        // with millisecond granularity — while a clone is the failure this has to catch.
        var seeded = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        await _repository.SaveAsync(new ServiceConnection
        {
            Service = ServiceDescriptorRegistry.OpenCodeServiceId,
            BaseUrl = "https://api.example.com",
            ApiKey = "sk-first",
            CreatedAt = seeded,
            UpdatedAt = seeded,
            IsActive = true
        });
        var firstRow = (await _repository.GetAllAsync()).Single(c => c.ApiKey == "sk-first");

        var result = await _handler.HandleAsync(Request("https://api.example.com", "sk-second"));
        var rows = await _repository.GetAllAsync();
        var secondRow = AssertRowSaved(rows, result);

        // `with` copies every property, so without an explicit reset the appended row
        // carried the row it replaces' stamps and the repository stored them verbatim.
        Assert.IsTrue(secondRow.CreatedAt > firstRow.CreatedAt,
            "a new connection must not inherit the creation time of the row it replaces");
        Assert.IsTrue(secondRow.UpdatedAt > firstRow.UpdatedAt,
            "an unchanged UpdatedAt leaves GetAllAsync's recency order with only its id tiebreak");
        Assert.AreEqual(secondRow.Id, rows[0].Id, "the newest row leads the recency order");
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
        var saved = AssertRowSaved(rows, reconnect);
        Assert.AreEqual("gpt-4o-mini", saved.Model, "a blank model field keeps the model already in use");
        Assert.AreEqual("sk-rotated", saved.ApiKey, "the key the form did supply is still written");
    }

    [TestMethod]
    public async Task HandleAsync_Reconnect_WithANewModel_ReplacesIt()
    {
        await _handler.HandleAsync(Request("https://api.example.com", "sk-first", model: "gpt-4o-mini"));

        var reconnect = await _handler.HandleAsync(Request("https://api.example.com", "sk-rotated", model: "gpt-4o"));

        var saved = AssertRowSaved(await _repository.GetAllAsync(), reconnect);
        Assert.AreEqual("gpt-4o", saved.Model, "an explicitly supplied model wins over the carried-forward one");
    }

    [TestMethod]
    public async Task HandleAsync_FirstConnect_LeavesTheModelUnset()
    {
        var result = await _handler.HandleAsync(Request("https://api.example.com", "sk-secret"));

        var saved = AssertRowSaved(await _repository.GetAllAsync(), result);
        Assert.IsNull(saved.Model, "a blank model field on a first connect stores no model, not an empty string");
    }

    [TestMethod]
    public async Task HandleAsync_TrimsWhitespaceFromFreeTextModel()
    {
        var result = await _handler.HandleAsync(
            Request("  https://api.example.com  ", "  sk-secret  ", model: "  gpt-4o-mini  "));

        var saved = AssertRowSaved(await _repository.GetAllAsync(), result);
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
            Service = ServiceDescriptorRegistry.OpenCodeServiceId,
            BaseUrl = "https://api.example.com",
            ApiKey = "sk-first",
            Verification = "verified",
            VerifiedAt = new DateTimeOffset(2026, 9, 26, 12, 0, 0, TimeSpan.Zero),
            IsActive = true
        });

        var result = await _handler.HandleAsync(Request("https://api.example.com", "sk-secret"));

        var saved = AssertRowSaved(await _repository.GetAllAsync(), result);
        Assert.AreEqual(ServiceConnection.Unverified, saved.Verification);
        Assert.IsNull(saved.VerifiedAt, "nothing verified this connection, so it has no verification time");
    }

    [TestMethod]
    public async Task HandleAsync_WhenNoRowIsWritten_ReportsFailureInsteadOfSuccess()
    {
        // The repository's "no row was written" answer — a stale id, rolled back so the
        // live connection survives. A real repository cannot be made to answer that from
        // the handler's own inputs, so the repository is stubbed.
        var repository = new Mock<IServiceConnectionRepository>();
        repository.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceConnection { Id = 987_654, Service = ServiceDescriptorRegistry.OpenCodeServiceId });
        repository.Setup(r => r.SaveAsync(It.IsAny<ServiceConnection>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(IServiceConnectionRepository.NoRowWritten);
        var handler = new SaveConnectionHandler(repository.Object);

        var result = await handler.HandleAsync(Request("https://api.example.com", "sk-secret"));

        Assert.IsFalse(result.Succeeded, "an unwritten row is not a saved connection");
        Assert.IsNull(result.Id, "a failed save has no id to report");
        Assert.IsNotNull(result.Error);
    }

    private static ConnectServiceRequest Request(
        string baseUrl,
        string apiKey,
        string? model = null,
        string? service = ServiceDescriptorRegistry.OpenCodeServiceId) =>
        new() { BaseUrl = baseUrl, ApiKey = apiKey, Model = model, Service = service ?? string.Empty };

    /// <summary>The row the result names. Success and the id are unwrapped here once,
    /// so no call site needs the null-forgiving operator, and a save that reported no
    /// row fails with the handler's own message instead of an unresolvable lookup.</summary>
    private static ServiceConnection AssertRowSaved(IReadOnlyList<ServiceConnection> rows, SaveConnectionResult result)
    {
        Assert.IsTrue(result.Succeeded, result.Error);
        Assert.IsNotNull(result.Id, "a successful save reports the id it wrote");
        var matches = rows.Where(c => c.Id == result.Id).ToList();
        Assert.AreEqual(1, matches.Count, $"row {result.Id} must exist exactly once");
        return matches[0];
    }

    private async Task AssertActiveIs(int expectedId)
    {
        var active = await _repository.GetActiveAsync();
        Assert.AreEqual(expectedId, active?.Id, "the connection just saved is the active one");
    }
}
