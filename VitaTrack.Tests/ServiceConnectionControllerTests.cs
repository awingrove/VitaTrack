using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using VitaTrack.Core.Features.ServiceConnections;
using VitaTrack.Web.Controllers;
using VitaTrack.Web.Models;

namespace VitaTrack.Tests;

/// <summary>
/// Controller tests over a stubbed repository, so they assert the controller's own
/// job: what the view is handed, and whether the POST redirects or comes back with
/// the form. Whether the write actually lands is the handler's and repository's
/// tests, not this one's. <para>
/// The connect surface. The picker surface — <c>SelectModel</c> and <c>Delete</c> — is
/// <see cref="ServiceConnectionControllerPickerTests"/>; the two together are one
/// controller split by the request under test.
/// </para>
/// </summary>
[TestClass]
public class ServiceConnectionControllerTests
{
    private readonly ServiceConnectionControllerHarness _harness = new();

    private Mock<IServiceConnectionRepository> Connections => _harness.Connections;

    private ServiceConnectionController Controller => _harness.Controller;

    [TestMethod]
    public async Task Index_WithNoConnection_RendersTheConnectForm()
    {
        Connections.Setup(c => c.GetActiveAsync(It.IsAny<CancellationToken>())).ReturnsAsync((ServiceConnection?)null);

        var model = ServiceConnectionControllerHarness.ModelOf(await Controller.Index());

        Assert.IsNull(model.Saved, "with nothing saved there is no saved state to render");
        Assert.IsNotNull(model.Form);
        Assert.AreEqual(string.Empty, model.Form.BaseUrl);
        Assert.IsNull(model.ModelPicker, "there is nothing to pick a model for before a connection exists");
    }

    /// <summary>With a connection saved, the picker is part of the page — the user has a
    /// model to choose and a connection to point at. Asserted on the view model rather
    /// than on markup: a plain GET does not probe (see
    /// <see cref="Index_DoesNotProbe_TheServiceOnEveryPageLoad"/>), so the picker arrives
    /// with no catalog, and the dropdown-versus-free-text choice is the view's.</summary>
    [TestMethod]
    public async Task Index_WithActiveConnection_RendersTheModelPicker()
    {
        Connections.Setup(c => c.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceConnectionControllerHarness.Saved());

        var model = ServiceConnectionControllerHarness.ModelOf(await Controller.Index());

        Assert.IsNotNull(model.ModelPicker, "a saved connection has a model to pick");
        Assert.AreEqual("gpt-4o-mini", model.ModelPicker.SelectedModel, "the picker shows the model in use");
        Assert.AreEqual(ServiceDescriptorRegistry.Variants.Count, model.ModelPicker.Variants.Count,
            "the variant dropdown is the registry's fixed vocabulary, rendered whole");
    }

