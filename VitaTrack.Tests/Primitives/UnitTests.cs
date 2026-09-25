using Microsoft.VisualStudio.TestTools.UnitTesting;
using VitaTrack.Core.Primitives;

namespace VitaTrack.Tests.Primitives;

[TestClass]
public class UnitTests
{
    [TestMethod]
    public void Parse_CanonicalizesMicrogramAliases()
    {
        Assert.AreEqual(Unit.Microgram, Unit.Parse("mcg"));
        Assert.AreEqual(Unit.Microgram, Unit.Parse("ug"));
        Assert.AreEqual(Unit.Microgram, Unit.Parse("μg"));
        Assert.AreEqual(Unit.Microgram, Unit.Parse("500 µg"));
    }

    [TestMethod]
    public void Parse_CanonicalizesInternationalUnits()
    {
        Assert.AreEqual(Unit.InternationalUnit, Unit.Parse("iu"));
        Assert.AreEqual(Unit.InternationalUnit, Unit.Parse("200 IU"));
    }

    [TestMethod]
    public void Parse_RecognizesCanonicalSymbols()
    {
        Assert.AreEqual(Unit.Milligram, Unit.Parse("mg"));
        Assert.AreEqual(Unit.Gram, Unit.Parse("g"));
        Assert.AreEqual(Unit.Milliliter, Unit.Parse("ml"));
        Assert.AreEqual(Unit.Teaspoon, Unit.Parse("tsp"));
        Assert.AreEqual(Unit.Tablespoon, Unit.Parse("tbsp"));
        Assert.AreEqual(Unit.Tablespoon, Unit.Parse("TBSP"));
        Assert.AreEqual(Unit.Tablet, Unit.Parse("tab"));
    }

    [TestMethod]
    public void Parse_AcceptsEveryDocumentedSymbolAndAlias()
    {
        // Every token the A1 table recognizes, asserted by Symbol rather than by
        // Unit equality so the whole mapping is pinned in one place.
        AssertAccepts("mg", "mg");
        AssertAccepts("MG", "mg");
        AssertAccepts("µg", "µg");
        AssertAccepts("μg", "µg");
        AssertAccepts("ug", "µg");
        AssertAccepts("mcg", "µg");
        AssertAccepts("g", "g");
        AssertAccepts("G", "g");
        AssertAccepts("ml", "ml");
        AssertAccepts("mL", "ml");
        AssertAccepts("tsp", "tsp");
        AssertAccepts("TSP", "tsp");
        AssertAccepts("tbsp", "tbsp");
        AssertAccepts("TBSP", "tbsp");
        AssertAccepts("tab", "tab");
        AssertAccepts("tablet", "tab");
        AssertAccepts("tablets", "tab");
        AssertAccepts("IU", "IU");
        AssertAccepts("iu", "IU");
        AssertAccepts("Iu", "IU");
    }

    [TestMethod]
    public void Parse_RecognizedSetIsExactlyTheEightDocumentedSymbols()
    {
        var tokens = new[]
        {
            "mg", "MG", "µg", "μg", "ug", "mcg", "g", "G", "ml", "mL",
            "tsp", "TSP", "tbsp", "TBSP", "tab", "tablet", "tablets",
            "IU", "iu", "Iu"
        };

        var symbols = tokens.Select(Unit.Parse).Select(u => u.Symbol).Distinct().ToList();

        CollectionAssert.AreEquivalent(
            new[] { "mg", "µg", "g", "ml", "tsp", "tbsp", "tab", "IU" },
            symbols,
            "The recognized set is exactly the eight documented symbols. A new unit must "
            + "extend Unit.Canonicalize, this test, and the AGENTS.md dosage bullet together.");
    }

    [TestMethod]
    public void Parse_EmptyYieldsUndefined()
    {
        var unit = Unit.Parse(string.Empty);
        Assert.IsFalse(unit.IsDefined);
        Assert.AreEqual(string.Empty, unit.ToString());
    }

    [TestMethod]
    public void EqualAliases_AreEqual()
    {
        Assert.AreEqual(Unit.Parse("mcg"), Unit.Parse("µg"));
    }

    [TestMethod]
    public void Parse_RejectsUnrecognizedTokens()
    {
        Assert.IsFalse(Unit.Parse("3 capsules").IsDefined, "a count-noun is not a unit");
        Assert.IsFalse(Unit.Parse("one tablet").IsDefined, "a spelled-out number is not a unit");
        Assert.IsFalse(Unit.Parse("500 mg with food").IsDefined, "free text after the unit is not a unit");
        Assert.IsFalse(Unit.Parse("50 mg/kg").IsDefined, "a compound unit is not a single unit");
        Assert.IsFalse(Unit.Parse("20%DV").IsDefined, "a percent daily value is not a unit");
        Assert.IsFalse(Unit.Parse(string.Empty).IsDefined, "no token means no unit");
        Assert.IsFalse(Unit.Parse(null).IsDefined, "no input means no unit");
    }

    private static void AssertAccepts(string token, string expectedSymbol)
    {
        var unit = Unit.Parse(token);
        Assert.IsTrue(unit.IsDefined, $"'{token}' should be recognized");
        Assert.AreEqual(expectedSymbol, unit.Symbol, $"'{token}' should canonicalize to '{expectedSymbol}'");
    }
}
