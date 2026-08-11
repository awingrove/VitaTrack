using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using VitaTrack.Infrastructure.Data;
using VitaTrack.Infrastructure.Models;
using VitaTrack.Infrastructure.Services;
using VitaTrack.Web.Controllers;

namespace VitaTrack.Tests;

[TestClass]
public class SupplementControllerTests
{
    private Mock<ISupplementRepository> _suppRepo = null!;
    private Mock<ISupplementNutrientRepository> _nutrientRepo = null!;
    private Mock<ILlmService> _llmService = null!;
    private Mock<ILogger<SupplementController>> _logger = null!;
    private SupplementController _controller = null!;

    [TestInitialize]
    public void Setup()
    {
        _suppRepo = new Mock<ISupplementRepository>();
        _nutrientRepo = new Mock<ISupplementNutrientRepository>();
        _llmService = new Mock<ILlmService>();
        _logger = new Mock<ILogger<SupplementController>>();
        _controller = new SupplementController(
            _suppRepo.Object,
            _nutrientRepo.Object,
            _llmService.Object,
            _logger.Object);
    }

    [TestMethod]
    public async Task Index_ReturnsViewWithSupplements()
    {
        var supplements = new List<Supplement>
        {
            new() { Id = 1, Name = "Vitamin C", Brand = "TestBrand", DailyDose = "500mg" },
            new() { Id = 2, Name = "Fish Oil", Brand = "OtherBrand", DailyDose = "1000mg" }
        };
        _suppRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(supplements);

        var result = await _controller.Index();

        var viewResult = result as ViewResult;
        Assert.IsNotNull(viewResult);
        var model = viewResult.Model as IEnumerable<Supplement>;
        Assert.IsNotNull(model);
        Assert.AreEqual(2, model.Count());
    }

    [TestMethod]
    public void Create_Get_ReturnsView()
    {
        var result = _controller.Create();

        Assert.IsInstanceOfType(result, typeof(ViewResult));
    }

    [TestMethod]
    public async Task Edit_Get_ReturnsViewWhenFound()
    {
        var supplement = new Supplement { Id = 5, Name = "EditMe", Brand = "Brand", DailyDose = "2 pills" };
        _suppRepo.Setup(r => r.GetByIdAsync(5)).ReturnsAsync(supplement);

        var result = await _controller.Edit(5);

        var viewResult = result as ViewResult;
        Assert.IsNotNull(viewResult);
        var model = viewResult.Model as Supplement;
        Assert.IsNotNull(model);
        Assert.AreEqual("EditMe", model.Name);
    }

    [TestMethod]
    public async Task Edit_Get_ReturnsNotFoundWhenMissing()
    {
        _suppRepo.Setup(r => r.GetByIdAsync(99)).ReturnsAsync((Supplement?)null);

        var result = await _controller.Edit(99);

        Assert.IsInstanceOfType(result, typeof(NotFoundResult));
    }

    [TestMethod]
    public async Task Edit_Post_MergesNutrientsAndRedirectsToReview()
    {
        var supplement = new Supplement { Id = 3, Name = "Edited", Brand = "Brand", DailyDose = "1 pill" };
        var existingNutrients = new List<SupplementNutrient>
        {
            new() { Id = 10, SupplementId = 3, GenericName = "Zinc", SpecificForm = "Citrate", Dosage = "15mg" }
        };
        _nutrientRepo.Setup(r => r.GetBySupplementIdAsync(3)).ReturnsAsync(existingNutrients);
        _llmService.Setup(s => s.EnrichSupplementAsync(It.IsAny<Supplement>())).ReturnsAsync(new LlmResult
        {
            NutritionJson = "{}",
            Nutrients =
            [
                new() { GenericName = "Zinc", SpecificForm = "Picolinate", Dosage = "25mg" },
                new() { GenericName = "Iron", SpecificForm = "Bisglycinate", Dosage = "18mg" }
            ]
        });

        var result = await _controller.Edit(3, supplement);

        var viewResult = result as ViewResult;
        Assert.IsNotNull(viewResult);
        Assert.AreEqual("Review", viewResult.ViewName);
    }

    [TestMethod]
    public async Task Edit_Post_ReturnsNotFoundWhenIdMismatch()
    {
        var supplement = new Supplement { Id = 99, Name = "X", Brand = "X", DailyDose = "X" };

        var result = await _controller.Edit(1, supplement);

        Assert.IsInstanceOfType(result, typeof(NotFoundResult));
    }

    [TestMethod]
    public async Task Delete_CallsRepoAndRedirectsToIndex()
    {
        _suppRepo.Setup(r => r.DeleteAsync(10)).ReturnsAsync(1);

        var result = await _controller.Delete(10);

        var redirectResult = result as RedirectToActionResult;
        Assert.IsNotNull(redirectResult);
        Assert.AreEqual("Index", redirectResult.ActionName);
        _suppRepo.Verify(r => r.DeleteAsync(10), Times.Once);
    }

    [TestMethod]
    public async Task DeleteSelected_DeletesCheckedSupplementsAndRedirects()
    {
        var ids = new List<int> { 1, 3, 5 };
        _suppRepo.Setup(r => r.DeleteAsync(ids)).ReturnsAsync(3);

        var result = await _controller.DeleteSelected(ids);

        var redirectResult = result as RedirectToActionResult;
        Assert.IsNotNull(redirectResult);
        Assert.AreEqual("Index", redirectResult.ActionName);
        _suppRepo.Verify(r => r.DeleteAsync(ids), Times.Once);
    }

    [TestMethod]
    public async Task DeleteSelected_EmptyListDoesNotCallRepo()
    {
        var result = await _controller.DeleteSelected([]);

        var redirectResult = result as RedirectToActionResult;
        Assert.IsNotNull(redirectResult);
        Assert.AreEqual("Index", redirectResult.ActionName);
        _suppRepo.Verify(r => r.DeleteAsync(It.IsAny<IEnumerable<int>>()), Times.Never);
    }
}
