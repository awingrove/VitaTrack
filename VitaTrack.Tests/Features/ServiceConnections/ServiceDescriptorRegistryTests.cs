using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VitaTrack.Core.Data;
using VitaTrack.Core.Features.ServiceConnections;

namespace VitaTrack.Tests.Features.ServiceConnections;

/// <summary>
/// The registry is the single authority for what a service id may be: the
/// <c>Service</c> column stores a descriptor id, never free text, so a second
/// descriptor is a scope change and these tests are where that becomes visible.
/// </summary>
[TestClass]
public class ServiceDescriptorRegistryTests
{
    /// <summary>Pins scope. One service ships today; adding a second descriptor is a
    /// deliberate change to this assertion, not something that happens quietly.</summary>
    [TestMethod]
    public void All_ContainsExactlyOneDescriptor_ShippedToday()
    {
        var all = ServiceDescriptorRegistry.All;

        Assert.AreEqual(1, all.Count, "one service ships today; a second descriptor is a scope change");
        Assert.AreEqual("opencode", all[0].ServiceId);
    }

    [TestMethod]
    public void Variants_Are_ExactlyTheSevenDocumentedValues()
    {
        string[] expected = ["default", "none", "low", "medium", "high", "xhigh", "max"];

        // Counted as well as compared: AreEquivalent alone would accept a list that
        // repeated one of the six and dropped another, and a duplicate variant would
        // reach the model picker as two indistinguishable options.
        Assert.AreEqual(expected.Length, ServiceDescriptorRegistry.Variants.Count,
            "a repeated variant would render twice in the picker and pass a set comparison");
        CollectionAssert.AreEquivalent(expected, ServiceDescriptorRegistry.Variants.ToList());
    }

    [TestMethod]
    public void Find_KnownId_ReturnsTheShippedDescriptor()
    {
        // The positive case the negative one needs: without it, a Find that always
        // returned null would satisfy Find_UnknownId_ReturnsNull.
        var found = ServiceDescriptorRegistry.Find("opencode");

        Assert.IsNotNull(found);
        Assert.AreEqual("opencode", found!.ServiceId);
        Assert.AreSame(ServiceDescriptorRegistry.All[0], found, "Find resolves the shipped record, not a copy");
    }

    [TestMethod]
    public void Find_UnknownId_ReturnsNull()
    {
        Assert.IsNull(ServiceDescriptorRegistry.Find("not-a-service"),
            "an unrecognised service id must not resolve to a descriptor");
        Assert.IsNull(ServiceDescriptorRegistry.Find(string.Empty),
            "a blank service id is not a descriptor either");
    }

    [TestMethod]
    public void Find_IgnoresCase_BecauseTheIdComesFromAFormAndATextColumn()
    {
        // The documented behaviour, held: the id arrives typed by a user and read back
        // from a text column, so "OpenCode" has to resolve. A case-sensitive lookup
        // would report an unverified connection for a row the app itself wrote.
        Assert.IsNotNull(ServiceDescriptorRegistry.Find("OPENCODE"),
            "a service id is not case-sensitive: the same service, typed differently");
        Assert.AreSame(ServiceDescriptorRegistry.All[0], ServiceDescriptorRegistry.Find("OpEnCoDe"),
            "a differently-cased id resolves to the shipped record, not a copy of it");
    }

    /// <summary>The registry is the authority for what a valid service id is, and the
    /// save handler is what writes that column. Storing a string the registry does not
    /// know would write a row no probe could ever verify, so the two are held together
    /// by this test rather than by a comment — and it is the registry's own spelling that
    /// has to resolve, because <c>ServiceCatalogClient</c> looks the stored value up.</summary>
    [TestMethod]
    public async Task TheServiceIdTheSaveHandlerStores_ResolvesToAShippedDescriptor()
    {
        var stored = await WriteOneAndReadItBack();

        Assert.IsNotNull(ServiceDescriptorRegistry.Find(stored),
            $"the save handler stored '{stored}' into the Service column, and a row the registry does not know can never be verified");
        Assert.AreEqual(ServiceDescriptorRegistry.OpenCodeServiceId, stored);
    }

