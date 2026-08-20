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
public class SupplementControllerEditTests
{
    private Mock<ISupplementRepository> _suppRepo = null!;
    private Mock<ISupplementNutrientRepository> _nutrientRepo = null!;
    private Mock<ISupplementNutrientService> _nutrientService = null!;
    private Mock<ILlmService> _llmService = null!;
    private Mock<ICsvImportService> _csvImportService = null!;
    private SupplementController _controller = null!;

    [TestInitialize]
    public void Setup()
    {
        _suppRepo = new Mock<ISupplementRepository>();
        _nutrientRepo = new Mock<ISupplementNutrientRepository>();
        _nutrientService = new Mock<ISupplementNutrientService>();
        _llmService = new Mock<ILlmService>();
        _csvImportService = new Mock<ICsvImportService>();
        _controller = new SupplementController(
            _suppRepo.Object, _nutrientRepo.Object, _nutrientService.Object,
            _llmService.Object, _csvImportService.Object);

        var urlHelper = new Mock<IUrlHelper>();
        urlHelper.Setup(u => u.Action(It.IsAny<Microsoft.AspNetCore.Mvc.Routing.UrlActionContext>()))
                 .Returns("/Supplement/Index");
        _controller.Url = urlHelper.Object;
        _controller.ControllerContext = new ControllerContext { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext() };
    }

    private static RedirectToActionResult AssertRedirectsToIndex(IActionResult result)
    {
        var redirect = result as RedirectToActionResult;
        Assert.IsNotNull(redirect);
        Assert.AreEqual("Index", redirect.ActionName);
        return redirect;
    }

    private void SetupEnrich(LlmResult result)
        => _llmService.Setup(s => s.EnrichSupplementAsync(It.IsAny<Supplement>())).ReturnsAsync(result);

    [TestMethod]
    public async Task Edit_Get_ReturnsViewWhenFound()
    {
        var supplement = new Supplement { Id = 5, Name = "EditMe", Brand = "Brand", DailyDose = "2 pills" };
        _suppRepo.Setup(r => r.GetByIdAsync(5)).ReturnsAsync(supplement);
        _nutrientRepo.Setup(r => r.GetBySupplementIdAsync(It.IsAny<int>()))
            .ReturnsAsync(new List<SupplementNutrient>());

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
        var request = new EditSupplementRequest { Id = 3, Name = "Edited", Brand = "Brand", DailyDose = "1 pill" };
        var existingNutrients = new List<SupplementNutrient>
        {
            new() { Id = 10, SupplementId = 3, GenericName = "Zinc", SpecificForm = "Citrate", Dosage = "15mg" }
        };
        _nutrientRepo.Setup(r => r.GetBySupplementIdAsync(3)).ReturnsAsync(existingNutrients);
        SetupEnrich(new LlmResult
        {
            NutritionJson = "{}",
            Nutrients =
            [
                new() { GenericName = "Zinc", SpecificForm = "Picolinate", Dosage = "25mg" },
                new() { GenericName = "Iron", SpecificForm = "Bisglycinate", Dosage = "18mg" }
            ]
        });

        var result = await _controller.Edit(3, request);

        var viewResult = result as ViewResult;
        Assert.IsNotNull(viewResult);
        Assert.AreEqual("Review", viewResult.ViewName);
    }

    [TestMethod]
    public async Task Edit_Post_ReturnsNotFoundWhenIdMismatch()
    {
        var request = new EditSupplementRequest { Id = 99, Name = "X", Brand = "X", DailyDose = "X" };

        var result = await _controller.Edit(1, request);

        Assert.IsInstanceOfType(result, typeof(NotFoundResult));
    }

    [TestMethod]
    public async Task EditSave_PersistsAndRedirectsWithoutEnrichment()
    {
        var request = new EditSupplementRequest { Id = 7, Name = "PlainEdit", Brand = "Brand", DailyDose = "2 pills" };
        _suppRepo.Setup(r => r.GetByIdAsync(7)).ReturnsAsync(new Supplement { Id = 7, Name = "Old" });

        var result = await _controller.EditSave(7, request);

        AssertRedirectsToIndex(result);
        _suppRepo.Verify(r => r.UpdateAsync(It.IsAny<Supplement>()), Times.Once);
        _llmService.Verify(s => s.EnrichSupplementAsync(It.IsAny<Supplement>()), Times.Never);
        _nutrientService.Verify(s => s.ReplaceAsync(It.IsAny<int>(), It.IsAny<IEnumerable<SupplementNutrientDto>>()), Times.Never);
    }

    [TestMethod]
    public async Task EditSave_PreservesEnrichmentMetadata()
    {
        var request = new EditSupplementRequest { Id = 11, Name = "PlainEdit", Brand = "Brand", DailyDose = "2 pills" };
        _suppRepo.Setup(r => r.GetByIdAsync(11))
            .ReturnsAsync(new Supplement { Id = 11, Name = "Old", NutritionJson = "{}", SwapSuggestion = "X" });

        Supplement? saved = null;
        _suppRepo.Setup(r => r.UpdateAsync(It.IsAny<Supplement>()))
            .Callback<Supplement>(s => saved = s)
            .Returns(Task.CompletedTask);

        var result = await _controller.EditSave(11, request);

        AssertRedirectsToIndex(result);
        Assert.IsNotNull(saved);
        Assert.AreEqual("{}", saved!.NutritionJson);
        Assert.AreEqual("X", saved.SwapSuggestion);
    }

    [TestMethod]
    public async Task EditSave_InvalidModel_ReturnsEditView()
    {
        var request = new EditSupplementRequest { Id = 8, Name = "", Brand = "Brand", DailyDose = "1 pill" };
        _controller.ModelState.AddModelError("Name", "Name is required");
        _suppRepo.Setup(r => r.GetByIdAsync(8)).ReturnsAsync(new Supplement { Id = 8, Name = "Old" });

        var result = await _controller.EditSave(8, request);

        var viewResult = result as ViewResult;
        Assert.IsNotNull(viewResult);
        Assert.AreEqual("Edit", viewResult.ViewName);
    }

    [TestMethod]
    public async Task Edit_Post_ToDtos_MapsChildrenOntoParent()
    {
        var request = new EditSupplementRequest { Id = 3, Name = "Edited", Brand = "Brand", DailyDose = "1 pill" };
        var parent = new SupplementNutrient { Id = 10, SupplementId = 3, GenericName = "B-Complex", SpecificForm = "Capsule", Dosage = "1" };
        var child = new SupplementNutrient { Id = 11, SupplementId = 3, GenericName = "B12", SpecificForm = "Methyl", Dosage = "500mcg", ParentNutrientId = 10 };
        _nutrientRepo.Setup(r => r.GetBySupplementIdAsync(3)).ReturnsAsync(new List<SupplementNutrient> { parent });
        _nutrientRepo.Setup(r => r.GetByParentIdAsync(10)).ReturnsAsync(new List<SupplementNutrient> { child });
        SetupEnrich(new LlmResult { NutritionJson = "{}", Nutrients = [] });

        var result = await _controller.Edit(3, request);

        var viewResult = result as ViewResult;
        Assert.IsNotNull(viewResult);
        var merged = viewResult.ViewData["ExtractedNutrients"] as List<SupplementNutrientDto>;
        Assert.IsNotNull(merged);
        Assert.AreEqual(1, merged.Count);
        Assert.IsNotNull(merged[0].Children);
        Assert.AreEqual(1, merged[0].Children!.Count);
        Assert.AreEqual("B12", merged[0].Children[0].GenericName);
    }
}
