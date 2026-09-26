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
    public void Normalize_CanonicalizesMicrogramAliasesToMicroSign()
    {
        Assert.AreEqual("500µg", Dosage.Normalize("500mcg"), "no space is invented");
        Assert.AreEqual("1.5 µg", Dosage.Normalize("1.5 ug"));
        Assert.AreEqual("900µg", Dosage.Normalize("900μg"));
        Assert.AreEqual("20µg", Dosage.Normalize("20µg"), "an already-canonical unit is untouched");
    }

    [TestMethod]
    public void Normalize_IsCaseInsensitive()
    {
        Assert.AreEqual("200IU", Dosage.Normalize("200iu"));
        Assert.AreEqual("500mg", Dosage.Normalize("500MG"));
    }

    [TestMethod]
    public void Normalize_PreservesSpacingStyle()
    {
        Assert.AreEqual("500µg", Dosage.Normalize("500mcg"));
        Assert.AreEqual("1.5 µg", Dosage.Normalize("1.5 ug"));
    }

    [TestMethod]
    public void Normalize_LeavesUnrecognizedAndFreeTextUnchanged()
    {
        Assert.AreEqual("one tablet", Dosage.Normalize("one tablet"), "no amount to anchor a rebuild");
        Assert.AreEqual("500 mg with food", Dosage.Normalize("500 mg with food"), "free text is not a unit");
        Assert.AreEqual("50 mg/kg", Dosage.Normalize("50 mg/kg"), "a compound unit is left alone");
        Assert.AreEqual("2 x 500mg", Dosage.Normalize("2 x 500mg"), "the shape regex is anchored, so this never matches");
    }

    [TestMethod]
    public void Normalize_CanonicalizesCountNounsToTheInvariantTab()
    {
        Assert.AreEqual("3 tab", Dosage.Normalize("3 tablets"),
            "'tablets' is now a recognized alias, so it canonicalizes like any other alias");
    }

    [TestMethod]
    public void Normalize_Blank_ReturnsEmpty()
    {
        Assert.AreEqual(string.Empty, Dosage.Normalize(""));
        Assert.AreEqual(string.Empty, Dosage.Normalize(null));
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

    /// <summary>
    /// A stored amount is always written with a "." separator, so reading one back must not
    /// depend on the ambient locale. Under de-DE a bare TryParse reads "1.5" as fifteen,
    /// because "." is that locale's group separator and NumberStyles allows thousands. This
    /// asserts the whole Parse path, not just the formatter, because the corruption happens
    /// on the way in.
    /// </summary>
    [TestMethod]
    public void Parse_ReadsAStoredDecimalAmount_UnderAnyCulture()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            var dosage = Dosage.Parse("1.5 mg");

            Assert.AreEqual(1.5m, dosage.Amount);
            Assert.AreEqual("1.5 mg", dosage.ToString());
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [TestMethod]
    public void IsWellFormed_AcceptsBlankInput()
    {
        // A blank dosage is legal: a blend child carries no dosage of its own. The
        // "required" rules live with the surfaces that impose them, not here.
        Assert.IsTrue(Dosage.IsWellFormed(null), "null is not malformed");
        Assert.IsTrue(Dosage.IsWellFormed(string.Empty), "an empty dosage is not malformed");
        Assert.IsTrue(Dosage.IsWellFormed("   "), "whitespace is not malformed");
    }

    [TestMethod]
    public void IsWellFormed_RejectsInputWithNoAmount()
    {
        Assert.IsFalse(Dosage.IsWellFormed("one tablet"), "a spelled-out number is not an amount");
        Assert.IsFalse(Dosage.IsWellFormed("abc"), "no digits and no meaning is not a dosage");
    }

    [TestMethod]
    public void IsWellFormed_RejectsAnUnrecognizedUnitToken()
    {
        Assert.IsFalse(Dosage.IsWellFormed("3 capsules"), "a count-noun is not a unit");
        Assert.IsFalse(Dosage.IsWellFormed("500 mg with food"), "free text after the unit is not a unit");
        Assert.IsFalse(Dosage.IsWellFormed("5mg x2"), "a multiplier is not part of a dosage");
        Assert.IsFalse(Dosage.IsWellFormed("50 mg/kg"), "a compound unit is not a single unit");
        Assert.IsFalse(Dosage.IsWellFormed("20%DV"), "a percent daily value is not a unit");
    }

    [TestMethod]
    public void IsWellFormed_AcceptsAmountOnly()
    {
        // A bare number stays legal on purpose: the defect this closes is a *wrong* unit,
        // not a missing one, and a bare number is not reinterpreted as tablets.
        Assert.IsTrue(Dosage.IsWellFormed("500"), "an amount with no unit is still a dosage");
        Assert.IsTrue(Dosage.IsWellFormed("1.5"), "a decimal amount with no unit is still a dosage");
        Assert.IsTrue(Dosage.IsWellFormed("1"), "the amount alone is a dosage, not one tablet");
    }

    [TestMethod]
    public void IsWellFormed_AcceptsARecognizedUnit()
    {
        Assert.IsTrue(Dosage.IsWellFormed("0mg"), "a zero amount still has a defined unit");
        Assert.IsTrue(Dosage.IsWellFormed("500 mg"), "spaced and unspaced are both fine");
        Assert.IsTrue(Dosage.IsWellFormed("500mg"));
        Assert.IsTrue(Dosage.IsWellFormed("200IU"));
        Assert.IsTrue(Dosage.IsWellFormed("900µg"));
        Assert.IsTrue(Dosage.IsWellFormed("1 tbsp"), "an alias is as good as its canonical symbol");
        Assert.IsTrue(Dosage.IsWellFormed("500mcg"), "an alias is as good as its canonical symbol");
        Assert.IsTrue(Dosage.IsWellFormed("1 tablet"), "'tablet' canonicalizes to 'tab'; it is 'one tablet' that fails, on the missing amount");
        Assert.IsTrue(Dosage.IsWellFormed("3 tablets"));
    }

    [TestMethod]
    public void TryParse_ReadsAStoredDecimalAmount_UnderAnyCulture()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            Assert.IsTrue(Dosage.TryParse("0.75 g", out var dosage));
            Assert.AreEqual(0.75m, dosage.Amount);
            Assert.AreEqual(Unit.Gram, dosage.Unit);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
