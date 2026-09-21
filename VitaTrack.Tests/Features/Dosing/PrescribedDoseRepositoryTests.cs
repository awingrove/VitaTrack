using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VitaTrack.Core.Data;
using VitaTrack.Core.Features.Dosing;
using VitaTrack.Core.Models;

namespace VitaTrack.Tests.Features.Dosing;

[TestClass]
public class PrescribedDoseRepositoryTests : SqliteTestBase
{
    private IPrescribedDoseRepository _doseRepo = null!;
    private IFamilyRepository _familyRepo = null!;
    private ISupplementRepository _supplementRepo = null!;

    [TestInitialize]
    public void Setup()
    {
        _doseRepo = new PrescribedDoseRepository(Connection);
        _familyRepo = new FamilyRepository(Connection);
        _supplementRepo = new SupplementRepository(Connection);
    }

    private async Task<int> SeedMemberAsync(string displayName)
        => await _familyRepo.AddAsync(new FamilyMember { Name = displayName, DisplayName = displayName });

    private async Task<int> SeedSupplementAsync(string name)
        => await _supplementRepo.AddAsync(new Supplement { Name = name, Brand = "Brand", DailyDose = "1 pill" });

    private async Task<int> AddDoseAsync(int memberId, int supplementId, decimal multiplier = 1m, string instructions = "Take daily")
        => await _doseRepo.AddAsync(new PrescribedDose
        {
            FamilyMemberId = memberId,
            SupplementId = supplementId,
            Multiplier = multiplier,
            Instructions = instructions
        });

    [TestMethod]
    public async Task AddAsync_GetByIdAsync_RoundTrip()
    {
        // Arrange
        var memberId = await SeedMemberAsync("Alice");
        var supplementId = await SeedSupplementAsync("Vitamin D");
        var startDate = new DateTime(2026, 1, 15);
        var endDate = new DateTime(2026, 6, 30);

        // Act – Add
        var id = await _doseRepo.AddAsync(new PrescribedDose
        {
            FamilyMemberId = memberId,
            SupplementId = supplementId,
            StartDate = startDate,
            EndDate = endDate,
            Multiplier = 1.5m,
            Instructions = "Take with food"
        });
        Assert.IsTrue(id > 0);

        // Assert – every column reads back as stored
        var fetched = await _doseRepo.GetByIdAsync(id);
        Assert.IsNotNull(fetched);
        Assert.AreEqual(memberId, fetched!.FamilyMemberId);
        Assert.AreEqual(supplementId, fetched.SupplementId);
        Assert.AreEqual(startDate, fetched.StartDate);
        Assert.AreEqual(endDate, fetched.EndDate);
        Assert.AreEqual(1.5m, fetched.Multiplier);
        Assert.AreEqual("Take with food", fetched.Instructions);
    }

    [TestMethod]
    public async Task GetByIdAsync_ReturnsNull_WhenMissing()
    {
        // Arrange – a dose exists, so the lookup is filtered, not just on an empty table
        var memberId = await SeedMemberAsync("Alice");
        var supplementId = await SeedSupplementAsync("Vitamin D");
        await AddDoseAsync(memberId, supplementId);

        // Act & Assert
        Assert.IsNull(await _doseRepo.GetByIdAsync(999));
    }

    [TestMethod]
    public async Task GetAllAsync_JoinsNames()
    {
        // Arrange – two doses across two members and two supplements
        var aliceId = await SeedMemberAsync("Alice");
        var bobId = await SeedMemberAsync("Bob");
        var vitaminDId = await SeedSupplementAsync("Vitamin D");
        var omegaId = await SeedSupplementAsync("Omega 3");
        var aliceDoseId = await AddDoseAsync(aliceId, vitaminDId);
        var bobDoseId = await AddDoseAsync(bobId, omegaId);

        // Act
        var all = await _doseRepo.GetAllAsync();

        // Assert – LEFT JOINs surface DisplayName / Name
        Assert.AreEqual(2, all.Count);
        var aliceDose = all.Single(d => d.Id == aliceDoseId);
        var bobDose = all.Single(d => d.Id == bobDoseId);
        Assert.AreEqual("Alice", aliceDose.FamilyMemberName);
        Assert.AreEqual("Vitamin D", aliceDose.SupplementName);
        Assert.AreEqual("Brand", aliceDose.SupplementBrand);
        Assert.AreEqual("Bob", bobDose.FamilyMemberName);
        Assert.AreEqual("Omega 3", bobDose.SupplementName);
        Assert.AreEqual("Brand", bobDose.SupplementBrand);
    }

    [TestMethod]
    public async Task GetAllAsync_ReturnsEmpty_WhenNoData()
    {
        Assert.AreEqual(0, (await _doseRepo.GetAllAsync()).Count);
    }

    [TestMethod]
    public async Task UpdateAsync_PersistsChanges()
    {
        // Arrange
        var memberId = await SeedMemberAsync("Alice");
        var supplementId = await SeedSupplementAsync("Vitamin D");
        var id = await AddDoseAsync(memberId, supplementId, multiplier: 1m, instructions: "Take daily");

        // Act – change multiplier and instructions
        var dose = await _doseRepo.GetByIdAsync(id);
        Assert.IsNotNull(dose);
        dose!.Multiplier = 2.25m;
        dose.Instructions = "Take with breakfast";
        await _doseRepo.UpdateAsync(dose);

        // Assert – read back from the database
        var updated = await _doseRepo.GetByIdAsync(id);
        Assert.IsNotNull(updated);
        Assert.AreEqual(2.25m, updated!.Multiplier);
        Assert.AreEqual("Take with breakfast", updated.Instructions);
    }

    [TestMethod]
    public async Task DeleteAsync_RemovesRow_AndReturnsZeroWhenMissing()
    {
        // Arrange
        var memberId = await SeedMemberAsync("Alice");
        var supplementId = await SeedSupplementAsync("Vitamin D");
        var id = await AddDoseAsync(memberId, supplementId);

        // Act – Delete
        var affected = await _doseRepo.DeleteAsync(id);

        // Assert – row gone, one row was affected
        Assert.AreEqual(1, affected);
        Assert.IsNull(await _doseRepo.GetByIdAsync(id));
        Assert.AreEqual(0, (await _doseRepo.GetAllAsync()).Count);

        // Act & Assert – deleting a missing id affects no rows
        Assert.AreEqual(0, await _doseRepo.DeleteAsync(id));
    }

    [TestMethod]
    public async Task GetByFamilyMemberIdAsync_ReturnsOnlyThatMembersDoses()
    {
        var aliceId = await SeedMemberAsync("Alice");
        var bobId = await SeedMemberAsync("Bob");
        var vitaminDId = await SeedSupplementAsync("Vitamin D");
        var omegaId = await SeedSupplementAsync("Omega 3");
        var aliceDoseId = await AddDoseAsync(aliceId, vitaminDId);
        await AddDoseAsync(bobId, omegaId);

        var doses = await _doseRepo.GetByFamilyMemberIdAsync(aliceId);

        var dose = doses.Single();
        Assert.AreEqual(aliceDoseId, dose.Id);
        Assert.AreEqual(aliceId, dose.FamilyMemberId);
        Assert.AreEqual("Alice", dose.FamilyMemberName);
        Assert.AreEqual("Vitamin D", dose.SupplementName);
        Assert.AreEqual("Brand", dose.SupplementBrand);
    }
}
