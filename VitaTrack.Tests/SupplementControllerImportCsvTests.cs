using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using VitaTrack.Infrastructure.Data;
using VitaTrack.Infrastructure.Models;
using VitaTrack.Infrastructure.Services;
using VitaTrack.Web.Controllers;

namespace VitaTrack.Tests;

[TestClass]
public class SupplementControllerImportCsvTests
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
            _suppRepo.Object,
            _nutrientRepo.Object,
            _nutrientService.Object,
            _llmService.Object,
            _csvImportService.Object);
    }

    [TestMethod]
    public async Task ImportCsv_NullFile_ReturnsErrorReport()
    {
        var result = await _controller.ImportCsv(null!);

        var partialResult = result as PartialViewResult;
        Assert.IsNotNull(partialResult);
        var report = partialResult.Model as CsvImportReport;
        Assert.IsNotNull(report);
        Assert.AreEqual(0, report.Successes.Count);
        Assert.IsTrue(report.Failures.Count > 0);
    }

    [TestMethod]
    public async Task ImportCsv_ValidCsv_ReturnsSuccessReport()
    {
        var csvRows = new List<CsvSupplementRow>
        {
            new(2, "Vitamin D3", "NatureWise", "2 capsules", null, null, null)
        };
        var parseResult = new CsvParseResult(csvRows, []);
        _csvImportService.Setup(s => s.ParseAsync(It.IsAny<Stream>())).ReturnsAsync(parseResult);
        _suppRepo.Setup(r => r.AddAsync(It.IsAny<Supplement>())).ReturnsAsync(1);

        var file = CreateFormFile("Name,Brand,DailyDose\nVitamin D3,NatureWise,2 capsules");
        var result = await _controller.ImportCsv(file);

        var partialResult = result as PartialViewResult;
        Assert.IsNotNull(partialResult);
        var report = partialResult.Model as CsvImportReport;
        Assert.IsNotNull(report);
        Assert.AreEqual(1, report.Successes.Count);
        Assert.AreEqual(0, report.Failures.Count);
        _suppRepo.Verify(r => r.AddAsync(It.IsAny<Supplement>()), Times.Once);
    }

    private static IFormFile CreateFormFile(string content)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, bytes.Length, "file", "test.csv");
    }
}
