using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using VitaTrack.Infrastructure.Data;
using VitaTrack.Infrastructure.Models;
using VitaTrack.Infrastructure.Services;
using VitaTrack.Web.Controllers;
using VitaTrack.Web.Models;

namespace VitaTrack.Tests;

[TestClass]
public class SupplementControllerTests
{
    private Mock<ISupplementRepository> _suppRepo = null!;
    private Mock<ISupplementNutrientRepository> _nutrientRepo = null!;
    private Mock<ISupplementNutrientService> _nutrientService = null!;
    private Mock<ILlmService> _llmService = null!;
    private SupplementController _controller = null!;

    [TestInitialize]
    public void Setup()
    {
        _suppRepo = new Mock<ISupplementRepository>();
        _nutrientRepo = new Mock<ISupplementNutrientRepository>();
        _nutrientService = new Mock<ISupplementNutrientService>();
        _llmService = new Mock<ILlmService>();
        _controller = new SupplementController(
            _suppRepo.Object,
            _nutrientRepo.Object,
            _nutrientService.Object,
            _llmService.Object);
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

    [TestMethod]
    public async Task Enrich_WithUrl_SavesAndReturnsNutrientEditor()
    {
        var supplement = new Supplement { Name = "TestSupp", Brand = "Brand", DailyDose = "1 pill", ManufacturerUrl = "https://example.com" };
        var llmResult = new LlmResult
        {
            NutritionJson = "{}",
            SwapSuggestion = "Try X",
            Nutrients =
            [
                new() { GenericName = "Vitamin C", SpecificForm = "Ascorbic Acid", Dosage = "500mg" }
            ]
        };
        var persisted = new ReplaceNutrientsResult(
            new List<SupplementNutrient>
            {
                new() { SupplementId = 42, GenericName = "Vitamin C", SpecificForm = "Ascorbic Acid", Dosage = "500mg" }
            },
            new List<NutrientFailure>());
        _llmService.Setup(s => s.EnrichSupplementAsync(It.IsAny<Supplement>())).ReturnsAsync(llmResult);
        _suppRepo.Setup(r => r.AddAsync(It.IsAny<Supplement>())).ReturnsAsync(42);
        _nutrientService.Setup(s => s.AddAsync(42, It.IsAny<IEnumerable<SupplementNutrientDto>>()))
                        .ReturnsAsync(persisted);

        var result = await _controller.Enrich(supplement);

        var partialResult = result as PartialViewResult;
        Assert.IsNotNull(partialResult);
        Assert.AreEqual("_NutrientEditor", partialResult.ViewName);
        var model = partialResult.Model as SupplementEditorViewModel;
        Assert.IsNotNull(model);
        Assert.AreEqual(42, model.SupplementId);
        Assert.AreEqual("TestSupp", model.SupplementName);
        Assert.AreEqual(1, model.Nutrients.Count);
        Assert.AreEqual("Vitamin C", model.Nutrients[0].GenericName);
        _suppRepo.Verify(r => r.AddAsync(It.IsAny<Supplement>()), Times.Once);
        _nutrientService.Verify(s => s.AddAsync(42, It.IsAny<IEnumerable<SupplementNutrientDto>>()), Times.Once);
    }

    [TestMethod]
    public async Task Enrich_WithoutUrl_SavesAndReturnsEmptyEditor()
    {
        var supplement = new Supplement { Name = "TestSupp", Brand = "Brand", DailyDose = "1 pill" };
        _llmService.Setup(s => s.EnrichSupplementAsync(It.IsAny<Supplement>())).ReturnsAsync(new LlmResult());
        _suppRepo.Setup(r => r.AddAsync(It.IsAny<Supplement>())).ReturnsAsync(43);
        _nutrientService.Setup(s => s.AddAsync(43, It.IsAny<IEnumerable<SupplementNutrientDto>>()))
                        .ReturnsAsync(new ReplaceNutrientsResult(new List<SupplementNutrient>(), new List<NutrientFailure>()));

        var result = await _controller.Enrich(supplement);

        var partialResult = result as PartialViewResult;
        Assert.IsNotNull(partialResult);
        Assert.AreEqual("_NutrientEditor", partialResult.ViewName);
        var model = partialResult.Model as SupplementEditorViewModel;
        Assert.IsNotNull(model);
        Assert.AreEqual(43, model.SupplementId);
        Assert.AreEqual(0, model.Nutrients.Count);
    }

    [TestMethod]
    public async Task Enrich_LlmReturnsError_SavesAndReturnsEditorWithError()
    {
        var supplement = new Supplement { Name = "TestSupp", Brand = "Brand", DailyDose = "1 pill", ManufacturerUrl = "https://example.com" };
        _llmService.Setup(s => s.EnrichSupplementAsync(It.IsAny<Supplement>()))
                   .ReturnsAsync(new LlmResult { ExtractionError = "Could not reach enrichment service. You can add nutrients manually." });
        _suppRepo.Setup(r => r.AddAsync(It.IsAny<Supplement>())).ReturnsAsync(44);
        _nutrientService.Setup(s => s.AddAsync(44, It.IsAny<IEnumerable<SupplementNutrientDto>>()))
                        .ReturnsAsync(new ReplaceNutrientsResult(new List<SupplementNutrient>(), new List<NutrientFailure>()));

        var result = await _controller.Enrich(supplement);

        var partialResult = result as PartialViewResult;
        Assert.IsNotNull(partialResult);
        var model = partialResult.Model as SupplementEditorViewModel;
        Assert.IsNotNull(model);
        Assert.IsNotNull(model.ExtractionError);
        Assert.IsTrue(model.ExtractionError.Contains("manually"));
        _suppRepo.Verify(r => r.AddAsync(It.IsAny<Supplement>()), Times.Once);
    }

    [TestMethod]
    public async Task Enrich_InvalidModel_ReturnsValidationErrors()
    {
        _controller.ModelState.AddModelError("Name", "Name is required");
        var supplement = new Supplement { Brand = "Brand", DailyDose = "1 pill" };

        var result = await _controller.Enrich(supplement);

        var partialResult = result as PartialViewResult;
        Assert.IsNotNull(partialResult);
        Assert.AreEqual("_ValidationErrors", partialResult.ViewName);
    }
}
