using Microsoft.VisualStudio.TestTools.UnitTesting;
using VitaTrack.Core.Primitives;

namespace VitaTrack.Tests.Primitives;

[TestClass]
public class DosageTests
{
    [TestMethod]
    public void Parse_SplitsAmountAndCanonicalUnit()
    {
        var dosage = Dosage.Parse("500 mg");
        Assert.AreEqual(500m, dosage.Amount);
        Assert.AreEqual(Unit.Milligram, dosage.Unit);
    }

    [TestMethod]
    public void Parse_CanonicalizesMicrogramAliases()
    {
        Assert.AreEqual(Unit.Microgram, Dosage.Parse("2 mcg").Unit);
        Assert.AreEqual(Unit.Microgram, Dosage.Parse("2 µg").Unit);
    }

    [TestMethod]
    public void Parse_EmptyYieldsUndefined()
    {
        var dosage = Dosage.Parse(string.Empty);
        Assert.IsFalse(dosage.IsDefined);
        Assert.AreEqual("0", dosage.ToString());
    }

    [TestMethod]
    public void ToString_CombinesAmountAndUnit()
    {
        Assert.AreEqual("500 mg", Dosage.Parse("500mg").ToString());
    }
}
