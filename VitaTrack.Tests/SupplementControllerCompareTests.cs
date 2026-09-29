using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using VitaTrack.Core.Data;
using VitaTrack.Core.Features.LlmEnrichment;
using VitaTrack.Core.Features.Nutrients;
using VitaTrack.Core.Features.Supplements;
using VitaTrack.Web.Controllers;

namespace VitaTrack.Tests;

[TestClass]
public class SupplementControllerCompareTests
{
    private Mock<ISupplementRepository> _suppRepo = null!;
    private Mock<ISupplementNutrientRepository> _nutrientRepo = null!;
    private SupplementController _controller = null!;

    [TestInitialize]
    public void Setup()
    {
        _suppRepo = new Mock<ISupplementRepository>();
        _nutrientRepo = new Mock<ISupplementNutrientRepository>();
        var nutrientService = new Mock<ISupplementNutrientService>();
        var llmService = new Mock<ILlmService>();
        var comparisonHandler = new BuildSupplementComparisonHandler(_suppRepo.Object, _nutrientRepo.Object);
        _controller = new SupplementController(
            _suppRepo.Object, _nutrientRepo.Object, nutrientService.Object,
            llmService.Object, comparisonHandler);

        var urlHelper = new Mock<IUrlHelper>();
        urlHelper.Setup(u => u.Action(It.IsAny<Microsoft.AspNetCore.Mvc.Routing.UrlActionContext>()))
                 .Returns("/Supplement/Index");
        _controller.Url = urlHelper.Object;
        _controller.ControllerContext = new ControllerContext { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext() };
    }

    private static Supplement Sup(int id, string name)
        => new() { Id = id, Name = name, Brand = "Brand", DailyDose = "1 tablet" };

    [TestMethod]
    public async Task Compare_ValidTwoIds_ReturnsViewWithGridInRequestOrder()
    {
        _suppRepo.Setup(r => r.GetByIdAsync(2)).ReturnsAsync(Sup(2, "Second"));
        _suppRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(Sup(1, "First"));
        _nutrientRepo.Setup(r => r.GetBySupplementIdsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new List<SupplementNutrient>());

        var result = await _controller.Compare("2,1");

        var view = result as ViewResult;
        Assert.IsNotNull(view);
        var grid = view!.Model as ComparisonGrid;
        Assert.IsNotNull(grid);
        CollectionAssert.AreEqual(
            new[] { 2, 1 },
            grid!.Columns.Select(c => c.SupplementId).ToList());
    }

    [TestMethod]
    public async Task Compare_FewerThanTwoValidIds_RedirectsToIndex()
    {
        _suppRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(Sup(1, "Only One"));

        foreach (var ids in new[] { "999999", null, "" })
        {
            var result = await _controller.Compare(ids);

            var redirect = result as RedirectToActionResult;
            Assert.IsNotNull(redirect, $"ids='{ids}' must redirect to the list");
            Assert.AreEqual("Index", redirect!.ActionName);
        }
    }
}
