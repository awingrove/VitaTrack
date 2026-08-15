using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VitaTrack.Infrastructure.Services;

namespace VitaTrack.Tests;

[TestClass]
public class CsvImportServiceTests
{
    private readonly CsvImportService _service = new();

    private static Stream ToStream(string csv) => new MemoryStream(Encoding.UTF8.GetBytes(csv));

    [TestMethod]
    public async Task ParseAsync_ValidCsvThreeRows_ReturnsThreeRows()
    {
        var csv = """
            Name,Brand,DailyDose,ManufacturerUrl,Cost
            Vitamin D3,NatureWise,2 capsules,https://example.com,15.99
            Magnesium,Doctor's Best,2 tablets,https://example.com,12.49
            Zinc Complex,NOW,1 capsule,,
            """;

        var result = await _service.ParseAsync(ToStream(csv));

        Assert.AreEqual(3, result.Rows.Count);
        Assert.AreEqual(0, result.Errors.Count);
        Assert.AreEqual("Vitamin D3", result.Rows[0].Name);
        Assert.AreEqual("NatureWise", result.Rows[0].Brand);
        Assert.AreEqual(15.99m, result.Rows[0].Cost);
        Assert.IsNull(result.Rows[2].Cost);
        Assert.IsNull(result.Rows[2].ManufacturerUrl);
    }

    [TestMethod]
    public async Task ParseAsync_MissingName_ReturnsErrorForRow()
    {
        var csv = """
            Name,Brand,DailyDose,ManufacturerUrl,Cost
            ,BrandX,1 tablet,,
            """;

        var result = await _service.ParseAsync(ToStream(csv));

        Assert.AreEqual(0, result.Rows.Count);
        Assert.AreEqual(1, result.Errors.Count);
        Assert.AreEqual(2, result.Errors[0].RowNumber);
        Assert.IsTrue(result.Errors[0].Message.Contains("Name"));
    }

    [TestMethod]
    public async Task ParseAsync_MissingBrand_ReturnsErrorForRow()
    {
        var csv = """
            Name,Brand,DailyDose,ManufacturerUrl,Cost
            Vitamin C,,500mg,,
            """;

        var result = await _service.ParseAsync(ToStream(csv));

        Assert.AreEqual(0, result.Rows.Count);
        Assert.AreEqual(1, result.Errors.Count);
        Assert.IsTrue(result.Errors[0].Message.Contains("Brand"));
    }

    [TestMethod]
    public async Task ParseAsync_MissingDailyDose_ReturnsErrorForRow()
    {
        var csv = """
            Name,Brand,DailyDose,ManufacturerUrl,Cost
            Vitamin C,NatureWise,,,
            """;

        var result = await _service.ParseAsync(ToStream(csv));

        Assert.AreEqual(0, result.Rows.Count);
        Assert.AreEqual(1, result.Errors.Count);
        Assert.IsTrue(result.Errors[0].Message.Contains("DailyDose"));
    }

    [TestMethod]
    public async Task ParseAsync_Exceeds20Rows_RejectsEntireFile()
    {
        var sb = new StringBuilder("Name,Brand,DailyDose,ManufacturerUrl,Cost\n");
        for (var i = 1; i <= 21; i++)
            sb.AppendLine($"Product{i},Brand{i},1 tablet,,");

        var result = await _service.ParseAsync(ToStream(sb.ToString()));

        Assert.AreEqual(0, result.Rows.Count);
        Assert.IsTrue(result.Errors.Any(e => e.Message.Contains("20")));
    }

    [TestMethod]
    public async Task ParseAsync_QuotedFieldWithComma_ParsedCorrectly()
    {
        var csv = """
            Name,Brand,DailyDose,ManufacturerUrl,Cost
            "Vitamin D, 5000 IU",NatureWise,1 capsule,,
            """;

        var result = await _service.ParseAsync(ToStream(csv));

        Assert.AreEqual(1, result.Rows.Count);
        Assert.AreEqual("Vitamin D, 5000 IU", result.Rows[0].Name);
    }

    [TestMethod]
    public async Task ParseAsync_EmptyLines_Skipped()
    {
        var csv = """

            Name,Brand,DailyDose,ManufacturerUrl,Cost

            Vitamin D3,NatureWise,2 capsules,,

            Zinc,NOW,1 capsule,,

            """;

        var result = await _service.ParseAsync(ToStream(csv));

        Assert.AreEqual(2, result.Rows.Count);
        Assert.AreEqual(0, result.Errors.Count);
    }

    [TestMethod]
    public async Task ParseAsync_InvalidCost_ReturnsError()
    {
        var csv = """
            Name,Brand,DailyDose,ManufacturerUrl,Cost
            Vitamin D3,NatureWise,2 capsules,,abc
            """;

        var result = await _service.ParseAsync(ToStream(csv));

        Assert.AreEqual(0, result.Rows.Count);
        Assert.AreEqual(1, result.Errors.Count);
        Assert.IsTrue(result.Errors[0].Message.Contains("Cost"));
    }

    [TestMethod]
    public async Task ParseAsync_BomCharacter_Handled()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF };
        var csvBytes = Encoding.UTF8.GetBytes("Name,Brand,DailyDose,ManufacturerUrl,Cost\nVitamin D3,NatureWise,2 capsules,,");
        var stream = new MemoryStream(bytes.Concat(csvBytes).ToArray());

        var result = await _service.ParseAsync(stream);

        Assert.AreEqual(1, result.Rows.Count);
        Assert.AreEqual("Vitamin D3", result.Rows[0].Name);
    }

    [TestMethod]
    public async Task ParseAsync_WrongHeader_Rejected()
    {
        var csv = """
            Foo,Bar,Baz,Qux,Quux
            Vitamin D3,NatureWise,2 capsules,,
            """;

        var result = await _service.ParseAsync(ToStream(csv));

        Assert.AreEqual(0, result.Rows.Count);
        Assert.IsTrue(result.Errors.Count > 0);
    }

    [TestMethod]
    public async Task ParseAsync_EmptyFile_ReturnsError()
    {
        var result = await _service.ParseAsync(ToStream(""));

        Assert.AreEqual(0, result.Rows.Count);
        Assert.IsTrue(result.Errors.Any(e => e.Message.Contains("empty")));
    }
}
