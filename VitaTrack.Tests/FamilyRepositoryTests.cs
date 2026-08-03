using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VitaTrack.Infrastructure.Data;
using VitaTrack.Infrastructure.Models;

namespace VitaTrack.Tests;

[TestClass]
public class FamilyRepositoryTests : SqliteTestBase
{
    private IFamilyRepository _repo = null!;

    [TestInitialize]
    public void Setup() => _repo = new FamilyRepository(Connection);

    [TestMethod]
    public async Task Add_GetAll_GetById_Update_Works()
    {
        // Arrange
        var member = new FamilyMember
        {
            Name = "John",
            DisplayName = "Johnny",
            AvatarUrl = "https://example.com/avatar.png"
        };

        // Act – Add
        var id = await _repo.AddAsync(member);
        Assert.IsTrue(id > 0);

        // Act – GetById
        var fetched = await _repo.GetByIdAsync(id);
        Assert.IsNotNull(fetched);
        Assert.AreEqual(member.Name, fetched!.Name);
        Assert.AreEqual(member.DisplayName, fetched.DisplayName);
        Assert.AreEqual(member.AvatarUrl, fetched.AvatarUrl);

        // Act – GetAll
        var all = await _repo.GetAllAsync();
        Assert.AreEqual(1, all.Count);

        // Act – Update
        fetched.DisplayName = "Jonny";
        await _repo.UpdateAsync(fetched);
        var updated = await _repo.GetByIdAsync(id);
        Assert.AreEqual("Jonny", updated!.DisplayName);
    }

    [TestMethod]
    public async Task GetAll_ReturnsEmpty_WhenNoData()
    {
        var all = await _repo.GetAllAsync();
        Assert.AreEqual(0, all.Count);
    }

    [TestMethod]
    public async Task Delete_RemovesEntity()
    {
        // Arrange
        var member = new FamilyMember
        {
            Name = "ToDelete",
            DisplayName = "TD",
            AvatarUrl = null
        };
        var id = await _repo.AddAsync(member);
        Assert.IsTrue(id > 0);

        // Act – Delete
        await _repo.DeleteAsync(id);

        // Assert
        var deleted = await _repo.GetByIdAsync(id);
        Assert.IsNull(deleted);

        var all = await _repo.GetAllAsync();
        Assert.AreEqual(0, all.Count);
    }

    [TestMethod]
    public async Task Delete_RemovesFamilyMemberWithPrescribedDoses()
    {
        // Arrange – create a family member with a prescribed dose
        var suppRepo = new SupplementRepository(Connection);
        var doseRepo = new PrescribedDoseRepository(Connection);

        var memberId = await _repo.AddAsync(new FamilyMember
        {
            Name = "WithDoses",
            DisplayName = "WD"
        });

        var suppId = await suppRepo.AddAsync(new Supplement
        {
            Name = "TestSupp",
            Brand = "Brand",
            DailyDose = "1 pill"
        });

        await doseRepo.AddAsync(new PrescribedDose
        {
            FamilyMemberId = memberId,
            SupplementId = suppId,
            Dosage = "500mg",
            Instructions = "Take daily",
            FrequencyPerDay = 1
        });

        // Act – Delete the family member
        await _repo.DeleteAsync(memberId);

        // Assert – family member gone
        var deleted = await _repo.GetByIdAsync(memberId);
        Assert.IsNull(deleted);

        // Assert – prescribed dose gone
        var doses = await doseRepo.GetAllAsync();
        Assert.AreEqual(0, doses.Count);
    }
}