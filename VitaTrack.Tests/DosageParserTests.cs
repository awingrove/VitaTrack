using Microsoft.VisualStudio.TestTools.UnitTesting;
using VitaTrack.Core;

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

    [TestMethod]
    public void NormalizeDosage_MicrogramAliases_AllBecomeMicroSign()
    {
        Assert.AreEqual("500µg", DosageParser.NormalizeDosage("500mcg"));
        Assert.AreEqual("1.5 µg", DosageParser.NormalizeDosage("1.5 ug"));
        Assert.AreEqual("900µg", DosageParser.NormalizeDosage("900μg"));
        Assert.AreEqual("20µg", DosageParser.NormalizeDosage("20µg"));
    }

    [TestMethod]
    public void NormalizeDosage_CaseInsensitive()
    {
        Assert.AreEqual("200IU", DosageParser.NormalizeDosage("200iu"));
        Assert.AreEqual("500mg", DosageParser.NormalizeDosage("500MG"));
    }

    [TestMethod]
    public void NormalizeDosage_PreservesSpacingStyle()
    {
        Assert.AreEqual("500µg", DosageParser.NormalizeDosage("500mcg"));
        Assert.AreEqual("1.5 µg", DosageParser.NormalizeDosage("1.5 ug"));
    }

    [TestMethod]
    public void NormalizeDosage_UnknownOrFreeText_LeftUnchanged()
    {
        Assert.AreEqual("one tablet", DosageParser.NormalizeDosage("one tablet"));
        Assert.AreEqual("500 mg with food", DosageParser.NormalizeDosage("500 mg with food"));
        Assert.AreEqual("3 tablets", DosageParser.NormalizeDosage("3 tablets"));
    }

    [TestMethod]
    public void NormalizeDosage_Blank_ReturnsEmpty()
    {
        Assert.AreEqual(string.Empty, DosageParser.NormalizeDosage(""));
        Assert.AreEqual(string.Empty, DosageParser.NormalizeDosage(null));
    }
}