    [TestMethod]
    public void FindVariant_ResolvesEachShippedVariantToItself()
    {
        foreach (var variant in ServiceDescriptorRegistry.Variants)
            Assert.AreEqual(variant, ServiceDescriptorRegistry.FindVariant(variant),
                "the stored value is the vocabulary's own spelling, so the picker cannot invent a seventh form of one");
    }

    [TestMethod]
    public void FindVariant_CaseDoesNotMatter_BecauseTheValueComesFromAForm()
    {
        Assert.AreEqual("xhigh", ServiceDescriptorRegistry.FindVariant("XHigh"));
    }

    [TestMethod]
    public void FindVariant_UnknownOrBlank_ReturnsNull()
    {
        Assert.IsNull(ServiceDescriptorRegistry.FindVariant("turbo"),
            "a variant the providers do not accept must not be storable");
        Assert.IsNull(ServiceDescriptorRegistry.FindVariant(string.Empty));
        Assert.IsNull(ServiceDescriptorRegistry.FindVariant(null));
    }

    /// <summary>Runs the real save handler over a real repository and returns the value
    /// it stored, so this asserts the write rather than the handler's in-memory record.
    /// A hand-built <see cref="ServiceConnection"/> would prove nothing about which
    /// spelling reaches the column.</summary>
    private static async Task<string> WriteOneAndReadItBack()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        DbInit.EnsureCreated(connection);
        var repository = new ServiceConnectionRepository(connection);

        var saved = await new SaveConnectionHandler(repository).HandleAsync(new ConnectServiceRequest
        {
            BaseUrl = "https://api.example.com",
            ApiKey = "sk-test",
            Service = "OpenCode",
        });
        Assert.IsTrue(saved.Succeeded, saved.Error);

        var stored = await repository.GetActiveAsync();
        Assert.IsNotNull(stored, "the row the handler reported writing is not there");
        return stored.Service;
    }

    [TestMethod]
    public void OpenCodeDescriptor_HeaderFactory_IncludesXOpencodeSession()
    {
        var descriptor = ServiceDescriptorRegistry.Find("opencode");

        Assert.IsNotNull(descriptor);
        var headers = descriptor!.HeaderFactory();

        Assert.IsTrue(headers.ContainsKey("x-opencode-session"), "the session header is the one service-specific header");
        Assert.IsFalse(string.IsNullOrWhiteSpace(headers["x-opencode-session"]),
            "an empty session id correlates nothing; the header must carry a value");
    }

    [TestMethod]
    public void OpenCodeDescriptor_HeaderFactory_ReturnsTheSameSessionId_EveryCall()
    {
        // The header is a delegate, not a dictionary, so a per-call value is possible.
        // A fresh id per call would make the header as useless for correlation as the
        // per-request id LlmClient used to mint.
        var descriptor = ServiceDescriptorRegistry.Find("opencode")!;

        var first = descriptor.HeaderFactory()["x-opencode-session"];
        var second = descriptor.HeaderFactory()["x-opencode-session"];

        Assert.AreEqual(first, second, "one process, one session id");
    }

    [TestMethod]
    public void OpenCodeDescriptor_HeaderFactory_UsesTheRegistrySessionId()
    {
        // The one home for the value: the completion client's singleton must be this
        // same id, or the header correlates nothing across the two request paths.
        var headers = ServiceDescriptorRegistry.Find("opencode")!.HeaderFactory();

        Assert.AreEqual(ServiceDescriptorRegistry.SessionId, headers["x-opencode-session"]);
    }

    [TestMethod]
    public void OpenCodeDescriptor_DefaultBaseUrl_IsEmpty()
    {
        var descriptor = ServiceDescriptorRegistry.Find("opencode");

        Assert.IsNotNull(descriptor);
        Assert.AreEqual(string.Empty, descriptor!.DefaultBaseUrl,
            "the gateway URL is the user's to supply; assuming one would silently point the app at the wrong host");
    }
}
