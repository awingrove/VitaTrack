using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Globalization;
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
    public void Parse_IsDefined_DistinguishesZeroAmountFromAbsentAmount()
    {
        Assert.IsFalse(Dosage.Parse(string.Empty).IsDefined, "nothing at all is undefined");
        Assert.IsFalse(Dosage.Parse("0").IsDefined, "a bare zero with no unit is undefined");
        Assert.IsTrue(Dosage.Parse("0mg").IsDefined, "a zero amount still has a defined unit");
        Assert.IsTrue(Dosage.Parse("500 mg").IsDefined, "amount and unit both present");
    }

    [TestMethod]
    public void Parse_CountNounIsAmountOnly_WhenNotAUnit()
    {
        var capsules = Dosage.Parse("3 capsules");
        Assert.IsTrue(capsules.IsDefined, "the amount alone still defines a dosage");
        Assert.IsFalse(capsules.Unit.IsDefined, "'capsules' is a count-noun, not a unit");

        var tablets = Dosage.Parse("3 tablets");
        Assert.IsTrue(tablets.IsDefined);
        Assert.IsTrue(tablets.Unit.IsDefined, "'tablets' is a recognized count-noun");
        Assert.AreEqual("tab", tablets.Unit.Symbol, "tablet and tablets both fold to the invariant 'tab'");
    }

    [TestMethod]
    public void TryParse_ReportsWhetherAnAmountIsPresent()
    {
        Assert.IsTrue(Dosage.TryParse("500mg", out var millis), "a milligram amount parses");
        Assert.AreEqual(500m, millis.Amount);
        Assert.IsTrue(Dosage.TryParse("1.5 mg", out var fraction), "a decimal amount parses");
        Assert.AreEqual(1.5m, fraction.Amount);
        Assert.IsTrue(Dosage.TryParse("200IU", out var units), "an amount with no spacing parses");
        Assert.AreEqual(200m, units.Amount);
        Assert.IsTrue(Dosage.TryParse("0", out var zero), "a bare zero still carries an amount");
        Assert.AreEqual(0m, zero.Amount);

        Assert.IsFalse(Dosage.TryParse("", out _), "no digits is not an amount");
        Assert.IsFalse(Dosage.TryParse("   ", out _), "whitespace is not an amount");
        Assert.IsFalse(Dosage.TryParse(null, out _), "null is not an amount");
        Assert.IsFalse(Dosage.TryParse("one tablet", out _), "a spelled-out number is not an amount");
        Assert.IsFalse(Dosage.TryParse("mg", out _), "a unit alone is not an amount");
    }

    [TestMethod]
    public void TryParse_SucceedsWithAnUndefinedUnit()
    {
        Assert.IsTrue(Dosage.TryParse("3 capsules", out var dosage),
            "an amount is present, so the caller gets a value even though the unit is not one");
        Assert.AreEqual(3m, dosage.Amount);
        Assert.IsFalse(dosage.Unit.IsDefined);
    }

    [TestMethod]
    public void TryParse_OnFailure_YieldsAnUndefinedDosage()
    {
        Dosage.TryParse("one tablet", out var dosage);
        Assert.AreEqual(0m, dosage.Amount);
        Assert.IsFalse(dosage.IsDefined, "a failed parse must not leak a half-built dosage");
    }

    [TestMethod]
    public void ToString_CombinesAmountAndUnit()
    {
        Assert.AreEqual("500 mg", Dosage.Parse("500mg").ToString());
    }

    [TestMethod]
    public void ToString_UsesInvariantDecimalSeparator()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.AreEqual("1.5 mg", new Dosage(1.5m, Unit.Milligram).ToString());
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
