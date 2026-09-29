using Microsoft.VisualStudio.TestTools.UnitTesting;
using VitaTrack.Core.Features.Nutrients;
using VitaTrack.Core.Features.Supplements;
using VitaTrack.Core.Primitives;

namespace VitaTrack.Tests;

[TestClass]
public class SupplementComparisonBuilderTests
{
    private static Supplement Sup(int id, string name, string brand = "TestBrand", string dailyDose = "1 tablet")
        => new() { Id = id, Name = name, Brand = brand, DailyDose = dailyDose };

    private static SupplementNutrient Nut(int id, int supplementId, string name,
        string form = "Form", string dosage = "10mg", int? parentId = null)
        => new()
        {
            Id = id,
            SupplementId = supplementId,
            GenericName = name,
            SpecificForm = form,
            Dosage = dosage,
            ParentNutrientId = parentId
        };

    private static ComparisonGrid Build(Supplement[] columns, params SupplementNutrient[][] nutrientsPerColumn)
    {
        var bySupplementId = new Dictionary<int, IReadOnlyList<SupplementNutrient>>();
        for (var i = 0; i < columns.Length; i++)
            bySupplementId[columns[i].Id] = nutrientsPerColumn[i];
        return SupplementComparisonBuilder.Build(columns, bySupplementId);
    }

    [TestMethod]
    public void Build_PreservesColumnInputOrder()
    {
        var first = Sup(2, "Vitamin D3", "BrandA", "1 tablet");
        var second = Sup(1, "Zinc Boost", "BrandB", "2 caps");

        var grid = Build(
            [first, second],
            [Nut(10, 2, "Vitamin D3")],
            [Nut(20, 1, "Zinc")]);

        CollectionAssert.AreEqual(
            new[] { 2, 1 },
            grid.Columns.Select(c => c.SupplementId).ToList());
        Assert.AreEqual("Vitamin D3", grid.Columns[0].Name);
        Assert.AreEqual("BrandA", grid.Columns[0].Brand);
        Assert.AreEqual("1 tablet", grid.Columns[0].DailyDose);
        Assert.AreEqual("Zinc Boost", grid.Columns[1].Name);
        Assert.AreEqual("BrandB", grid.Columns[1].Brand);
        Assert.AreEqual("2 caps", grid.Columns[1].DailyDose);
    }

    [TestMethod]
    public void Build_MergesNamesTrimmedAndCaseInsensitive()
    {
        var grid = Build(
            [Sup(1, "A"), Sup(2, "B")],
            [Nut(10, 1, "Vitamin D3", dosage: "25µg")],
            [Nut(20, 2, "vitamin d3 ", dosage: "25µg")]);

        Assert.AreEqual(1, grid.Rows.Count);
        Assert.AreEqual("Vitamin D3", grid.Rows[0].Label);
        Assert.IsFalse(grid.Rows[0].IsBlendChild);
        Assert.IsNotNull(grid.Rows[0].Cells[0]);
        Assert.IsNotNull(grid.Rows[0].Cells[1]);
    }

    [TestMethod]
    public void Build_SortsRowsAlphabetically_CaseInsensitive()
    {
        var grid = Build(
            [Sup(1, "A")],
            [
                Nut(10, 1, "zinc"),
                Nut(11, 1, "Vitamin D"),
                Nut(12, 1, "alpha")
            ]);

        CollectionAssert.AreEqual(
            new[] { "alpha", "Vitamin D", "zinc" },
            grid.Rows.Select(r => r.Label).ToList());
    }

