using System.Linq;
using System.Threading;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using VitaTrack.Core.Features.ServiceConnections;
using VitaTrack.Web.Controllers;
using VitaTrack.Web.Models;

namespace VitaTrack.Tests;

/// <summary>
/// The surface that only exists once a connection is saved: the two htmx POST targets
/// (<c>Connect</c>, which answers with the model picker or the unverified branch, and
/// <c>SelectModel</c>), and <c>Delete</c>. <see cref="ServiceConnectionControllerTests"/>
/// covers the connect form itself; the two are one controller split by request, because
/// the complete-type size limit does not leave room for both in one class.
/// <para>
/// Every test here is an htmx request unless its name says otherwise, so the assertion is
/// on the fragment the controller answered with and the model that fragment carries — a
/// controller that returned a redirect instead would leave the page saying nothing.
/// </para>
/// </summary>
[TestClass]
public class ServiceConnectionControllerProbeTests
{
    private const string BaseUrl = "https://api.example.com";
    private const string ApiKey = "sk-secret-abcd1234";
    private const int SavedId = ServiceConnectionControllerHarness.SavedId;

    private readonly ServiceConnectionControllerHarness _harness = new(htmx: true);

    private Mock<IServiceConnectionRepository> Connections => _harness.Connections;

    private ServiceConnectionController Controller => _harness.Controller;

    private static ConnectServiceRequest ValidRequest => new()
    {
        BaseUrl = BaseUrl,
        ApiKey = ApiKey,
    };

    [TestMethod]
    public async Task Connect_ValidProbe_RendersModelPickerWithCatalogModels()
    {
        _harness.GivenOneRowRepository()
            .GivenTheProbeAnswers(new ModelCatalog(["gpt-4o", "gpt-4o-mini"], true));

        var result = await Controller.Connect(ValidRequest, CancellationToken.None);

        var model = ServiceConnectionControllerHarness.FragmentOf<ServiceConnectionViewModel>(result, "_ConnectionState");
        CollectionAssert.AreEqual(new[] { "gpt-4o", "gpt-4o-mini" }, model.ModelPicker!.Models.ToList(),
            "the picker is populated from the catalog the service actually listed — not from a guess at model names");
        Assert.AreEqual(ServiceConnection.Verified, model.Saved!.Verification,
            "the saved state the swap carries must say what the probe found");
        Assert.IsNull(model.ProbeNote, "a probe that listed models has nothing to warn about");
    }

    /// <summary>The stamp the picker is rendered from was persisted, not just read: a
    /// verification flag that lives only in the response is gone on the next page load,
    /// and the unverified state would then be the only one that ever survives.</summary>
    [TestMethod]
    public async Task Connect_ValidProbe_StoresTheVerificationItReports()
    {
        _harness.GivenOneRowRepository()
            .GivenTheProbeAnswers(new ModelCatalog(["gpt-4o"], true));

        await Controller.Connect(ValidRequest, CancellationToken.None);

        Assert.AreEqual(2, _harness.Rows.Count, "the connect writes the connection and then the probe's stamp");
        Assert.AreEqual(ServiceConnection.Verified, _harness.Rows[^1].Verification,
            "the verified stamp must reach the row the next page load reads");
        Assert.IsNotNull(_harness.Rows[^1].VerifiedAt);
    }

    /// <summary>404 is not a bad key. Several OpenAI-compatible endpoints do not
    /// implement model listing at all, so an unverified answer is a sibling fragment the
    /// user can act on — not an error, and not a reason to discard the connection they
    /// just saved.</summary>
    [TestMethod]
    public async Task Connect_FailedProbe_RendersTheUnverifiedWarningAndSavesTheConnection()
    {
        _harness.GivenOneRowRepository().GivenTheProbeAnswers(ModelCatalog.Unverified);

        var result = await Controller.Connect(ValidRequest, CancellationToken.None);

        var model = ServiceConnectionControllerHarness.FragmentOf<ServiceConnectionViewModel>(result, "_ConnectionState");
        Assert.AreEqual(ServiceConnection.Unverified, model.Saved!.Verification, "the badge says what the probe found");
        Assert.AreEqual("••••1234", model.Saved.MaskedApiKey, "the key is saved and shown masked, never in full");
        Assert.IsNotNull(model.ModelPicker, "a failed probe still leaves a model to choose");
        Assert.AreEqual(0, model.ModelPicker.Models.Count,
            "no catalog was listed, so the model field is free text rather than an empty dropdown");
        Assert.IsNotNull(model.ProbeNote,
            "the unverified branch is what tells the user a 404 is not a bad key; without the note the free-text field is unexplained");
        Assert.AreEqual(2, _harness.Rows.Count, "a failed probe still leaves the connection saved");
    }

