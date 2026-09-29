using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using VitaTrack.Core.Features.ServiceConnections;
using VitaTrack.Tests.TestDoubles;

namespace VitaTrack.Tests;

/// <summary>
/// What a client disconnect does to a connect, at the controller seam. The claim under
/// test is the one the handler-level test cannot reach: the token the action is handed is
/// the token that reaches the probe, and a probe abandoned that way writes no
/// verification state.
/// <para>
/// The real <see cref="ServiceCatalogClient"/> over a <see cref="CancellationObservingHandler"/>
/// is what makes this an in-flight abort. The mock catalog every other controller test
/// uses returns a completed task and cannot observe a token at all, so a test written
/// against it would pass with the token threaded nowhere.
/// </para>
/// <para>
/// What this does <em>not</em> cover is MVC's own binding of a <c>CancellationToken</c>
/// action parameter to <c>HttpContext.RequestAborted</c>. That is framework behaviour with
/// its own tests; every e2e POST in the suite runs through the real binder, and a
/// connection that lost its binder would 500 rather than quietly stamp something.
/// </para>
/// </summary>
[TestClass]
public class ServiceConnectionControllerCancellationTests
{
    private const string BaseUrl = "https://api.example.com";
    private const string ApiKey = "sk-secret-abcd1234";

    private static ConnectServiceRequest ValidRequest => new()
    {
        BaseUrl = BaseUrl,
        ApiKey = ApiKey,
    };

    /// <summary>The save lands, the probe does not, and nothing is stamped. Asserted on
    /// the rows rather than on the exception alone: "unverified" is also what a probe
    /// that ran and failed writes, so only the absence of the second write tells the two
    /// apart — and a controller that swallowed the cancellation and reported unverified
    /// would leave exactly that pair of rows behind.</summary>
    [TestMethod]
    public async Task Connect_WhenTheClientDisconnects_StampsNoVerification()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var harness = new ServiceConnectionControllerHarness(htmx: true)
            .GivenOneRowRepository()
            .GivenTheProbeRunsThrough(new CancellationObservingHandler());

        var connect = harness.Controller.Connect(ValidRequest, cancelled.Token);

        // Bounded, because a connect that does *not* thread the token does not fail — it
        // hangs on a handler waiting for a cancellation that will never come, and the
        // assertion below would sit there until the HTTP client gave up two minutes later
        // with a message about a timeout, naming neither the cause nor the token.
        var settled = await Task.WhenAny(connect, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.AreSame(connect, settled,
            "the connect did not finish: the caller's cancelled token never reached the probe");
        await Assert.ThrowsExceptionAsync<TaskCanceledException>(async () => await connect);

        Assert.AreEqual(1, harness.Rows.Count, "the connect wrote the connection and nothing else: no probe ran");
        Assert.AreEqual(ServiceConnection.Unverified, harness.Rows[^1].Verification);
        Assert.IsNull(harness.Rows[^1].VerifiedAt, "a probe the client walked away from verified nothing");
    }

    /// <summary>The token the action receives is the one the probe is given — the seam
    /// the test above depends on, asserted on its own so a failure names the cause. The
    /// harness's request carries a <em>different</em> token, so a controller that
    /// re-read <c>HttpContext.RequestAborted</c> instead of threading its parameter would
    /// be caught here rather than looking like a passing cancellation test.</summary>
    [TestMethod]
    public async Task Connect_ProbesWithTheTokenTheActionWasGiven_NotAFreshOne()
    {
        using var cts = new CancellationTokenSource();
        var harness = new ServiceConnectionControllerHarness(htmx: true)
            .GivenOneRowRepository()
            .GivenTheProbeAnswers(ModelCatalog.Unverified);

        await harness.Controller.Connect(ValidRequest, cts.Token);

        harness.Catalog.Verify(
            c => c.ListModelsAsync(It.IsAny<ServiceConnection>(), cts.Token), Times.Once,
            "the token a client disconnect trips is the one the action was handed");
    }
}
