using Dapper;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VitaTrack.Infrastructure.Data;
using VitaTrack.Infrastructure.Models;
using VitaTrack.Infrastructure.Services;

namespace VitaTrack.Tests;

[TestClass]
public class ReportingServiceTests : SqliteTestBase
{
    private ReportingService CreateService() => new(
        new SupplementRepository(Connection),
        new PrescribedDoseRepository(Connection),
        new FamilyRepository(Connection),
        new SupplementNutrientRepository(Connection));

    private int InsertSupplement(string name, decimal? cost, decimal? servingsPerBottle)
    {
        return Connection.ExecuteScalar<int>(
            "INSERT INTO Supplements (Name, Brand, DailyDose, Cost, ServingsPerBottle) VALUES (@Name, 'TestBrand', '1 tablet', @Cost, @Servings); SELECT last_insert_rowid();",
            new { Name = name, Cost = cost, Servings = servingsPerBottle });
    }

    private int InsertMember(string name)
    {
        return Connection.ExecuteScalar<int>(
            "INSERT INTO FamilyMembers (Name, DisplayName) VALUES (@Name, @Name); SELECT last_insert_rowid();",
            new { Name = name });
    }

    private int InsertDose(int memberId, int supplementId, decimal frequencyPerDay,
        DateTime? start = null, DateTime? end = null)
    {
        return Connection.ExecuteScalar<int>(
            @"INSERT INTO PrescribedDoses (FamilyMemberId, SupplementId, StartDate, EndDate, Dosage, Instructions, FrequencyPerDay)
              VALUES (@Member, @Supplement, @Start, @End, '1 tablet', '', @Freq); SELECT last_insert_rowid();",
            new { Member = memberId, Supplement = supplementId, Start = start, End = end, Freq = frequencyPerDay });
    }

    private void InsertNutrient(int supplementId, string genericName, string dosage)
    {
        Connection.Execute(
            "INSERT INTO SupplementNutrients (SupplementId, GenericName, SpecificForm, Dosage) VALUES (@S, @N, '', @D)",
            new { S = supplementId, N = genericName, D = dosage });
    }

    [TestMethod]
    public async Task CostReport_MonthlyCost_IsBottlePriceTimesDosesDividedByServings()
    {
        var memberId = InsertMember("Alice");
        var supplementId = InsertSupplement("Vitamin C", cost: 30.00m, servingsPerBottle: 60);
        InsertDose(memberId, supplementId, frequencyPerDay: 2);

        var data = await CreateService().GetCostReportDataAsync();

        Assert.AreEqual(1, data.SupplementCosts.Count);
        Assert.AreEqual(30.00m, data.SupplementCosts[0].MonthlyCost);
        Assert.AreEqual(30.00m, data.MemberCosts[0].MonthlyCost);
        Assert.AreEqual(30.00m, data.GrandTotal);
    }

    [TestMethod]
    public async Task CostReport_ExcludesSupplementWithoutServingsOrCost()
    {
        var memberId = InsertMember("Alice");
        var noServings = InsertSupplement("No Servings", cost: 10m, servingsPerBottle: null);
        var noCost = InsertSupplement("No Cost", cost: null, servingsPerBottle: 60);
        InsertDose(memberId, noServings, frequencyPerDay: 1);
        InsertDose(memberId, noCost, frequencyPerDay: 1);

        var data = await CreateService().GetCostReportDataAsync();

        Assert.AreEqual(0m, data.GrandTotal);
        Assert.AreEqual(0, data.SupplementCosts.Count);
    }

    [TestMethod]
    public async Task NutrientReport_TotalCost_MatchesCostReportMonthlyFormula()
    {
        var memberId = InsertMember("Alice");
        var supplementId = InsertSupplement("Vitamin C", cost: 12.00m, servingsPerBottle: 60);
        InsertNutrient(supplementId, "Vitamin C", "500mg");
        InsertDose(memberId, supplementId, frequencyPerDay: 2);

        var data = await CreateService().GetNutrientReportDataAsync();

        Assert.AreEqual(12.00m, data.TotalCost);
    }

    [TestMethod]
    public async Task NutrientReport_NutrientDailyTotals_ClampNonPositiveFrequency()
    {
        var memberId = InsertMember("Alice");
        var supplementId = InsertSupplement("Vitamin C", cost: 12.00m, servingsPerBottle: 60);
        InsertNutrient(supplementId, "Vitamin C", "500mg");
        InsertDose(memberId, supplementId, frequencyPerDay: 0);

        var data = await CreateService().GetNutrientReportDataAsync();

        Assert.AreEqual(500m, data.GrandTotals["Vitamin C"]);
    }

    [TestMethod]
    public async Task Reports_ExcludeExpiredDoses()
    {
        var memberId = InsertMember("Alice");
        var supplementId = InsertSupplement("Vitamin C", cost: 12.00m, servingsPerBottle: 60);
        InsertNutrient(supplementId, "Vitamin C", "500mg");
        InsertDose(memberId, supplementId, frequencyPerDay: 1,
            start: DateTime.Today.AddDays(-30), end: DateTime.Today.AddDays(-1));

        var service = CreateService();
        var costData = await service.GetCostReportDataAsync();
        var nutrientData = await service.GetNutrientReportDataAsync();

        Assert.AreEqual(0m, costData.GrandTotal);
        Assert.AreEqual(0, nutrientData.GrandTotals.Count);
    }
}
