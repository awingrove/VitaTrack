using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using VitaTrack.Core.Features.ServiceConnections;
using VitaTrack.Tests.TestDoubles;

namespace VitaTrack.Tests.Features.ServiceConnections;

/// <summary>
/// The probe's two writes to the connection row, and the one request it must not make
/// a claim about. It runs against the real repository over in-memory SQLite, because
/// "a failed probe still leaves a usable connection" is a claim about the stored row:
/// a mock could only re-assert whatever the mock was told to write.
/// <para>
/// The catalog client is stubbed except in the cancellation test, which needs the real
/// one — it is the component Task 4 established propagates a caller's cancellation, and
/// this handler persists a verification state, so a swallowed abort would stamp a probe
/// that never ran.
/// </para>
/// </summary>
[TestClass]
public class ProbeConnectionHandlerTests : SqliteTestBase
{
    private const string BaseUrl = "https://api.example.com";
    private const string ApiKey = "sk-probe-abcd1234";

    private readonly ServiceConnectionRepository _repository;
    private readonly SaveConnectionHandler _save;

    public ProbeConnectionHandlerTests()
    {
        _repository = new ServiceConnectionRepository(Connection);
        _save = new SaveConnectionHandler(_repository);
    }

    [TestMethod]
    public async Task ProbeAsync_VerifiedProbe_StampsVerifiedAndVerifiedAt()
    {
        await SaveAsync();
        var verifiedAtOrLater = DateTimeOffset.UtcNow;
        var handler = Handler(Catalog("gpt-4o", "gpt-4o-mini"));

        var result = await handler.ProbeAsync();

        Assert.IsTrue(result.Succeeded, "a probe that answered is not a failed write");
        Assert.IsTrue(result.Catalog.Verified);
        CollectionAssert.AreEqual(new[] { "gpt-4o", "gpt-4o-mini" }, result.Catalog.Models.ToList(),
            "the catalog the service listed is what the picker is populated from");

        var row = await ActiveAsync();
        Assert.AreEqual(ServiceConnection.Verified, row.Verification);
        Assert.IsNotNull(row.VerifiedAt, "a verified connection carries the moment it was verified");
        Assert.IsTrue(row.VerifiedAt >= verifiedAtOrLater,
            $"the stamp is when this probe ran, not the epoch or the row's creation time ({row.VerifiedAt})");
    }

    /// <summary>The Global Constraints bullet: the key is always saved, whatever the
    /// probe returns; only <c>Verification</c> changes. A 404 is not a bad key — several
    /// OpenAI-compatible endpoints do not implement <c>/v1/models</c> at all — so this
    /// path must leave a connection the user can still enrich with.</summary>
    [TestMethod]
    public async Task ProbeAsync_FailedProbe_StampsUnverified_AndLeavesTheConnectionSaved()
    {
        await SaveAsync();
        var handler = Handler(ModelCatalog.Unverified);

        var result = await handler.ProbeAsync();

        // "Succeeded" here means the stamp was written, not that the service answered:
        // an unverified answer is a state the UI reports, never an error to report back.
        Assert.IsTrue(result.Succeeded);
        Assert.IsFalse(result.Catalog.Verified);
        Assert.AreEqual(0, result.Catalog.Models.Count);

        var row = await ActiveAsync();
        Assert.IsNotNull(row, "a failed probe must not remove or deactivate the connection");
        Assert.AreEqual(ApiKey, row.ApiKey, "the key is saved whatever the probe returned");
        Assert.AreEqual(BaseUrl, row.BaseUrl);
        Assert.IsTrue(row.IsActive, "the connection stays usable: enrichment reads the active row");
        Assert.AreEqual(ServiceConnection.Unverified, row.Verification);
        Assert.IsNull(row.VerifiedAt, "nothing verified this connection, so it has no verification time");
    }