    [TestMethod]
    public void Build_NestsChildrenUnderParent_OrphansTopLevel()
    {
        var omegaBlend = Nut(100, 1, "Omega Blend", form: "Blend", dosage: "500mg");
        var epa = Nut(101, 1, "EPA", form: "Eicosapentaenoic acid", dosage: "300mg", parentId: 100);

        var grid = Build(
            [Sup(1, "A"), Sup(2, "B")],
            [omegaBlend, epa],
            [Nut(300, 2, "DHA", form: "Docosahexaenoic acid", dosage: "200mg", parentId: 999999)]);

        // Top-level rows sort alphabetically; the resolved child sits directly
        // under its parent; the orphan (parent id absent from all data) is top-level.
        CollectionAssert.AreEqual(
            new[] { "DHA", "Omega Blend", "EPA" },
            grid.Rows.Select(r => r.Label).ToList());
        Assert.IsFalse(grid.Rows[0].IsBlendChild);   // DHA orphan
        Assert.IsFalse(grid.Rows[1].IsBlendChild);   // Omega Blend parent
        Assert.IsTrue(grid.Rows[2].IsBlendChild);    // EPA under its parent

        // EPA's cell exists only in column 1 (the column that owns the parent group).
        Assert.IsNotNull(grid.Rows[2].Cells[0]);
        Assert.IsNull(grid.Rows[2].Cells[1]);
    }

    [TestMethod]
    public void Build_ChildrenKeepDataOrder_UnderParent()
    {
        var blend = Nut(100, 1, "Blend", form: "Blend", dosage: "500mg");
        var zinc = Nut(101, 1, "Zinc", form: "Picolinate", dosage: "", parentId: 100);
        var magnesium = Nut(102, 1, "Magnesium", form: "Glycinate", dosage: "", parentId: 100);

        var grid = Build(
            [Sup(1, "A"), Sup(2, "B")],
            [blend, zinc, magnesium],
            [Nut(200, 2, "Vitamin C", dosage: "500mg")]);

        var rows = grid.Rows.Select(r => r.Label).ToList();
        var blendIndex = rows.IndexOf("Blend");
        Assert.AreEqual("Zinc", rows[blendIndex + 1]);
        Assert.AreEqual("Magnesium", rows[blendIndex + 2]);
    }

    [TestMethod]
    public void Build_NormalizesDosage_McgBecomesCanonicalMicroGram()
    {
        var grid = Build(
            [Sup(1, "A"), Sup(2, "B")],
            [Nut(10, 1, "B12", dosage: "500 mcg")],
            [Nut(20, 2, "B12", dosage: "250mcg")]);

        var row = grid.Rows.Single();
        Assert.AreEqual(Dosage.Normalize("500 mcg"), row.Cells[0]!.Dosage);
        Assert.AreEqual(Dosage.Normalize("250mcg"), row.Cells[1]!.Dosage);
        // Canonical microgram symbol, not the alias the row was written with.
        Assert.AreEqual("500 µg", row.Cells[0]!.Dosage);
        Assert.AreEqual("250µg", row.Cells[1]!.Dosage);
    }

    [TestMethod]
    public void Build_MissingNutrient_YieldsNullCell()
    {
        var grid = Build(
            [Sup(1, "A"), Sup(2, "B")],
            [Nut(10, 1, "Zinc")],
            [Nut(20, 2, "Magnesium")]);

        Assert.AreEqual(2, grid.Rows.Count);
        var zincRow = grid.Rows.Single(r => r.Label == "Zinc");
        var magnesiumRow = grid.Rows.Single(r => r.Label == "Magnesium");
        Assert.IsNotNull(zincRow.Cells[0]);
        Assert.IsNull(zincRow.Cells[1]);
        Assert.IsNull(magnesiumRow.Cells[0]);
        Assert.IsNotNull(magnesiumRow.Cells[1]);
    }

    [TestMethod]
    public void Build_EmptyDosageChild_YieldsCellWithEmptyDosage()
    {
        var blend = Nut(100, 1, "Blend", form: "Blend", dosage: "500mg");
        var child = Nut(101, 1, "Pectin", form: "Citrus", dosage: "", parentId: 100);

        var grid = Build(
            [Sup(1, "A"), Sup(2, "B")],
            [blend, child],
            []);

        var childRow = grid.Rows.Single(r => r.Label == "Pectin");
        Assert.IsTrue(childRow.IsBlendChild);
        Assert.IsNotNull(childRow.Cells[0]);
        Assert.AreEqual(string.Empty, childRow.Cells[0]!.Dosage);
        Assert.IsNull(childRow.Cells[1]);
    }
}
