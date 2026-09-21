using Dapper;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VitaTrack.Core.Data;
using VitaTrack.Core.Features.Dosing;
using VitaTrack.Core.Models;
using VitaTrack.Core.Services;

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

    private int InsertDose(int memberId, int supplementId, decimal multiplier,
        DateTime? start = null, DateTime? end = null)
    {
        return Connection.ExecuteScalar<int>(
            @"INSERT INTO PrescribedDoses (FamilyMemberId, SupplementId, StartDate, EndDate, Multiplier, Instructions)
              VALUES (@Member, @Supplement, @Start, @End, @Multiplier, ''); SELECT last_insert_rowid();",
            new { Member = memberId, Supplement = supplementId, Start = start, End = end, Multiplier = multiplier });
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
        InsertDose(memberId, supplementId, multiplier: 2);

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
        InsertDose(memberId, noServings, multiplier: 1);
        InsertDose(memberId, noCost, multiplier: 1);

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
        InsertDose(memberId, supplementId, multiplier: 2);

        var data = await CreateService().GetNutrientReportDataAsync();

        Assert.AreEqual(12.00m, data.TotalCost);
    }

    [TestMethod]
    public async Task NutrientReport_NutrientDailyTotals_ClampNonPositiveMultiplier()
    {
        var memberId = InsertMember("Alice");
        var supplementId = InsertSupplement("Vitamin C", cost: 12.00m, servingsPerBottle: 60);
        InsertNutrient(supplementId, "Vitamin C", "500mg");
        InsertDose(memberId, supplementId, multiplier: 0);

        var data = await CreateService().GetNutrientReportDataAsync();

        Assert.AreEqual("500", data.MemberData.Single()["Vitamin C"]);
    }

    [TestMethod]
    public async Task Reports_ExcludeExpiredDoses()
    {
        var memberId = InsertMember("Alice");
        var supplementId = InsertSupplement("Vitamin C", cost: 12.00m, servingsPerBottle: 60);
        InsertNutrient(supplementId, "Vitamin C", "500mg");
        InsertDose(memberId, supplementId, multiplier: 1,
            start: DateTime.Today.AddDays(-30), end: DateTime.Today.AddDays(-1));

        var service = CreateService();
        var costData = await service.GetCostReportDataAsync();
        var nutrientData = await service.GetNutrientReportDataAsync();

        Assert.AreEqual(0m, costData.GrandTotal);
        Assert.AreEqual(0, nutrientData.MemberData.Count);
    }

    [TestMethod]
    public async Task NutrientReport_Units_AreExtractedPerNutrient()
    {
        var memberId = InsertMember("Alice");
        var supplementId = InsertSupplement("Vitamin C", cost: 12.00m, servingsPerBottle: 60);
        InsertNutrient(supplementId, "Vitamin C", "500mg");
        InsertDose(memberId, supplementId, multiplier: 1);

        var data = await CreateService().GetNutrientReportDataAsync();

        Assert.IsTrue(data.Units.TryGetValue("Vitamin C", out var unit));
        Assert.AreEqual("mg", unit);
    }

    [TestMethod]
    public async Task NutrientReport_Units_MergeConflictingUnitsAcrossSupplements()
    {
        var memberId = InsertMember("Alice");
        var suppA = InsertSupplement("Fish Oil", cost: 12.00m, servingsPerBottle: 60);
        var suppB = InsertSupplement("Multivitamin", cost: 12.00m, servingsPerBottle: 60);
        InsertNutrient(suppA, "Vitamin D", "200IU");
        InsertNutrient(suppB, "Vitamin D", "20µg");
        InsertDose(memberId, suppA, multiplier: 1);
        InsertDose(memberId, suppB, multiplier: 1);
        var data = await CreateService().GetNutrientReportDataAsync();

        Assert.IsTrue(data.Units.TryGetValue("Vitamin D", out var unit));
        Assert.AreEqual("IU, µg", unit);
    }

    [TestMethod]
    public async Task NutrientReport_Contributions_ListSupplementsBehindMemberTotals()
    {
        var memberId = InsertMember("Alice");
        var alpha = InsertSupplement("Alpha Vit", cost: 12.00m, servingsPerBottle: 60);
        var beta = InsertSupplement("Beta Vit", cost: 12.00m, servingsPerBottle: 60);
        InsertNutrient(alpha, "Vitamin C", "500mg");
        InsertNutrient(beta, "Vitamin C", "90mg");
        InsertDose(memberId, alpha, multiplier: 1);
        InsertDose(memberId, beta, multiplier: 1);

        var data = await CreateService().GetNutrientReportDataAsync();

        var contribs = data.MemberContributions.Single()["Vitamin C"];
        Assert.AreEqual(2, contribs.Count);
        Assert.AreEqual("Alpha Vit", contribs[0].SupplementName);
        Assert.AreEqual(500m, contribs[0].Amount);
        Assert.AreEqual(90m, contribs[1].Amount);
        Assert.AreEqual(590m, contribs.Sum(c => c.Amount),
            "Contributions must sum to the displayed member total");
    }

    [TestMethod]
    public async Task NutrientReport_Contributions_ApplyMultiplierPerDose()
    {
        var memberId = InsertMember("Alice");
        var suppId = InsertSupplement("Solo Vit", cost: 12.00m, servingsPerBottle: 60);
        InsertNutrient(suppId, "Vitamin D", "500IU");
        InsertDose(memberId, suppId, multiplier: 2);

        var data = await CreateService().GetNutrientReportDataAsync();

        var contribs = data.MemberContributions.Single()["Vitamin D"];
        Assert.AreEqual(1, contribs.Count);
        Assert.AreEqual(1000m, contribs[0].Amount);
        Assert.AreEqual(2m, contribs[0].Multiplier);
    }

    [TestMethod]
    public async Task NutrientReport_Contributions_AggregateSameSupplementDoses()
    {
        var memberId = InsertMember("Alice");
        var suppId = InsertSupplement("Solo Vit", cost: 12.00m, servingsPerBottle: 60);
        InsertNutrient(suppId, "Vitamin D", "500IU");
        InsertDose(memberId, suppId, multiplier: 2);
        InsertDose(memberId, suppId, multiplier: 2);

        var data = await CreateService().GetNutrientReportDataAsync();

        var contribs = data.MemberContributions.Single()["Vitamin D"];
        Assert.AreEqual(1, contribs.Count);
        Assert.AreEqual(2000m, contribs[0].Amount);
        Assert.AreEqual(2m, contribs[0].Multiplier);
    }

    [TestMethod]
    public async Task NutrientReport_Contributions_HideMultiplierWhenDosesDisagree()
    {
        var memberId = InsertMember("Alice");
        var suppId = InsertSupplement("Solo Vit", cost: 12.00m, servingsPerBottle: 60);
        InsertNutrient(suppId, "Vitamin D", "500IU");
        InsertDose(memberId, suppId, multiplier: 2);
        InsertDose(memberId, suppId, multiplier: 3);

        var data = await CreateService().GetNutrientReportDataAsync();

        var contribs = data.MemberContributions.Single()["Vitamin D"];
        Assert.AreEqual(1, contribs.Count);
        Assert.AreEqual(2500m, contribs[0].Amount);
        Assert.IsNull(contribs[0].Multiplier);
    }

    [TestMethod]
    public async Task NutrientReport_Contributions_SkipZeroDosageNutrients()
    {
        var memberId = InsertMember("Alice");
        var suppId = InsertSupplement("Blend Supp", cost: 12.00m, servingsPerBottle: 60);
        InsertNutrient(suppId, "Vitamin C", "500mg");
        InsertNutrient(suppId, "Blend Child", "");
        InsertDose(memberId, suppId, multiplier: 1);

        var data = await CreateService().GetNutrientReportDataAsync();

        var memberRows = data.MemberContributions.Single();
        Assert.IsTrue(memberRows.ContainsKey("Vitamin C"));
        Assert.IsFalse(memberRows.ContainsKey("Blend Child"));
    }

    [TestMethod]
    public async Task NutrientReport_Totals_ShowDecimalsOnlyWhenNeeded()
    {
        var memberId = InsertMember("Alice");
        var whole = InsertSupplement("Whole Vit", cost: 12.00m, servingsPerBottle: 60);
        var fractional = InsertSupplement("Frac Vit", cost: 12.00m, servingsPerBottle: 60);
        InsertNutrient(whole, "Vitamin C", "500mg");
        InsertNutrient(fractional, "Vitamin D", "1.5µg");
        InsertDose(memberId, whole, multiplier: 1);
        InsertDose(memberId, fractional, multiplier: 1);

        var data = await CreateService().GetNutrientReportDataAsync();

        var totals = data.MemberData.Single();
        Assert.AreEqual("500", totals["Vitamin C"], "Whole numbers render without decimals");
        Assert.AreEqual("1.5", totals["Vitamin D"], "Fractional amounts keep one decimal");
    }
}
