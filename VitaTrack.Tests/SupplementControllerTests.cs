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

    private static PartialViewResult AssertPartialView(IActionResult result, string viewName)
    {
        var partial = result as PartialViewResult;
        Assert.IsNotNull(partial);
        Assert.AreEqual(viewName, partial.ViewName);
        return partial;
    }

    private void SetupEnrich(LlmResult result)
        => _llmService.Setup(s => s.EnrichSupplementAsync(It.IsAny<Supplement>())).ReturnsAsync(result);

    private void SetupAddAsync(int id)
        => _suppRepo.Setup(r => r.AddAsync(It.IsAny<Supplement>())).ReturnsAsync(id);

    private void SetupPersistHierarchy(int id, ReplaceNutrientsResult result)
        => _nutrientService.Setup(s => s.PersistHierarchyAsync(id, It.IsAny<IEnumerable<SupplementNutrientDto>>())).ReturnsAsync(result);

    [TestMethod]
    public async Task Index_ReturnsViewWithSupplements()
    {
        var supplements = new List<Supplement>
        {
            new() { Id = 1, Name = "Vitamin C", Brand = "TestBrand", DailyDose = "500mg" },
            new() { Id = 2, Name = "Fish Oil", Brand = "OtherBrand", DailyDose = "1000mg" }
        };
        _suppRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(supplements);
        _nutrientRepo.Setup(r => r.GetCountsBySupplementIdsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new Dictionary<int, int>());

        var result = await _controller.Index();

        var viewResult = result as ViewResult;
        Assert.IsNotNull(viewResult);
        var model = viewResult.Model as IEnumerable<Supplement>;
        Assert.IsNotNull(model);
        Assert.AreEqual(2, model.Count());
    }

    [TestMethod]
    public void Create_Get_ReturnsView()
        => Assert.IsInstanceOfType(_controller.Create(), typeof(ViewResult));

    [TestMethod]
    public async Task Delete_CallsRepoAndRedirectsToIndex()
    {
        _suppRepo.Setup(r => r.DeleteAsync(10)).ReturnsAsync(1);

        var result = await _controller.Delete(10);

        AssertRedirectsToIndex(result);
        _suppRepo.Verify(r => r.DeleteAsync(10), Times.Once);
    }

    [TestMethod]
    public async Task DeleteSelected_DeletesCheckedSupplementsAndRedirects()
    {
        var ids = new List<int> { 1, 3, 5 };
        _suppRepo.Setup(r => r.DeleteAsync(ids)).ReturnsAsync(3);

        var result = await _controller.DeleteSelected(ids);

        AssertRedirectsToIndex(result);
        _suppRepo.Verify(r => r.DeleteAsync(ids), Times.Once);
    }

    [TestMethod]
    public async Task DeleteSelected_EmptyListDoesNotCallRepo()
    {
        var result = await _controller.DeleteSelected([]);

        AssertRedirectsToIndex(result);
        _suppRepo.Verify(r => r.DeleteAsync(It.IsAny<IEnumerable<int>>()), Times.Never);
    }

    [TestMethod]
    public async Task Enrich_WithUrl_SavesAndReturnsNutrientEditor()
    {
        var request = new CreateSupplementRequest { Name = "TestSupp", Brand = "Brand", DailyDose = "1 pill", ManufacturerUrl = "https://example.com" };
        var llmResult = new LlmResult
        {
            NutritionJson = "{}",
            SwapSuggestion = "Try X",
            Nutrients = [new() { GenericName = "Vitamin C", SpecificForm = "Ascorbic Acid", Dosage = "500mg" }]
        };
        var persisted = new ReplaceNutrientsResult(
            [new SupplementNutrient { SupplementId = 42, GenericName = "Vitamin C", SpecificForm = "Ascorbic Acid", Dosage = "500mg" }],
            []);
        SetupEnrich(llmResult);
        SetupAddAsync(42);
        SetupPersistHierarchy(42, persisted);

        var result = await _controller.Enrich(request);

        var model = AssertPartialView(result, "_NutrientEditor").Model as SupplementEditorViewModel;
        Assert.IsNotNull(model);
        Assert.AreEqual(42, model.SupplementId);
        Assert.AreEqual("TestSupp", model.SupplementName);
        Assert.AreEqual(1, model.Nutrients.Count);
        Assert.AreEqual("Vitamin C", model.Nutrients[0].GenericName);
        _suppRepo.Verify(r => r.AddAsync(It.IsAny<Supplement>()), Times.Once);
        _nutrientService.Verify(s => s.PersistHierarchyAsync(42, It.IsAny<IEnumerable<SupplementNutrientDto>>()), Times.Once);
    }

    [TestMethod]
    public async Task Enrich_WithoutUrl_SavesAndReturnsEmptyEditor()
    {
        var request = new CreateSupplementRequest { Name = "TestSupp", Brand = "Brand", DailyDose = "1 pill" };
        SetupEnrich(new LlmResult());
        SetupAddAsync(43);
        SetupPersistHierarchy(43, new ReplaceNutrientsResult([], []));

        var result = await _controller.Enrich(request);

        var model = AssertPartialView(result, "_NutrientEditor").Model as SupplementEditorViewModel;
        Assert.IsNotNull(model);
        Assert.AreEqual(43, model.SupplementId);
        Assert.AreEqual(0, model.Nutrients.Count);
    }

    [TestMethod]
    public async Task Enrich_LlmReturnsError_SavesAndReturnsEditorWithError()
    {
        var request = new CreateSupplementRequest { Name = "TestSupp", Brand = "Brand", DailyDose = "1 pill", ManufacturerUrl = "https://example.com" };
        SetupEnrich(new LlmResult { ExtractionError = "Could not reach enrichment service. You can add nutrients manually." });
        SetupAddAsync(44);
        SetupPersistHierarchy(44, new ReplaceNutrientsResult([], []));

        var result = await _controller.Enrich(request);

        var model = AssertPartialView(result, "_NutrientEditor").Model as SupplementEditorViewModel;
        Assert.IsNotNull(model);
        Assert.IsNotNull(model.ExtractionError);
        Assert.IsTrue(model.ExtractionError.Contains("manually"));
        _suppRepo.Verify(r => r.AddAsync(It.IsAny<Supplement>()), Times.Once);
    }

    [TestMethod]
    public async Task Enrich_InvalidModel_ReturnsValidationErrors()
    {
        _controller.ModelState.AddModelError("Name", "Name is required");
        var request = new CreateSupplementRequest { Brand = "Brand", DailyDose = "1 pill" };

        var result = await _controller.Enrich(request);

        AssertPartialView(result, "_ValidationErrors");
    }

    [TestMethod]
    public async Task Enrich_ReturnsEditorWithBlendChildren()
    {
        var request = new CreateSupplementRequest { Name = "TestSupp", Brand = "Brand", DailyDose = "1 pill", ManufacturerUrl = "https://example.com" };
        var llmResult = new LlmResult
        {
            NutritionJson = "{}",
            Nutrients =
            [
                new() { GenericName = "B-Complex", SpecificForm = "Capsule", Dosage = "1", Children = [ new() { GenericName = "B12", SpecificForm = "Methyl", Dosage = "500mcg" } ] }
            ]
        };
        var parent = new SupplementNutrient { Id = 777, SupplementId = 42, GenericName = "B-Complex", SpecificForm = "Capsule", Dosage = "1" };
        var child = new SupplementNutrient { SupplementId = 42, GenericName = "B12", SpecificForm = "Methyl", Dosage = "500mcg", ParentNutrientId = 777 };
        var persisted = new ReplaceNutrientsResult(new List<SupplementNutrient> { parent, child }, []);
        SetupEnrich(llmResult);
        SetupAddAsync(42);
        SetupPersistHierarchy(42, persisted);
        _nutrientRepo.Setup(r => r.GetByParentIdAsync(777)).ReturnsAsync(new List<SupplementNutrient> { child });

        var result = await _controller.Enrich(request);

        var model = AssertPartialView(result, "_NutrientEditor").Model as SupplementEditorViewModel;
        Assert.IsNotNull(model);
        Assert.AreEqual(1, model.Nutrients.Count);
        Assert.IsNotNull(model.Nutrients[0].Children);
        Assert.AreEqual(1, model.Nutrients[0].Children!.Count);
        Assert.AreEqual("B12", model.Nutrients[0].Children[0].GenericName);
    }

    [TestMethod]
    public async Task CreateSave_PersistsAndRedirectsWithoutEnrichment()
    {
        var request = new CreateSupplementRequest
        {
            Name = "PlainSupp",
            Brand = "Brand",
            DailyDose = "1 pill",
            ManufacturerUrl = "https://example.com"
        };
        var result = await _controller.CreateSave(request);

        Assert.IsInstanceOfType(result, typeof(EmptyResult));
        Assert.AreEqual("/Supplement/Index", _controller.Response.Headers["HX-Redirect"].ToString());
        _suppRepo.Verify(r => r.AddAsync(It.IsAny<Supplement>()), Times.Once);
        _llmService.Verify(s => s.EnrichSupplementAsync(It.IsAny<Supplement>()), Times.Never);
        _nutrientService.Verify(s => s.AddAsync(It.IsAny<int>(), It.IsAny<IEnumerable<SupplementNutrientDto>>()), Times.Never);
    }

    [TestMethod]
    public async Task CreateSave_InvalidModel_ReturnsValidationErrors()
    {
        _controller.ModelState.AddModelError("Name", "Name is required");
        var request = new CreateSupplementRequest { Brand = "Brand", DailyDose = "1 pill" };

        var result = await _controller.CreateSave(request);

        AssertPartialView(result, "_ValidationErrors");
    }
}
