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
public class SupplementControllerUpdateNutrientsTests
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
    public async Task UpdateNutrients_DelegatesToServiceAndReturnsEditor()
    {
        var supplement = new Supplement { Id = 42, Name = "TestSupp", Brand = "Brand", DailyDose = "1 pill" };
        var replaced = new ReplaceNutrientsResult(
            new List<SupplementNutrient>
            {
                new() { SupplementId = 42, GenericName = "Zinc", SpecificForm = "Citrate", Dosage = "15mg" }
            },
            new List<NutrientFailure>());
        _suppRepo.Setup(r => r.GetByIdAsync(42)).ReturnsAsync(supplement);
        _nutrientService.Setup(s => s.ReplaceAsync(42, It.IsAny<IEnumerable<SupplementNutrientDto>>()))
                        .ReturnsAsync(replaced);

        var request = new ReplaceNutrientsRequest
        {
            SupplementId = 42,
            Nutrients = new List<SupplementNutrientDto>
            {
                new() { GenericName = "Zinc", SpecificForm = "Citrate", Dosage = "15mg" }
            }
        };

        var result = await _controller.UpdateNutrients(request);

        var partialResult = result as PartialViewResult;
        Assert.IsNotNull(partialResult);
        Assert.AreEqual("_NutrientEditor", partialResult.ViewName);
        var model = partialResult.Model as SupplementEditorViewModel;
        Assert.IsNotNull(model);
        Assert.IsTrue(model.SaveSuccess);
        Assert.AreEqual(1, model.Nutrients.Count);
        Assert.AreEqual("Zinc", model.Nutrients[0].GenericName);
        _nutrientService.Verify(s => s.ReplaceAsync(42, It.IsAny<IEnumerable<SupplementNutrientDto>>()), Times.Once);
    }

    [TestMethod]
    public async Task UpdateNutrients_PartialFailure_SurfacesErrorOnEditor()
    {
        var supplement = new Supplement { Id = 50, Name = "X", Brand = "Y", DailyDose = "1" };
        var replaced = new ReplaceNutrientsResult(
            new List<SupplementNutrient>(),
            new List<NutrientFailure> { new("Iron", "constraint failed") });
        _suppRepo.Setup(r => r.GetByIdAsync(50)).ReturnsAsync(supplement);
        _nutrientService.Setup(s => s.ReplaceAsync(50, It.IsAny<IEnumerable<SupplementNutrientDto>>()))
                        .ReturnsAsync(replaced);

        var request = new ReplaceNutrientsRequest
        {
            SupplementId = 50,
            Nutrients = new List<SupplementNutrientDto> { new() { GenericName = "Iron" } }
        };

        var result = await _controller.UpdateNutrients(request);

        var partialResult = result as PartialViewResult;
        Assert.IsNotNull(partialResult);
        var model = partialResult.Model as SupplementEditorViewModel;
        Assert.IsNotNull(model);
        Assert.IsTrue(model.ExtractionError?.Contains("Iron") ?? false);
    }

    [TestMethod]
    public async Task UpdateNutrients_SupplementNotFound_ReturnsNotFound()
    {
        _suppRepo.Setup(r => r.GetByIdAsync(99)).ReturnsAsync((Supplement?)null);

        var result = await _controller.UpdateNutrients(new ReplaceNutrientsRequest { SupplementId = 99 });

        Assert.IsInstanceOfType(result, typeof(NotFoundResult));
    }
}