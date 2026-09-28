using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using VitaTrack.Core.Features.ServiceConnections;
using VitaTrack.Web.Controllers;
using VitaTrack.Web.Models;

namespace VitaTrack.Tests;

/// <summary>
/// The shared scaffolding for the <c>ServiceConnectionController</c> test classes: a
/// single-row repository, a stubbed catalog, and a controller wired to the real
/// handlers over both. The handlers are the real types on purpose — the controller's
/// contract is that it defers to them, so stubbing them would stub away the contract
/// under test.
/// <para>
/// Two test classes rather than one because the complete-type size limit is 300 lines and
/// the connect surface plus the picker surface do not fit in it. Both are the same
/// controller; they are split by the request each one makes, not by the type.
/// </para>
/// </summary>
internal sealed class ServiceConnectionControllerHarness
{
    /// <summary>The header htmx sets on a request it issued, and the only thing that
    /// tells the controller to answer with a fragment instead of a redirect. Written
    /// here rather than in a helper so both classes read the same way.</summary>
    public const string HtmxHeader = "HX-Request";

    public const string HtmxHeaderValue = "true";

    private const int FirstRowId = 7;

    /// <summary>The id the stub gives the first row it is asked to save. Named here rather
    /// than repeated in tests so a change to the stub's id arithmetic is one edit.</summary>
    public const int SavedId = FirstRowId;

    private readonly List<ServiceConnection> _rows = [];

    public Mock<IServiceConnectionRepository> Connections { get; } = new();

    public Mock<IServiceCatalogClient> Catalog { get; } = new();

    public ServiceConnectionController Controller { get; }

    /// <param name="htmx">Whether the request under test carries <c>HX-Request</c>.</param>
    public ServiceConnectionControllerHarness(bool htmx = false)
    {
        var http = new DefaultHttpContext();
        if (htmx) http.Request.Headers[HtmxHeader] = HtmxHeaderValue;
        Controller = new ServiceConnectionController(
            Connections.Object,
            new SaveConnectionHandler(Connections.Object),
            new ProbeConnectionHandler(Connections.Object, Catalog.Object))
        {
            ControllerContext = new ControllerContext { HttpContext = http },
        };
    }

    /// <summary>Every row the stub has been asked to save, oldest first. The connect flow
    /// writes twice — the connection, then the probe's stamp — so which write a value
    /// came from is part of what the tests assert.</summary>
    public IReadOnlyList<ServiceConnection> Rows => _rows;

    /// <summary>Wires the repository as a single-row, read-after-write store: a save
    /// publishes the row the next read returns, and an insert gets an id.
    /// <para>
    /// Read-after-write is not decoration. A connect saves and then probes, and the
    /// probe reads the row back — so a mock returning a fixed value would have the probe
    /// verify a connection the save never wrote, and a test written against that would
    /// pass on code that does not work.
    /// </para></summary>
    public ServiceConnectionControllerHarness GivenOneRowRepository(ServiceConnection? row = null)
    {
        if (row is not null) _rows.Add(row);

        Connections.Setup(c => c.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _rows.Count > 0 ? _rows[^1] : null);
        Connections.Setup(c => c.SaveAsync(It.IsAny<ServiceConnection>(), It.IsAny<CancellationToken>()))
            .Returns((ServiceConnection saved, CancellationToken _) =>
            {
                var written = saved.Id == 0 ? saved with { Id = FirstRowId } : saved;
                _rows.Add(written);
                return Task.FromResult(written.Id);
            });

        return this;
    }

    /// <summary>The catalog answers with this. Without it the probe reads
    /// <see cref="ModelCatalog.Unverified"/>, which is the right default for a test that
    /// is not about the probe.</summary>
    public ServiceConnectionControllerHarness GivenTheProbeAnswers(ModelCatalog catalog)
    {
        Catalog.Setup(c => c.ListModelsAsync(It.IsAny<ServiceConnection>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(catalog);
        return this;
    }

    /// <summary>The active connection as the repository hands it back: saved, active,
    /// unverified. Every test that needs a live connection starts from this.</summary>
    public static ServiceConnection Saved(string apiKey = "sk-secret-abcd1234") => new()
    {
        Id = FirstRowId,
        Service = ServiceDescriptorRegistry.OpenCodeServiceId,
        BaseUrl = "https://api.example.com",
        ApiKey = apiKey,
        Model = "gpt-4o-mini",
        Verification = ServiceConnection.Unverified,
        IsActive = true,
    };

    public static ServiceConnectionViewModel ModelOf(IActionResult result)
    {
        var view = result as ViewResult;
        Assert.IsNotNull(view, "expected a view, got " + result.GetType().Name);
        var model = view.Model as ServiceConnectionViewModel;
        Assert.IsNotNull(model);
        return model;
    }

    /// <summary>The fragment an htmx POST must be answered with, and the model it was
    /// handed. Asserting the view name is what makes this a claim about <em>which</em>
    /// fragment: the picker and the unverified branch are siblings, and a controller
    /// that answered both with one of them would be wrong half the time.</summary>
    public static T FragmentOf<T>(IActionResult result, string expectedViewName) where T : class
    {
        var view = result as PartialViewResult;
        Assert.IsNotNull(view, "an htmx POST is answered with a fragment, got " + result.GetType().Name);
        Assert.AreEqual(expectedViewName, view.ViewName);
        var model = view.Model as T;
        Assert.IsNotNull(model, $"view '{expectedViewName}' was handed a model the test cannot read");
        return model;
    }

    /// <summary>The raw key must not be reachable from the view model at all. This walks
    /// the rendered model's own values, so a new property that leaked the key would fail
    /// here even though the masked property is still correct.</summary>
    public static bool JsonContains(object model, string needle) =>
        JsonSerializer.Serialize(model).Contains(needle, StringComparison.Ordinal);
}
