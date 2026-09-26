using Dapper;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VitaTrack.Core.Data;
using VitaTrack.Core.Features.Dosing;
using VitaTrack.Core.Features.Family;
using VitaTrack.Core.Features.Reporting;
using VitaTrack.Core.Features.Nutrients;
using VitaTrack.Core.Features.Supplements;

namespace VitaTrack.Tests;

/// <summary>
/// Which units reach the Nutrient Report's unit column. Split out of
/// <see cref="ReportingServiceTests"/> because unit admission is a separate concern
/// from amount aggregation, and because the class it came from hit the 300-line
/// split trigger. The last two tests are the regression for free text leaking in
/// as a unit, in both directions: an unrecognized token contributes none, a
/// recognized count-noun still does.
/// </summary>
[TestClass]
public class NutrientReportUnitTests : SqliteTestBase
{
    private ReportingService CreateService() => new(
        new SupplementRepository(Connection, new SupplementNutrientRepository(Connection), new PrescribedDoseRepository(Connection)),
        new PrescribedDoseRepository(Connection),
        new FamilyRepository(Connection, new PrescribedDoseRepository(Connection)),
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

    private int InsertDose(int memberId, int supplementId, decimal multiplier)
    {
        return Connection.ExecuteScalar<int>(
            @"INSERT INTO PrescribedDoses (FamilyMemberId, SupplementId, StartDate, EndDate, Multiplier, Instructions)
              VALUES (@Member, @Supplement, NULL, NULL, @Multiplier, ''); SELECT last_insert_rowid();",
            new { Member = memberId, Supplement = supplementId, Multiplier = multiplier });
    }

    private void InsertNutrient(int supplementId, string genericName, string dosage)
    {
        Connection.Execute(
            "INSERT INTO SupplementNutrients (SupplementId, GenericName, SpecificForm, Dosage) VALUES (@S, @N, '', @D)",
            new { S = supplementId, N = genericName, D = dosage });
    }

    [TestMethod]
    public async Task NutrientReport_Units_AreExtractedPerNutrient()
    {
        var memberId = InsertMember("Alice");
        var supplementId = InsertSupplement("Vitamin C", cost: 12.00m, servingsPerBottle: 60);
        InsertNutrient(supplementId, "Vitamin C", "500mg");
        InsertDose(memberId, supplementId, multiplier: 1);

        var data = await CreateService().GetNutrientReportDataAsync();

        Assert.IsTrue(data.Units.Any(u => u.NutrientName == "Vitamin C"));
        Assert.AreEqual("mg", data.Units.Single(u => u.NutrientName == "Vitamin C").Units);
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

        Assert.IsTrue(data.Units.Any(u => u.NutrientName == "Vitamin D"));
        Assert.AreEqual("IU, µg", data.Units.Single(u => u.NutrientName == "Vitamin D").Units);
    }

    [TestMethod]
    public async Task NutrientReport_FreeTextDosage_ContributesNoUnit()
    {
        var memberId = InsertMember("Alice");
        var suppId = InsertSupplement("Fish Oil", cost: 12.00m, servingsPerBottle: 60);
        InsertNutrient(suppId, "Fish Oil", "3 capsules");
        InsertDose(memberId, suppId, multiplier: 1);

        var data = await CreateService().GetNutrientReportDataAsync();

        Assert.IsFalse(data.Units.Any(u => u.NutrientName == "Fish Oil"),
            "free text is not a unit, so the nutrient contributes none");
        Assert.IsFalse(data.Units.Any(u => u.Units.Contains("capsule")),
            "neither 'capsules' nor '3 capsules' may appear in a unit column");
        Assert.AreEqual("3", data.MemberTotals.Single().Totals.Single(t => t.NutrientName == "Fish Oil").Amount,
            "the amount is still counted — only the unit is withheld");
    }

    [TestMethod]
    public async Task NutrientReport_CountNounDosage_StillContributesItsUnit()
    {
        var memberId = InsertMember("Alice");
        var suppId = InsertSupplement("Calcium", cost: 12.00m, servingsPerBottle: 60);
        InsertNutrient(suppId, "Calcium", "3 tablets");
        InsertDose(memberId, suppId, multiplier: 1);

        var data = await CreateService().GetNutrientReportDataAsync();

        Assert.AreEqual("tab", data.Units.Single(u => u.NutrientName == "Calcium").Units,
            "'3 tablets' is a legitimate dose: dropping count-nouns from the recognized set "
            + "would silently break it, so pin the unit here");
    }
}
