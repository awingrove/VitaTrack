using Microsoft.VisualStudio.TestTools.UnitTesting;
using VitaTrack.Core.Primitives;

namespace VitaTrack.Tests.Primitives;

[TestClass]
public class MoneyTests
{
    [TestMethod]
    public void Formats_Gbp_WithPoundSymbol()
    {
        Assert.AreEqual("£15.99", new Money(15.99m, "GBP").ToString());
    }

    [TestMethod]
    public void Formats_Usd_WithDollarSymbol()
    {
        Assert.AreEqual("$20.00", new Money(20m, "USD").ToString());
    }

    [TestMethod]
    public void Formats_UnknownCurrency_WithCodeSuffix()
    {
        Assert.AreEqual("5.00 XYZ", new Money(5m, "XYZ").ToString());
    }

    [TestMethod]
    public void Addition_SumsAmount_KeepingCurrency()
    {
        var sum = new Money(10m, "GBP") + new Money(5.50m, "GBP");
        Assert.AreEqual(new Money(15.50m, "GBP"), sum);
    }

    [TestMethod]
    public void Default_IsUndefined()
    {
        Assert.IsFalse(default(Money).IsDefined);
    }
}
