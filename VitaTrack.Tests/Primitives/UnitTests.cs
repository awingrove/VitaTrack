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
    public void Parse_PassesThroughKnownUnits()
    {
        Assert.AreEqual(Unit.Milligram, Unit.Parse("mg"));
        Assert.AreEqual(Unit.Gram, Unit.Parse("g"));
        Assert.AreEqual(Unit.Milliliter, Unit.Parse("ml"));
        Assert.AreEqual(Unit.Teaspoon, Unit.Parse("tsp"));
    }

    [TestMethod]
    public void Parse_EmptyYieldsUndefined()
    {
        var unit = Unit.Parse(string.Empty);
        Assert.IsFalse(unit.IsDefined);
        Assert.AreEqual(string.Empty, unit.ToString());
    }

    [TestMethod]
    public void Parse_UnknownUnitPassesThrough()
    {
        var unit = Unit.Parse("nonsense");
        Assert.AreEqual("nonsense", unit.Symbol);
    }

    [TestMethod]
    public void EqualAliases_AreEqual()
    {
        Assert.AreEqual(Unit.Parse("mcg"), Unit.Parse("µg"));
    }
}
