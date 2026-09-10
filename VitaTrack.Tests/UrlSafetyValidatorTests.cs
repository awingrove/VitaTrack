using Microsoft.VisualStudio.TestTools.UnitTesting;
using VitaTrack.Infrastructure.Services;

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
    public void IsUrlSafe_BlocksIpv4Loopback()
    {
        Assert.IsFalse(UrlSafetyValidator.IsUrlSafe("https://127.0.0.1/x"));
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
    public void IsUrlSafe_BlocksNonUrlInput()
    {
        Assert.IsFalse(UrlSafetyValidator.IsUrlSafe("not a url"));
    }

    [TestMethod]
    public void IsUrlSafe_BlocksEmptyInput()
    {
        Assert.IsFalse(UrlSafetyValidator.IsUrlSafe(string.Empty));
    }

    [TestMethod]
    public void IsUrlSafe_BlocksUnresolvableHost()
    {
        // RFC 6761: the .invalid TLD is guaranteed never to resolve.
        Assert.IsFalse(UrlSafetyValidator.IsUrlSafe("https://no-such-host.invalid/page"));
    }

    [TestMethod]
    public void IsUrlSafe_BlocksIpv6SiteLocalAddress()
    {
        Assert.IsFalse(UrlSafetyValidator.IsUrlSafe("https://[fec0::1]/"));
    }
}