    /// <summary>A bare page load must not spend a network round trip. The probe is
    /// reachable from the connect form and from nowhere else, so the claim is that
    /// <c>Index</c> makes no call at all — asserted on the client's invocation count,
    /// which is zero only if the call was never made.</summary>
    [TestMethod]
    public async Task Index_DoesNotProbe_TheServiceOnEveryPageLoad()
    {
        Connections.Setup(c => c.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceConnectionControllerHarness.Saved());

        await Controller.Index();

        _harness.Catalog.Verify(
            c => c.ListModelsAsync(It.IsAny<ServiceConnection>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task Index_WithActiveConnection_RendersTheSavedConnection_WithMaskedKey()
    {
        Connections.Setup(c => c.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceConnectionControllerHarness.Saved(apiKey: "sk-secret-abcd1234"));

        var model = ServiceConnectionControllerHarness.ModelOf(await Controller.Index());

        Assert.IsNotNull(model.Saved);
        Assert.AreEqual(ServiceDescriptorRegistry.OpenCodeServiceId, model.Saved.Service, "the saved service reaches the view");
        Assert.AreEqual("https://api.example.com", model.Saved.BaseUrl);
        Assert.AreEqual("gpt-4o-mini", model.Saved.Model);
        Assert.AreEqual(ServiceConnection.Unverified, model.Saved.Verification, "the saved verification state reaches the view");
        Assert.AreEqual("••••1234", model.Saved.MaskedApiKey, "only the last four characters reach the view");
        Assert.IsFalse(
            ServiceConnectionControllerHarness.JsonContains(model, "sk-secret-abcd1234"),
            "the raw API key must appear nowhere in what the view is handed");
    }

    [TestMethod]
    public async Task Index_WithActiveConnection_PrefillsTheFormButNotTheKey()
    {
        Connections.Setup(c => c.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceConnectionControllerHarness.Saved(apiKey: "sk-secret-abcd1234"));

        var model = ServiceConnectionControllerHarness.ModelOf(await Controller.Index());

        Assert.AreEqual("https://api.example.com", model.Form.BaseUrl, "a reconnect should not retype the URL");
        Assert.AreEqual("gpt-4o-mini", model.Form.Model, "a reconnect should not retype the model");
        Assert.AreEqual(ServiceDescriptorRegistry.OpenCodeServiceId, model.Form.Service,
            "the saved service is the one the form offers again");
        Assert.AreEqual(string.Empty, model.Form.ApiKey, "the key is re-entered on every save: the form model never carries it");
    }

    [TestMethod]
    public async Task Index_WithAKeyNoLongerThanTheMask_ShowsNoCharactersOfIt()
    {
        Connections.Setup(c => c.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceConnectionControllerHarness.Saved(apiKey: "1234"));

        var model = ServiceConnectionControllerHarness.ModelOf(await Controller.Index());

        Assert.IsNotNull(model.Saved);
        Assert.AreEqual("••••", model.Saved.MaskedApiKey, "a four-character key has no safe characters to show");
    }

    /// <summary>Review Focus #3 as the connect surface sees it. Not an htmx request, so
    /// the answer is a redirect rather than a fragment — and the probe still ran, because
    /// the redirect is about how the page is delivered, not about whether the connection
    /// was tested. The row the repository is left holding is the proof: it is verified
    /// and stamped, which only the probe could have done.</summary>
    [TestMethod]
    public async Task Connect_ValidRequest_SavesAndRedirectsToIndex()
    {
        _harness.GivenOneRowRepository()
            .GivenTheProbeAnswers(new ModelCatalog(["gpt-4o"], true));

        var result = await Controller.Connect(new ConnectServiceRequest
        {
            BaseUrl = "https://api.example.com",
            ApiKey = "sk-secret",
            Model = "gpt-4o-mini"
        }, CancellationToken.None);

        var redirect = result as RedirectToActionResult;
        Assert.IsNotNull(redirect, "a saved connection redirects so a reload cannot resubmit the form");
        Assert.AreEqual("Index", redirect.ActionName);
        Assert.AreEqual(2, _harness.Rows.Count, "the connect writes the connection and then the probe's stamp");
        Assert.AreEqual("https://api.example.com", _harness.Rows[0].BaseUrl);
        Assert.AreEqual("sk-secret", _harness.Rows[0].ApiKey);
        Assert.IsTrue(_harness.Rows[0].IsActive, "the connection just written is the active one");
        Assert.AreEqual("gpt-4o-mini", _harness.Rows[0].Model);
        Assert.AreEqual(ServiceConnection.Verified, _harness.Rows[1].Verification);
    }

    [TestMethod]
    public async Task Connect_InvalidModelState_ReturnsTheFormWithErrors()
    {
        // The error is seeded, not produced by a blank field: a hand-built request
        // carries a valid ModelState, and a blank field is caught by the handler's own
        // rejection instead — the branch under test would never run. So the field
        // values are ones the handler would happily write, and nothing but this guard
        // stands between the POST and a save.
        Controller.ModelState.AddModelError(nameof(ConnectServiceRequest.BaseUrl), ConnectServiceRequest.BaseUrlRequired);
        Connections.Setup(c => c.GetActiveAsync(It.IsAny<CancellationToken>())).ReturnsAsync((ServiceConnection?)null);

        var result = await Controller.Connect(new ConnectServiceRequest
        {
            BaseUrl = "https://api.example.com",
            ApiKey = "sk-secret"
        }, CancellationToken.None);

        var view = result as ViewResult;
        Assert.IsNotNull(view, "an invalid ModelState comes back to the form, it does not redirect");
        Assert.AreEqual("Index", view.ViewName);
        Assert.IsNotNull(ServiceConnectionControllerHarness.ModelOf(view).Form);
        Assert.IsTrue(view.ViewData.ModelState.ErrorCount > 0, "the errors that rejected the POST come back with the form");
        Connections.Verify(c => c.SaveAsync(It.IsAny<ServiceConnection>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>The htmx half of the same rejection, and the one that was broken: a
    /// fragment that held only the saved state put the field errors — which belong to the
    /// form — nowhere on the page. Asserting the fragment name rather than "a fragment came
    /// back" is the point: the swapped region has to be the one that carries the form.</summary>
    [TestMethod]
    public async Task Connect_InvalidModelState_OverHtmx_SwapsInTheRegionThatCarriesTheForm()
    {
        var htmx = new ServiceConnectionControllerHarness(htmx: true);
        htmx.Connections.Setup(c => c.GetActiveAsync(It.IsAny<CancellationToken>())).ReturnsAsync((ServiceConnection?)null);
        htmx.Controller.ModelState.AddModelError(nameof(ConnectServiceRequest.BaseUrl), ConnectServiceRequest.BaseUrlRequired);

        var result = await htmx.Controller.Connect(new ConnectServiceRequest
        {
            BaseUrl = "https://api.example.com",
            ApiKey = "sk-secret"
        }, CancellationToken.None);

        var model = ServiceConnectionControllerHarness.FragmentOf<ServiceConnectionViewModel>(result, "_ConnectionState");
        Assert.AreEqual("https://api.example.com", model.Form.BaseUrl, "what the user typed comes back with the error");
        htmx.Connections.Verify(c => c.SaveAsync(It.IsAny<ServiceConnection>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task Connect_Rejected_RedisplaysWhatWasTypedOverTheSavedValues()
    {
        // A rejected submit must not silently swap the user's half-typed URL for the
        // saved one, or they cannot tell which value the form is about to save.
        Connections.Setup(c => c.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceConnectionControllerHarness.Saved(apiKey: "sk-secret-abcd1234"));

        var result = await Controller.Connect(new ConnectServiceRequest
        {
            BaseUrl = "https://typed.example.com",
            ApiKey = "  ",
            Model = "typed-model"
        }, CancellationToken.None);

        var model = ServiceConnectionControllerHarness.ModelOf(result);
        Assert.AreEqual("https://typed.example.com", model.Form.BaseUrl);
        Assert.AreEqual("typed-model", model.Form.Model);
        Assert.AreEqual(string.Empty, model.Form.ApiKey,
            "the typed key does not come back in the form model, whatever the control type renders");
        Connections.Verify(c => c.SaveAsync(It.IsAny<ServiceConnection>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task Connect_WhenTheHandlerRejects_ShowsTheErrorAndDoesNotRedirect()
    {
        // A hand-built request carries a valid ModelState, so this reaches the handler's
        // own blank-key rejection — the path MVC's [Required] normally takes first.
        var result = await Controller.Connect(new ConnectServiceRequest { BaseUrl = "https://api.example.com", ApiKey = "  " }, CancellationToken.None);

        var view = result as ViewResult;
        Assert.IsNotNull(view, "a rejected connect comes back to the form with the handler's error");
        Assert.AreEqual(string.Empty, ServiceConnectionControllerHarness.ModelOf(view).Form.ApiKey, "even on this path the key is not carried");
        Connections.Verify(c => c.SaveAsync(It.IsAny<ServiceConnection>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>The controller half of the registry rule. The handler's own rejection is
    /// pinned in <c>SaveConnectionHandlerTests.HandleAsync_RejectsUnknownServiceId</c>;
    /// what is asserted here is that the rejection comes back as the form with the
    /// handler's wording and no write — not as a redirect to a page claiming a connection
    /// was saved, which is what the old "always redirect" branch would have done.</summary>
    [TestMethod]
    public async Task Connect_WithAServiceTheRegistryDoesNotKnow_ReturnsTheFormWithTheErrorAndWritesNothing()
    {
        Connections.Setup(c => c.GetActiveAsync(It.IsAny<CancellationToken>())).ReturnsAsync((ServiceConnection?)null);

        var result = await Controller.Connect(new ConnectServiceRequest
        {
            BaseUrl = "https://api.example.com",
            ApiKey = "sk-secret",
            Service = "not-a-service",
        }, CancellationToken.None);

        var view = result as ViewResult;
        Assert.IsNotNull(view, "a service the registry does not ship is a form error, not a saved connection");
        Assert.AreEqual("Index", view.ViewName);
        var model = ServiceConnectionControllerHarness.ModelOf(view);
        Assert.AreEqual("not-a-service", model.Form.Service, "the choice is redisplayed so the user can correct it");
        Connections.Verify(c => c.SaveAsync(It.IsAny<ServiceConnection>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task Connect_WhenNoRowIsWritten_ShowsTheErrorInsteadOfClaimingSuccess()
    {
        Connections.Setup(c => c.GetActiveAsync(It.IsAny<CancellationToken>())).ReturnsAsync((ServiceConnection?)null);
        // The repository's "no row was written" answer must not be read as a saved connection.
        Connections.Setup(c => c.SaveAsync(It.IsAny<ServiceConnection>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(IServiceConnectionRepository.NoRowWritten);

        var result = await Controller.Connect(new ConnectServiceRequest
        {
            BaseUrl = "https://api.example.com",
            ApiKey = "sk-secret"
        }, CancellationToken.None);

        Assert.IsNotNull(result as ViewResult, "an unwritten row is a failure to report, not a save to redirect away from");
    }
}
