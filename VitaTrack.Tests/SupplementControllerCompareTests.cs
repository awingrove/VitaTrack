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

    [TestMethod]
    public async Task Compare_JunkQuery_RendersWhenTwoValidRemain()
    {
        _suppRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(Sup(1, "First"));
        _suppRepo.Setup(r => r.GetByIdAsync(2)).ReturnsAsync(Sup(2, "Second"));
        _nutrientRepo.Setup(r => r.GetBySupplementIdsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new List<SupplementNutrient>());

        var result = await _controller.Compare("abc,-1,,1,,2");

        var view = result as ViewResult;
        Assert.IsNotNull(view, "junk tokens must be dropped without throwing");
        var grid = view!.Model as ComparisonGrid;
        Assert.IsNotNull(grid);
        Assert.AreEqual(2, grid!.Columns.Count);
    }

    [TestMethod]
    public async Task Compare_MalformedAndDuplicateIds_RedirectsWhenFewerThanTwoResolve()
    {
        _suppRepo.Setup(r => r.GetByIdAsync(2)).ReturnsAsync(Sup(2, "Only One"));

        var result = await _controller.Compare("abc,2,2");

        var redirect = result as RedirectToActionResult;
        Assert.IsNotNull(redirect, "dedupe leaves one resolved id, which must redirect");
        Assert.AreEqual("Index", redirect!.ActionName);
    }

    [TestMethod]
    public async Task Compare_UnknownIdsSkipped_RendersWithSurvivors()
    {
        _suppRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(Sup(1, "Known One"));
        _suppRepo.Setup(r => r.GetByIdAsync(2)).ReturnsAsync(Sup(2, "Known Two"));
        _nutrientRepo.Setup(r => r.GetBySupplementIdsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new List<SupplementNutrient>());

        var result = await _controller.Compare("1,999999,2");

        var view = result as ViewResult;
        Assert.IsNotNull(view, "unknown id must be skipped, survivors render");
        var grid = view!.Model as ComparisonGrid;
        Assert.IsNotNull(grid);
        CollectionAssert.AreEqual(
            new[] { 1, 2 },
            grid!.Columns.Select(c => c.SupplementId).ToList());
    }

    [TestMethod]
    public async Task Compare_UnknownIdsOnly_Redirects()
    {
        var result = await _controller.Compare("999999,1000000");

        var redirect = result as RedirectToActionResult;
        Assert.IsNotNull(redirect, "no resolving id must redirect to the list");
        Assert.AreEqual("Index", redirect!.ActionName);
    }

    [TestMethod]
    public async Task Compare_BeyondCapUrl_StillRenders()
    {
        for (var id = 1; id <= 6; id++)
            _suppRepo.Setup(r => r.GetByIdAsync(id)).ReturnsAsync(Sup(id, $"Supplement {id}"));
        _nutrientRepo.Setup(r => r.GetBySupplementIdsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new List<SupplementNutrient>());

        var result = await _controller.Compare("1,2,3,4,5,6");

        var view = result as ViewResult;
        Assert.IsNotNull(view, "server accepts any count past the client-side cap of 5");
        var grid = view!.Model as ComparisonGrid;
        Assert.IsNotNull(grid);
        Assert.AreEqual(6, grid!.Columns.Count);
    }
}