    /// <summary>Progressive enhancement, and the reason the htmx branch above is not the
    /// only one. Without htmx — a browser with the script blocked, or a plain form post —
    /// the same POST has to leave the user on a page that says what happened, which is a
    /// redirect to the freshly rendered Index.</summary>
    [TestMethod]
    public async Task Connect_WithoutHtmx_RedirectsToIndex_EvenThoughTheProbeRan()
    {
        var plain = new ServiceConnectionControllerHarness();
        plain.GivenOneRowRepository().GivenTheProbeAnswers(new ModelCatalog(["gpt-4o"], true));

        var result = await plain.Controller.Connect(ValidRequest, CancellationToken.None);

        var redirect = result as RedirectToActionResult;
        Assert.IsNotNull(redirect, "without htmx a fragment would be rendered into no target at all");
        Assert.AreEqual("Index", redirect.ActionName);
        Assert.AreEqual(ServiceConnection.Verified, plain.Rows[^1].Verification,
            "the probe is about the connection, not about how the page is delivered");
    }

    /// <summary>The picker's half of the same claim. <c>SelectModel</c> answers through
    /// <c>Swapped</c>, so a plain form post redirects to the freshly rendered Index with
    /// the choice saved — which is what makes the markup's <c>asp-action</c> and
    /// <c>method="post"</c> load-bearing: with the script blocked the browser falls back
    /// to them, and without them it would GET the current URL and drop the submission.
    /// The markup is the half a controller test cannot see; the e2e pins that
    /// (<c>should save a model with htmx blocked</c>).</summary>
    [TestMethod]
    public async Task SelectModel_WithoutHtmx_RedirectsToIndex_AndStillSavesTheModel()
    {
        var plain = new ServiceConnectionControllerHarness();
        plain.GivenOneRowRepository(ServiceConnectionControllerHarness.Saved());

        var result = await plain.Controller.SelectModel(
            new SelectModelRequest { Model = "gpt-4o", Variant = "high" }, CancellationToken.None);

        var redirect = result as RedirectToActionResult;
        Assert.IsNotNull(redirect, "a fragment rendered into no target at all is how the choice would be lost");
        Assert.AreEqual("Index", redirect.ActionName);
        Assert.AreEqual("gpt-4o", plain.Rows[^1].Model,
            "the redirect is about how the page is delivered, not about whether the choice was kept");
    }

    [TestMethod]
    public async Task SelectModel_PersistsModelAndVariant()
    {
        _harness.GivenOneRowRepository(ServiceConnectionControllerHarness.Saved());

        var result = await Controller.SelectModel(new SelectModelRequest { Model = "gpt-4o", Variant = "high" }, CancellationToken.None);

        Assert.AreEqual(2, _harness.Rows.Count);
        Assert.AreEqual("gpt-4o", _harness.Rows[^1].Model);
        Assert.AreEqual("high", _harness.Rows[^1].Variant);
        Assert.IsTrue(_harness.Rows[^1].IsActive, "writing a model must not deactivate the connection it belongs to");
        var model = ServiceConnectionControllerHarness.FragmentOf<ServiceConnectionViewModel>(result, "_ConnectionState");
        Assert.IsTrue(model.ModelPicker!.JustSaved, "the swap confirms the choice; a silent no-op would be indistinguishable from success");
        Assert.IsNull(model.ProbeNote, "choosing a model is not a probe, so it must not produce the probe's warning");
        Assert.AreEqual("gpt-4o", model.ModelPicker.SelectedModel);
        Assert.AreEqual("high", model.ModelPicker.SelectedVariant);
    }

    /// <summary>Review Focus #4, seen from the surface the picker writes. The value comes
    /// from a <c>&lt;select&gt;</c> today, but a pasted one would carry spaces, and the
    /// stored model is what goes into a request body.</summary>
    [TestMethod]
    public async Task SelectModel_TrimsTheModelItStores()
    {
        _harness.GivenOneRowRepository(ServiceConnectionControllerHarness.Saved());

        await Controller.SelectModel(new SelectModelRequest { Model = "  gpt-4o  ", Variant = "none" }, CancellationToken.None);

        Assert.AreEqual("gpt-4o", _harness.Rows[^1].Model);
    }

