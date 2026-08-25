using Microsoft.VisualStudio.TestTools.UnitTesting;
using VitaTrack.Infrastructure;

namespace VitaTrack.Tests;

[TestClass]
public class DosageParserTests
{
    [TestMethod]
    public void ParseAmount_ExtractsNumberFromMilligrams()
    {
        Assert.AreEqual(500m, DosageParser.ParseAmount("500mg"));
    }

    [TestMethod]
    public void ParseAmount_ExtractsDecimalNumber()
    {
        Assert.AreEqual(1.5m, DosageParser.ParseAmount("1.5 mg"));
    }

    [TestMethod]
    public void ParseAmount_IgnoresUnit()
    {
        Assert.AreEqual(200m, DosageParser.ParseAmount("200IU"));
    }

    [TestMethod]
    public void ParseAmount_Blank_ReturnsZero()
    {
        Assert.AreEqual(0m, DosageParser.ParseAmount(""));
        Assert.AreEqual(0m, DosageParser.ParseAmount("   "));
    }

    [TestMethod]
    public void ParseAmount_Null_ReturnsZero()
    {
        Assert.AreEqual(0m, DosageParser.ParseAmount(null));
    }

    [TestMethod]
    public void ParseAmount_NoDigits_ReturnsZero()
    {
        Assert.AreEqual(0m, DosageParser.ParseAmount("one tablet"));
    }
}
