using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using VitaTrack.Infrastructure.Data;
using VitaTrack.Infrastructure.Models;
using VitaTrack.Web.Controllers;

namespace VitaTrack.Tests;

[TestClass]
public class SupplementNutrientControllerTests
{
    private Mock<ISupplementNutrientRepository> _nutrientRepo = null!;
    private Mock<ISupplementRepository> _supplementRepo = null!;
    private SupplementNutrientController _controller = null!;

    [TestInitialize]
    public void Setup()
    {
        _nutrientRepo = new Mock<ISupplementNutrientRepository>();
        _supplementRepo = new Mock<ISupplementRepository>();
        _controller = new SupplementNutrientController(
            _nutrientRepo.Object,
            _supplementRepo.Object);
    }

    [TestMethod]
    public async Task DeleteSelected_DeletesCheckedNutrientsAndRedirects()
    {
        var ids = new List<int> { 1, 2 };
        const int supplementId = 10;
        _nutrientRepo.Setup(r => r.DeleteAsync(ids)).ReturnsAsync(2);

        var result = await _controller.DeleteSelected(ids, supplementId);

        var redirectResult = result as RedirectToActionResult;
        Assert.IsNotNull(redirectResult);
        Assert.AreEqual("Index", redirectResult.ActionName);
        Assert.AreEqual(supplementId, redirectResult.RouteValues!["supplementId"]);
        _nutrientRepo.Verify(r => r.DeleteAsync(ids), Times.Once);
    }

    [TestMethod]
    public async Task DeleteSelected_EmptyListDoesNotCallRepo()
    {
        const int supplementId = 10;

        var result = await _controller.DeleteSelected([], supplementId);

        var redirectResult = result as RedirectToActionResult;
        Assert.IsNotNull(redirectResult);
        Assert.AreEqual("Index", redirectResult.ActionName);
        _nutrientRepo.Verify(r => r.DeleteAsync(It.IsAny<IEnumerable<int>>()), Times.Never);
    }
}
