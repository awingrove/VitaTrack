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
/// tests, not this one's.
/// </summary>
[TestClass]
public class ServiceConnectionControllerTests
{
    private Mock<IServiceConnectionRepository> _connections = null!;
    private ServiceConnectionController _controller = null!;

    [TestInitialize]
    public void Setup()
    {
        _connections = new Mock<IServiceConnectionRepository>();
        // The handler under test is the real one: the controller's contract is that it
        // defers to the handler, so stubbing the handler would stub away that contract.
        _controller = new ServiceConnectionController(_connections.Object, new SaveConnectionHandler(_connections.Object));
        _controller.ControllerContext = new ControllerContext { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext() };
    }

    [TestMethod]
    public async Task Index_WithNoConnection_RendersTheConnectForm()
    {
        _connections.Setup(c => c.GetActiveAsync(It.IsAny<CancellationToken>())).ReturnsAsync((ServiceConnection?)null);

        var result = await _controller.Index();

        var model = AssertViewModel(result);
        Assert.IsNull(model.Saved, "with nothing saved there is no saved state to render");
        Assert.IsNotNull(model.Form);
        Assert.AreEqual(string.Empty, model.Form.BaseUrl);
    }

    [TestMethod]
    public async Task Index_WithActiveConnection_RendersTheSavedConnection_WithMaskedKey()
    {
        _connections.Setup(c => c.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Saved(apiKey: "sk-secret-abcd1234"));

        var result = await _controller.Index();

        var model = AssertViewModel(result);
        Assert.IsNotNull(model.Saved);
        Assert.AreEqual("https://api.example.com", model.Saved.BaseUrl);
        Assert.AreEqual("gpt-4o-mini", model.Saved.Model);
        Assert.AreEqual("••••1234", model.Saved.MaskedApiKey, "only the last four characters reach the view");
        Assert.IsFalse(
            JsonContains(model, "sk-secret-abcd1234"),
            "the raw API key must appear nowhere in what the view is handed");
    }

    [TestMethod]
    public async Task Index_WithActiveConnection_PrefillsTheFormButNotTheKey()
    {
        _connections.Setup(c => c.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Saved(apiKey: "sk-secret-abcd1234"));

        var model = AssertViewModel(await _controller.Index());

        Assert.AreEqual("https://api.example.com", model.Form.BaseUrl, "a reconnect should not retype the URL");
        Assert.AreEqual("gpt-4o-mini", model.Form.Model, "a reconnect should not retype the model");
        Assert.AreEqual(string.Empty, model.Form.ApiKey, "the key is a password field: it is re-entered, never prefilled");
    }

    [TestMethod]
    public async Task Index_WithAKeyNoLongerThanTheMask_ShowsNoCharactersOfIt()
    {
        _connections.Setup(c => c.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Saved(apiKey: "1234"));

        var model = AssertViewModel(await _controller.Index());

        Assert.IsNotNull(model.Saved);
        Assert.AreEqual("••••", model.Saved.MaskedApiKey, "a four-character key has no safe characters to show");
    }

    [TestMethod]
    public async Task Connect_ValidRequest_SavesAndRedirectsToIndex()
    {
        _connections.Setup(c => c.GetActiveAsync(It.IsAny<CancellationToken>())).ReturnsAsync((ServiceConnection?)null);
        _connections.Setup(c => c.SaveAsync(It.IsAny<ServiceConnection>(), It.IsAny<CancellationToken>())).ReturnsAsync(42);

        var result = await _controller.Connect(new ConnectServiceRequest
        {
            BaseUrl = "https://api.example.com",
            ApiKey = "sk-secret",
            Model = "gpt-4o-mini"
        });

        var redirect = result as RedirectToActionResult;
        Assert.IsNotNull(redirect, "a saved connection redirects so a reload cannot resubmit the form");
        Assert.AreEqual("Index", redirect.ActionName);
        _connections.Verify(c => c.SaveAsync(
            It.Is<ServiceConnection>(s => s.BaseUrl == "https://api.example.com" && s.ApiKey == "sk-secret" && s.IsActive),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task Connect_InvalidRequest_ReturnsTheFormWithErrors()
    {
        var result = await _controller.Connect(new ConnectServiceRequest { BaseUrl = "   ", ApiKey = "" });

        var view = result as ViewResult;
        Assert.IsNotNull(view, "a rejected connect comes back to the form, it does not redirect");
        Assert.AreEqual("Index", view.ViewName);
        Assert.IsNotNull(AssertViewModel(view).Form);
        _connections.Verify(c => c.SaveAsync(It.IsAny<ServiceConnection>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task Connect_Rejected_RedisplaysWhatWasTypedOverTheSavedValues()
    {
        // A rejected submit must not silently swap the user's half-typed URL for the
        // saved one, or they cannot tell which value the form is about to save.
        _connections.Setup(c => c.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Saved(apiKey: "sk-secret-abcd1234"));

        var result = await _controller.Connect(new ConnectServiceRequest
        {
            BaseUrl = "https://typed.example.com",
            ApiKey = "  ",
            Model = "typed-model"
        });

        var model = AssertViewModel(result);
        Assert.AreEqual("https://typed.example.com", model.Form.BaseUrl);
        Assert.AreEqual("typed-model", model.Form.Model);
        Assert.AreEqual("  ", model.Form.ApiKey);
        _connections.Verify(c => c.SaveAsync(It.IsAny<ServiceConnection>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task Connect_WhenTheHandlerRejects_ShowsTheErrorAndDoesNotRedirect()
    {
        var result = await _controller.Connect(new ConnectServiceRequest { BaseUrl = "https://api.example.com", ApiKey = "  " });

        var view = result as ViewResult;
        Assert.IsNotNull(view, "a rejected connect comes back to the form with the handler's error");
        _connections.Verify(c => c.SaveAsync(It.IsAny<ServiceConnection>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task Connect_WhenNoRowIsWritten_ShowsTheErrorInsteadOfClaimingSuccess()
    {
        _connections.Setup(c => c.GetActiveAsync(It.IsAny<CancellationToken>())).ReturnsAsync((ServiceConnection?)null);
        // SaveAsync's "no row was written" answer must not be read as a saved connection.
        _connections.Setup(c => c.SaveAsync(It.IsAny<ServiceConnection>(), It.IsAny<CancellationToken>())).ReturnsAsync(0);

        var result = await _controller.Connect(new ConnectServiceRequest
        {
            BaseUrl = "https://api.example.com",
            ApiKey = "sk-secret"
        });

        var view = result as ViewResult;
        Assert.IsNotNull(view, "an unwritten row is a failure to report, not a save to redirect away from");
    }

    private static ServiceConnectionViewModel AssertViewModel(IActionResult result)
    {
        var view = result as ViewResult;
        Assert.IsNotNull(view);
        var model = view.Model as ServiceConnectionViewModel;
        Assert.IsNotNull(model);
        return model;
    }

    private static ServiceConnection Saved(string apiKey) => new()
    {
        Id = 7,
        Service = SaveConnectionHandler.ServiceName,
        BaseUrl = "https://api.example.com",
        ApiKey = apiKey,
        Model = "gpt-4o-mini",
        Verification = ServiceConnection.Unverified,
        IsActive = true
    };

    /// <summary>The raw key must not be reachable from the view model at all. This
    /// walks the rendered model's own values, so a new property that leaked the key
    /// would fail here even though the masked property is still correct.</summary>
    private static bool JsonContains(object model, string needle) =>
        System.Text.Json.JsonSerializer.Serialize(model).Contains(needle, StringComparison.Ordinal);
}
