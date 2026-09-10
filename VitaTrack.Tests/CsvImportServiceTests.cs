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
            Name,Brand,DailyDose,ManufacturerUrl,Cost,ServingsPerBottle
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
            Name,Brand,DailyDose,ManufacturerUrl,Cost,ServingsPerBottle
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
            Name,Brand,DailyDose,ManufacturerUrl,Cost,ServingsPerBottle
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
            Name,Brand,DailyDose,ManufacturerUrl,Cost,ServingsPerBottle
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
        var sb = new StringBuilder("Name,Brand,DailyDose,ManufacturerUrl,Cost,ServingsPerBottle\n");
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
            Name,Brand,DailyDose,ManufacturerUrl,Cost,ServingsPerBottle
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

            Name,Brand,DailyDose,ManufacturerUrl,Cost,ServingsPerBottle

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
            Name,Brand,DailyDose,ManufacturerUrl,Cost,ServingsPerBottle
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
        var csvBytes = Encoding.UTF8.GetBytes("Name,Brand,DailyDose,ManufacturerUrl,Cost,ServingsPerBottle\nVitamin D3,NatureWise,2 capsules,,");
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

    [TestMethod]
    public async Task ParseAsync_WithServingsPerBottle_ParsesOptionalValue()
    {
        var csv = """
            Name,Brand,DailyDose,ManufacturerUrl,Cost,ServingsPerBottle
            Vitamin D3,NatureWise,2 capsules,https://example.com,15.99,60
            Zinc,NOW,1 capsule,,
            """;

        var result = await _service.ParseAsync(ToStream(csv));

        Assert.AreEqual(2, result.Rows.Count);
        Assert.AreEqual(0, result.Errors.Count);
        Assert.AreEqual(60m, result.Rows[0].ServingsPerBottle);
        Assert.IsNull(result.Rows[1].ServingsPerBottle);
    }

    [TestMethod]
    public async Task ParseAsync_NonPositiveServingsPerBottle_ReturnsError()
    {
        var csv = """
            Name,Brand,DailyDose,ManufacturerUrl,Cost,ServingsPerBottle
            Vitamin D3,NatureWise,2 capsules,,15.99,0
            """;

        var result = await _service.ParseAsync(ToStream(csv));

        Assert.AreEqual(0, result.Rows.Count);
        Assert.AreEqual(1, result.Errors.Count);
        Assert.IsTrue(result.Errors[0].Message.Contains("ServingsPerBottle"));
    }

    [TestMethod]
    public async Task ParseAsync_HeaderColumnCountMismatch_RejectedWithCounts()
    {
        var csv = """
            Name,Brand,DailyDose,ManufacturerUrl,Cost,ServingsPerBottle,Extra
            Vitamin D3,NatureWise,2 capsules,,,
            """;

        var result = await _service.ParseAsync(ToStream(csv));

        Assert.AreEqual(0, result.Rows.Count);
        Assert.AreEqual(1, result.Errors.Count);
        Assert.IsTrue(result.Errors[0].Message.Contains("Expected 6 columns, found 7"));
    }

    [TestMethod]
    public async Task ParseAsync_FieldLengthLimits_ReturnsError()
    {
        var longText = new string('A', 201);
        var longUrl = new string('h', 501);
        var csv = "Name,Brand,DailyDose,ManufacturerUrl,Cost,ServingsPerBottle\n" +
                  $"{longText},Brand,1 capsule,,\n" +
                  $"Name,{longText},1 capsule,,\n" +
                  $"Name,Brand,{longText},,\n" +
                  $"Name,Brand,1 capsule,{longUrl},\n";

        var result = await _service.ParseAsync(ToStream(csv));

        Assert.AreEqual(0, result.Rows.Count);
        Assert.AreEqual(4, result.Errors.Count);
        Assert.IsTrue(result.Errors.Any(e => e.Message.Contains("Name exceeds 200 characters")));
        Assert.IsTrue(result.Errors.Any(e => e.Message.Contains("Brand exceeds 200 characters")));
        Assert.IsTrue(result.Errors.Any(e => e.Message.Contains("DailyDose exceeds 200 characters")));
        Assert.IsTrue(result.Errors.Any(e => e.Message.Contains("ManufacturerUrl exceeds 500 characters")));
    }

    [TestMethod]
    public async Task ParseAsync_NonPositiveCost_ReturnsError()
    {
        var csv = """
            Name,Brand,DailyDose,ManufacturerUrl,Cost,ServingsPerBottle
            Vitamin D3,NatureWise,2 capsules,,0
            Zinc,NOW,1 capsule,,-5.99
            """;

        var result = await _service.ParseAsync(ToStream(csv));

        Assert.AreEqual(0, result.Rows.Count);
        Assert.AreEqual(2, result.Errors.Count);
        Assert.IsTrue(result.Errors.All(e => e.Message == "Cost must be positive"));
    }

    [TestMethod]
    public async Task ParseAsync_InvalidServingsPerBottle_ReturnsError()
    {
        var csv = """
            Name,Brand,DailyDose,ManufacturerUrl,Cost,ServingsPerBottle
            Vitamin D3,NatureWise,2 capsules,,15.99,abc
            """;

        var result = await _service.ParseAsync(ToStream(csv));

        Assert.AreEqual(0, result.Rows.Count);
        Assert.AreEqual(1, result.Errors.Count);
        Assert.IsTrue(result.Errors[0].Message.Contains("Invalid ServingsPerBottle value: 'abc'"));
    }

    [TestMethod]
    public async Task ParseAsync_QuotedFieldWithEscapedQuotes_ParsesLiteralQuote()
    {
        var csv = """
            Name,Brand,DailyDose,ManufacturerUrl,Cost,ServingsPerBottle
            "Vitamin ""D3"" 5000 IU",NatureWise,1 capsule,,
            """;

        var result = await _service.ParseAsync(ToStream(csv));

        Assert.AreEqual(1, result.Rows.Count);
        Assert.AreEqual(0, result.Errors.Count);
        Assert.AreEqual("Vitamin \"D3\" 5000 IU", result.Rows[0].Name);
    }
    [TestMethod]
    public async Task ParseAsync_HeaderColumnNameMismatch_RejectedWithExpectedColumn()
    {
        var csv = "Name,Brand,DailyDose,ManufacturerLink,Cost,ServingsPerBottle\nVitamin D3,NatureWise,1 capsule,,";
        var result = await _service.ParseAsync(ToStream(csv));
        Assert.AreEqual(0, result.Rows.Count);
        Assert.AreEqual(1, result.Errors.Count);
        StringAssert.Contains(result.Errors[0].Message, "Column 4: expected 'ManufacturerUrl'");
    }
}