    /// <summary>Re-probing a connection that had verified must be able to take the stamp
    /// back off. Without this the flag would be write-once and a rotated key would keep
    /// claiming a verification the new key never earned.</summary>
    [TestMethod]
    public async Task ProbeAsync_AfterAVerifiedProbe_AFailedReprobeRemovesTheStamp()
    {
        await SaveAsync();
        var handler = Handler(Catalog("gpt-4o"));
        await handler.ProbeAsync();
        Assert.AreEqual(ServiceConnection.Verified, (await ActiveAsync()).Verification);

        var reprobe = Handler(ModelCatalog.Unverified);
        await reprobe.ProbeAsync();

        var row = await ActiveAsync();
        Assert.AreEqual(ServiceConnection.Unverified, row.Verification);
        Assert.IsNull(row.VerifiedAt, "a stale VerifiedAt beside an unverified flag is a lie the pair cannot express");
    }

    /// <summary>The row the probe wrote to is the one the picker reads: the active
    /// connection, carrying the credential the user just saved. Asserted on the argument
    /// rather than on the result, because "unverified" is also what a skipped probe
    /// returns and only the argument tells the two apart.</summary>
    [TestMethod]
    public async Task ProbeAsync_ProbesTheSavedConnection_NotAnEmptyOne()
    {
        await SaveAsync();
        var catalog = new Mock<IServiceCatalogClient>();
        catalog.Setup(c => c.ListModelsAsync(It.IsAny<ServiceConnection>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Catalog("gpt-4o"));

        await new ProbeConnectionHandler(_repository, catalog.Object).ProbeAsync();

        catalog.Verify(c => c.ListModelsAsync(
            It.Is<ServiceConnection>(conn => conn.BaseUrl == BaseUrl && conn.ApiKey == ApiKey),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>Task 4's carry-forward, seen from the consumer that depends on it: a
    /// probe the caller abandoned is not a result about the connection, so it must not
    /// stamp one. The real catalog client and a handler that only unblocks on
    /// cancellation are what make this a real in-flight abort rather than a stub that
    /// ignores the token.</summary>
    [TestMethod]
    public async Task ProbeAsync_WhenTheCallerAbandonsTheProbe_StampsNothing()
    {
        await SaveAsync();
        using var cts = new CancellationTokenSource();
        var handler = new ProbeConnectionHandler(_repository, new ServiceCatalogClient(
            new SequencedHttpClientFactory(new CancellationObservingHandler()),
            new RecordingLogger<ServiceCatalogClient>()));

        var probe = handler.ProbeAsync(cts.Token);
        await cts.CancelAsync();

        await Assert.ThrowsExceptionAsync<TaskCanceledException>(async () => await probe);
        var row = await ActiveAsync();
        Assert.AreEqual(ServiceConnection.Unverified, row.Verification,
            "an abandoned probe is not a probe result, so it must not be recorded as one");
        Assert.IsNull(row.VerifiedAt);
    }

    [TestMethod]
    public async Task ProbeAsync_WithNoActiveConnection_ReportsItAndWritesNothing()
    {
        var handler = Handler(Catalog("gpt-4o"));

        var result = await handler.ProbeAsync();

        Assert.IsFalse(result.Succeeded, "there was no connection to probe");
        Assert.AreEqual(ProbeConnectionHandler.NoConnection, result.Error);
        Assert.AreEqual(0, (await _repository.GetAllAsync()).Count, "a probe with nothing to probe must not write a row");
    }

    private static ModelCatalog Catalog(params string[] models) => new(models, true);

    private ProbeConnectionHandler Handler(ModelCatalog catalog)
    {
        var client = new Mock<IServiceCatalogClient>();
        client.Setup(c => c.ListModelsAsync(It.IsAny<ServiceConnection>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(catalog);
        return new ProbeConnectionHandler(_repository, client.Object);
    }

    /// <summary>A saved, active, unverified connection — the state a connect leaves
    /// behind before any probe has run, so every test here starts from the same place.</summary>
    private async Task<int> SaveAsync()
    {
        var result = await _save.HandleAsync(new ConnectServiceRequest
        {
            BaseUrl = BaseUrl,
            ApiKey = ApiKey,
        });
        Assert.IsTrue(result.Succeeded, result.Error);
        Assert.IsNotNull(result.Id, "the save this test builds on reported no id");
        return result.Id.Value;
    }

    private async Task<ServiceConnection> ActiveAsync()
    {
        var row = await _repository.GetActiveAsync();
        Assert.IsNotNull(row, "the probe must leave a connection to read the stamp from");
        return row;
    }
}
