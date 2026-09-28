using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewComponents;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using VitaTrack.Core.Features.ServiceConnections;
using VitaTrack.Web.Models;
using VitaTrack.Web.ViewComponents;

namespace VitaTrack.Tests;

/// <summary>
/// The connection badge, which is the reason the unverified state cannot be read as a
/// verified one from somewhere other than Settings: the enrichment pages render it too,
/// so a user about to spend a request on a service that was never confirmed sees that it
/// was never confirmed.
/// <para>
/// It is a view component rather than a property on the Review model because two slices
/// need it and only one of them owns the connection: injecting
/// <see cref="IServiceConnectionRepository"/> into a view would put a data read in the
/// view, and threading the state through <c>SupplementController</c>'s view models
/// would make that controller know about a table it does not own.
/// </para>
/// </summary>
[TestClass]
public class ConnectionBadgeViewComponentTests
{
    [TestMethod]
    public async Task Invoke_WithAnUnverifiedConnection_ReportsUnverified()
    {
        var view = await InvokeAsync(Verification(ServiceConnection.Unverified));

        Assert.IsInstanceOfType(view, typeof(ViewViewComponentResult), "a badge is markup, not an empty string");
        var model = AssertBadgeModel(view);
        Assert.IsFalse(model.Verified, "an unverified connection must not render as a good one");
        Assert.AreEqual(ServiceConnection.Unverified, model.Verification);
    }

    [TestMethod]
    public async Task Invoke_WithAVerifiedConnection_ReportsVerified()
    {
        var view = await InvokeAsync(Verification(ServiceConnection.Verified));

        Assert.IsTrue(AssertBadgeModel(view).Verified);
    }

    /// <summary>With nothing connected there is no state to report, and the enrichment
    /// flow already says so in words — <c>LlmService</c> refuses with "No AI service
    /// connection is set up". A second, vaguer statement of the same thing would be noise
    /// on a page the user cannot fix from.</summary>
    [TestMethod]
    public async Task Invoke_WithNoConnection_RendersNothing()
    {
        var view = await InvokeAsync((ServiceConnection?)null);

        Assert.IsInstanceOfType(view, typeof(ContentViewComponentResult));
        Assert.AreEqual(string.Empty, ((ContentViewComponentResult)view).Content, "an empty badge is still a badge");
    }

    /// <summary>The read is a database round trip on every page that renders the badge, so
    /// the component must ask the repository exactly once — not once per property the
    /// view reads.</summary>
    [TestMethod]
    public async Task Invoke_ReadsTheActiveConnectionOnce()
    {
        var connections = new Mock<IServiceConnectionRepository>();
        connections.Setup(c => c.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Verification(ServiceConnection.Verified));

        await InvokeAsync(connections.Object);

        connections.Verify(c => c.GetActiveAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    private static async Task<IViewComponentResult> InvokeAsync(ServiceConnection? active)
    {
        var connections = new Mock<IServiceConnectionRepository>();
        connections.Setup(c => c.GetActiveAsync(It.IsAny<CancellationToken>())).ReturnsAsync(active);
        return await InvokeAsync(connections.Object);
    }

    /// <summary>The component reads <c>HttpContext.RequestAborted</c>, and a view component
    /// reaches that through its <c>ViewContext</c> — so a bare
    /// <c>ViewComponentContext</c> would null-reference on the first line.</summary>
    private static async Task<IViewComponentResult> InvokeAsync(IServiceConnectionRepository connections) =>
        await new ConnectionBadgeViewComponent(connections)
        {
            ViewComponentContext = new ViewComponentContext
            {
                ViewContext = new ViewContext { HttpContext = new DefaultHttpContext() },
            },
        }.InvokeAsync();

    private static ServiceConnection Verification(string verification) =>
        ServiceConnectionControllerHarness.Saved() with { Verification = verification };

    /// <summary>The model the badge partial is rendered from. The null-forgiving operator
    /// is the compiler being told to trust the assertion immediately above it, which is
    /// exactly the case it is for: a failed <c>IsNotNull</c> has already thrown.</summary>
    private static ConnectionBadgeViewModel AssertBadgeModel(IViewComponentResult view)
    {
        var rendered = view as ViewViewComponentResult;
        Assert.IsNotNull(rendered, "the badge must be markup, not an empty string: expected a view, got "
            + view.GetType().Name);
        var viewData = rendered.ViewData;
        Assert.IsNotNull(viewData, "a view result with no ViewData has no model to read");
        var model = viewData.Model as ConnectionBadgeViewModel;
        Assert.IsNotNull(model, "the badge view was handed a model this test cannot read");
        return model!;
    }
}
