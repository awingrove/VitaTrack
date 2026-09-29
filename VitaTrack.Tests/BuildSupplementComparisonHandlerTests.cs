using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using VitaTrack.Core.Features.Nutrients;
using VitaTrack.Core.Features.Supplements;

namespace VitaTrack.Tests;

[TestClass]
public class BuildSupplementComparisonHandlerTests
{
    private Mock<ISupplementRepository> _suppRepo = null!;
    private Mock<ISupplementNutrientRepository> _nutrientRepo = null!;
    private BuildSupplementComparisonHandler _handler = null!;

    [TestInitialize]
    public void Setup()
    {
        _suppRepo = new Mock<ISupplementRepository>();
        _nutrientRepo = new Mock<ISupplementNutrientRepository>();
        _handler = new BuildSupplementComparisonHandler(_suppRepo.Object, _nutrientRepo.Object);
    }

    private static Supplement Sup(int id, string name)
        => new() { Id = id, Name = name, Brand = "Brand", DailyDose = "1 tablet" };

    private void Resolves(params Supplement[] supplements)
    {
        foreach (var supplement in supplements)
            _suppRepo.Setup(r => r.GetByIdAsync(supplement.Id)).ReturnsAsync(supplement);
    }

    private void Nutrients(params SupplementNutrient[] rows)
        => _nutrientRepo.Setup(r => r.GetBySupplementIdsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(rows.ToList());

    [TestMethod]
    public async Task BuildAsync_FewerThanTwoResolve_ReturnsNull()
    {
        Resolves(Sup(1, "Only One"));

        Assert.IsNull(await _handler.BuildAsync([1]));
        Assert.IsNull(await _handler.BuildAsync([999999, 999998]));
        Assert.IsNull(await _handler.BuildAsync([]));
    }

    [TestMethod]
    public async Task BuildAsync_PreservesRequestedOrder()
    {
        Resolves(Sup(1, "Alpha"), Sup(2, "Beta"));
        Nutrients();

        var grid = await _handler.BuildAsync([2, 1]);

        Assert.IsNotNull(grid);
        CollectionAssert.AreEqual(
            new[] { 2, 1 },
            grid!.Columns.Select(c => c.SupplementId).ToList());
    }

    [TestMethod]
    public async Task BuildAsync_DropsUnknownIds_RendersSurvivors()
    {
        Resolves(Sup(1, "Known One"), Sup(2, "Known Two"));
        Nutrients();

        var grid = await _handler.BuildAsync([1, 999999, 2]);

        Assert.IsNotNull(grid);
        CollectionAssert.AreEqual(
            new[] { 1, 2 },
            grid!.Columns.Select(c => c.SupplementId).ToList());
        // The nutrient fetch sees only the surviving ids.
        _nutrientRepo.Verify(
            r => r.GetBySupplementIdsAsync(It.Is<IEnumerable<int>>(ids => ids.SequenceEqual(new[] { 1, 2 }))),
            Times.Once);
    }

    [TestMethod]
    public async Task BuildAsync_PassesNutrientsThrough()
    {
        Resolves(Sup(1, "Alpha"), Sup(2, "Beta"));
        Nutrients(new SupplementNutrient
        {
            Id = 10,
            SupplementId = 1,
            GenericName = "Zinc",
            SpecificForm = "Picolinate",
            Dosage = "5mg"
        });

        var grid = await _handler.BuildAsync([1, 2]);

        Assert.IsNotNull(grid);
        var row = grid!.Rows.Single(r => r.Label == "Zinc");
        Assert.IsNotNull(row.Cells[0]);
        Assert.AreEqual("Picolinate", row.Cells[0]!.SpecificForm);
        Assert.AreEqual("5mg", row.Cells[0]!.Dosage);
        Assert.IsNull(row.Cells[1]);
    }
}
