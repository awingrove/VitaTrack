using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
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
    public void Variants_Are_ExactlyTheSixDocumentedValues()
    {
        string[] expected = ["none", "low", "medium", "high", "xhigh", "max"];

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