    /// <summary>A stale verified stamp must not survive a new choice. Re-saving the row
    /// through the picker writes every column, so this is where a forgotten
    /// <c>VerifiedAt = null</c> would leave "unverified" beside a timestamp.</summary>
    [TestMethod]
    public async Task SelectModel_LeavesTheVerificationStampAlone()
    {
        _harness.GivenOneRowRepository(ServiceConnectionControllerHarness.Saved() with
        {
            Verification = ServiceConnection.Verified,
            VerifiedAt = System.DateTimeOffset.UtcNow,
        });

        await Controller.SelectModel(new SelectModelRequest { Model = "gpt-4o", Variant = "low" }, CancellationToken.None);

        Assert.AreEqual(ServiceConnection.Verified, _harness.Rows[^1].Verification,
            "choosing a model says nothing about whether the credential works");
        Assert.IsNotNull(_harness.Rows[^1].VerifiedAt);
    }

    /// <summary>The variant vocabulary is enforced by the bind model, so the rejection the
    /// controller acts on is produced <em>by the model</em> here rather than typed into
    /// <c>ModelState</c> by the test. That is the difference between a test that pins the
    /// controller's response to a rejection and one that pins the rejection itself:
    /// delete the registry check from <c>SelectModelRequest.Validate</c> and
    /// <c>ModelState.IsValid</c> goes true, the branch is never taken, and the write this
    /// test forbids happens. (MVC's own binding of a validated model into
    /// <c>ModelState</c> is framework behaviour every e2e POST exercises; what is in this
    /// repo's hands is the rule and the branch, and both are pinned.)</summary>
    [TestMethod]
    public async Task SelectModel_RejectsAVariantOutsideTheRegistryVocabulary()
    {
        _harness.GivenOneRowRepository(ServiceConnectionControllerHarness.Saved());
        var request = new SelectModelRequest { Model = "gpt-4o", Variant = "turbo" };
        _harness.GivenModelStateProducedByValidating(request);

        var result = await Controller.SelectModel(request, CancellationToken.None);

        Assert.AreEqual(1, _harness.Rows.Count, "a rejected variant must not write the column the registry owns");
        ServiceConnectionControllerHarness.FragmentOf<ServiceConnectionViewModel>(result, "_ConnectionState");
    }

    /// <summary>Review Focus #5 on the surface that would break it: the picker writes the
    /// model and the variant, and must not touch anything else on the row. A whole-row
    /// rebuild from the connect form's (much smaller) request would reset both.</summary>
    [TestMethod]
    public async Task SelectModel_LeavesTheConnectionSettingsAlone()
    {
        _harness.GivenOneRowRepository(ServiceConnectionControllerHarness.Saved() with { MaxTokens = 999, Temperature = 0.25 });

        await Controller.SelectModel(new SelectModelRequest { Model = "gpt-4o", Variant = "low" }, CancellationToken.None);

        var written = _harness.Rows[^1];
        Assert.AreEqual("sk-secret-abcd1234", written.ApiKey, "the picker does not know the key and must not blank it");
        Assert.AreEqual("https://api.example.com", written.BaseUrl);
        Assert.AreEqual(ServiceDescriptorRegistry.OpenCodeServiceId, written.Service);
        Assert.AreEqual(999, written.MaxTokens);
        Assert.AreEqual(0.25, written.Temperature);
    }

    /// <summary>Not reachable from the picker itself — it only renders when a connection
    /// exists — but reachable from a page left open while the connection was removed in
    /// another tab. The answer is the same region with the error on it, because the user
    /// is looking at the picker.</summary>
    [TestMethod]
    public async Task SelectModel_WithNoActiveConnection_ReportsItAndWritesNothing()
    {
        _harness.GivenOneRowRepository();

        var result = await Controller.SelectModel(new SelectModelRequest { Model = "gpt-4o", Variant = "none" }, CancellationToken.None);

        ServiceConnectionControllerHarness.FragmentOf<ServiceConnectionViewModel>(result, "_ConnectionState");
        Assert.AreEqual(0, _harness.Rows.Count);
        Assert.IsTrue(Controller.ModelState.ContainsKey(string.Empty),
            "the user has to be told why nothing happened");
    }

    [TestMethod]
    public async Task Delete_ClearsTheActiveConnection()
    {
        _harness.GivenOneRowRepository(ServiceConnectionControllerHarness.Saved());

        var result = await Controller.Delete();

        Connections.Verify(c => c.DeleteAsync(SavedId, It.IsAny<CancellationToken>()), Times.Once,
            "the row the Settings screen is showing is the one that must go");
        var redirect = result as RedirectToActionResult;
        Assert.IsNotNull(redirect);
        Assert.AreEqual("Index", redirect.ActionName, "the user lands back on a page that no longer claims a connection");
    }

    [TestMethod]
    public async Task Delete_WithNothingSaved_DoesNotWriteAndStillRedirects()
    {
        _harness.GivenOneRowRepository();

        var result = await Controller.Delete();

        Connections.Verify(c => c.DeleteAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.IsNotNull(result as RedirectToActionResult);
    }
}
