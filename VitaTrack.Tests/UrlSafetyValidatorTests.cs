using Microsoft.VisualStudio.TestTools.UnitTesting;
using VitaTrack.Core.Features.LlmEnrichment;

namespace VitaTrack.Tests;

[TestClass]
public class UrlSafetyValidatorTests
{
    [TestMethod]
    public void IsUrlSafe_AllowsPublicIpLiteralOverHttps()
    {
        Assert.IsTrue(UrlSafetyValidator.IsUrlSafe("https://8.8.8.8/x"));
    }

    [TestMethod]
    public void IsUrlSafe_AllowsPublicHttps()
    {
        Assert.IsTrue(UrlSafetyValidator.IsUrlSafe("https://example.com/product"));
    }

    [TestMethod]
    public void IsUrlSafe_BlocksIpv4Loopback()
    {
        Assert.IsFalse(UrlSafetyValidator.IsUrlSafe("https://127.0.0.1/x"));
    }

    [TestMethod]
    public void IsUrlSafe_BlocksLoopbackHostnames()
    {
        Assert.IsFalse(UrlSafetyValidator.IsUrlSafe("https://localhost/"));
    }

    [TestMethod]
    public void IsUrlSafe_BlocksIpv6Loopback()
    {
        Assert.IsFalse(UrlSafetyValidator.IsUrlSafe("https://[::1]/"));
    }

    [TestMethod]
    public void IsUrlSafe_BlocksPrivateRange10()
    {
        Assert.IsFalse(UrlSafetyValidator.IsUrlSafe("https://10.0.0.5/x"));
    }

    [TestMethod]
    public void IsUrlSafe_BlocksPrivateRange17216()
    {
        Assert.IsFalse(UrlSafetyValidator.IsUrlSafe("https://172.16.0.1/x"));
    }

    [TestMethod]
    public void IsUrlSafe_BlocksPrivateRange192168()
    {
        Assert.IsFalse(UrlSafetyValidator.IsUrlSafe("https://192.168.1.10/x"));
    }

    [TestMethod]
    public void IsUrlSafe_BlocksLinkLocalMetadataAddress()
    {
        Assert.IsFalse(UrlSafetyValidator.IsUrlSafe("https://169.254.169.254/x"));
    }

    [TestMethod]
    public void IsUrlSafe_BlocksNonHttpsScheme()
    {
        Assert.IsFalse(UrlSafetyValidator.IsUrlSafe("http://8.8.8.8/x"));
    }

    [TestMethod]
    public void IsUrlSafe_BlocksNonHttpsSchemes()
    {
        Assert.IsFalse(UrlSafetyValidator.IsUrlSafe("http://example.com"));
        Assert.IsFalse(UrlSafetyValidator.IsUrlSafe("ftp://example.com"));
    }

    [TestMethod]
    public void IsUrlSafe_BlocksNonUrlInput()
    {
        Assert.IsFalse(UrlSafetyValidator.IsUrlSafe("not a url"));
        Assert.IsFalse(UrlSafetyValidator.IsUrlSafe("https://"));
    }

    [TestMethod]
    public void IsUrlSafe_BlocksEmptyInput()
    {
        Assert.IsFalse(UrlSafetyValidator.IsUrlSafe(string.Empty));
    }

    [TestMethod]
    public void IsUrlSafe_BlocksHost_WhenResolutionFails()
    {
        // Inject a failing resolver — the DNS-failure branch must block without network I/O.
        Assert.IsFalse(UrlSafetyValidator.IsUrlSafe(
            "https://unresolvable.example/page",
            _ => throw new System.Net.Sockets.SocketException()));
    }

    [TestMethod]
    public void IsUrlSafe_ResolverSeam_HonoursResolvedAddresses()
    {
        Assert.IsFalse(UrlSafetyValidator.IsUrlSafe(
            "https://internal-host",
            _ => [System.Net.IPAddress.Parse("10.1.2.3")]));
        Assert.IsTrue(UrlSafetyValidator.IsUrlSafe(
            "https://public-host",
            _ => [System.Net.IPAddress.Parse("8.8.4.4")]));
    }

    [TestMethod]
    public void IsUrlSafe_BlocksIpv6SiteLocalAddress()
    {
        Assert.IsFalse(UrlSafetyValidator.IsUrlSafe("https://[fec0::1]/"));
    }
}
