using Microsoft.VisualStudio.TestTools.UnitTesting;
using VitaTrack.Infrastructure.Services;

namespace VitaTrack.Tests;

[TestClass]
public class UrlSafetyValidatorTests
{
    [TestMethod]
    public void IsUrlSafe_AllowsPublicHttps()
    {
        Assert.IsTrue(UrlSafetyValidator.IsUrlSafe("https://example.com/product"));
    }

    [TestMethod]
    public void IsUrlSafe_AllowsHttpsIpLiteralPublic()
    {
        Assert.IsTrue(UrlSafetyValidator.IsUrlSafe("https://93.184.216.34/"));
    }

    [TestMethod]
    public void IsUrlSafe_BlocksHttpScheme()
    {
        Assert.IsFalse(UrlSafetyValidator.IsUrlSafe("http://example.com"));
    }

    [TestMethod]
    public void IsUrlSafe_BlocksFtpScheme()
    {
        Assert.IsFalse(UrlSafetyValidator.IsUrlSafe("ftp://example.com"));
    }

    [TestMethod]
    public void IsUrlSafe_BlocksMalformedUrl()
    {
        Assert.IsFalse(UrlSafetyValidator.IsUrlSafe("not a url"));
        Assert.IsFalse(UrlSafetyValidator.IsUrlSafe(""));
        Assert.IsFalse(UrlSafetyValidator.IsUrlSafe("https://"));
    }

    [TestMethod]
    public void IsUrlSafe_BlocksLoopback()
    {
        Assert.IsFalse(UrlSafetyValidator.IsUrlSafe("https://127.0.0.1/"));
        Assert.IsFalse(UrlSafetyValidator.IsUrlSafe("https://localhost/"));
        Assert.IsFalse(UrlSafetyValidator.IsUrlSafe("https://[::1]/"));
    }

    [TestMethod]
    public void IsUrlSafe_BlocksPrivateAndReservedRanges()
    {
        Assert.IsFalse(UrlSafetyValidator.IsUrlSafe("https://10.0.0.1/"));
        Assert.IsFalse(UrlSafetyValidator.IsUrlSafe("https://172.16.0.1/"));
        Assert.IsFalse(UrlSafetyValidator.IsUrlSafe("https://192.168.0.1/"));
        Assert.IsFalse(UrlSafetyValidator.IsUrlSafe("https://169.254.169.254/")); // cloud metadata SSRF
        Assert.IsFalse(UrlSafetyValidator.IsUrlSafe("https://[fe80::1]/"));
    }

    [TestMethod]
    public void IsUrlSafe_BlocksUnresolvableHost()
    {
        Assert.IsFalse(UrlSafetyValidator.IsUrlSafe("https://no-such-host.invalid/"));
    }
}
