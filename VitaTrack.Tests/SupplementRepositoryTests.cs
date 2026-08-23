using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VitaTrack.Infrastructure.Data;
using VitaTrack.Infrastructure.Models;

namespace VitaTrack.Tests;

[TestClass]
public class SupplementRepositoryTests : SqliteTestBase
{
    private ISupplementRepository _repo = null!;

    [TestInitialize]
    public void Setup() => _repo = new SupplementRepository(Connection);

    [TestMethod]
    public async Task Crud_Works()
    {
        var sup = new Supplement
        {
            Name = "Vitamin D",
            Brand = "NatureMade",
            DailyDose = "1000 IU",
            ManufacturerUrl = "https://example.com/vitd",
            NutritionJson = "{}",
            SwapSuggestion = null
        };

        var id = await _repo.AddAsync(sup);
        Assert.IsTrue(id > 0);

        var fetched = await _repo.GetByIdAsync(id);
        Assert.IsNotNull(fetched);
        Assert.AreEqual(sup.Name, fetched!.Name);
        Assert.AreEqual(sup.Brand, fetched.Brand);

        var all = await _repo.GetAllAsync();
        Assert.AreEqual(1, all.Count);

        fetched!.DailyDose = "2000 IU";
        await _repo.UpdateAsync(fetched);
        var updated = await _repo.GetByIdAsync(id);
        Assert.AreEqual("2000 IU", updated!.DailyDose);
    }

    [TestMethod]
    public async Task Delete_RemovesEntity()
    {
        // Arrange
        var sup = new Supplement
        {
            Name = "ToDelete",
            Brand = "TestBrand",
            DailyDose = "100 IU",
            ManufacturerUrl = "https://example.com/todel",
            NutritionJson = "{}",
            SwapSuggestion = null
        };
        var id = await _repo.AddAsync(sup);
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
    public async Task DeleteMultiple_RemovesSelectedEntities()
    {
        var ids = new List<int>();
        for (int i = 0; i < 3; i++)
        {
            var id = await _repo.AddAsync(new Supplement
            {
                Name = $"Supp{i}",
                Brand = "Brand",
                DailyDose = "1 pill"
            });
            ids.Add(id);
        }

        await _repo.DeleteAsync([ids[0], ids[2]]);

        var all = await _repo.GetAllAsync();
        Assert.AreEqual(1, all.Count);
        Assert.AreEqual(ids[1], all[0].Id);
    }

    [TestMethod]
    public async Task DeleteMultiple_EmptyListDeletesNothing()
    {
        await _repo.AddAsync(new Supplement
        {
            Name = "Keep",
            Brand = "Brand",
            DailyDose = "1 pill"
        });

        await _repo.DeleteAsync(new List<int>());

        var all = await _repo.GetAllAsync();
        Assert.AreEqual(1, all.Count);
    }

    [TestMethod]
    public async Task GetAllAsync_ReturnsRowsOrderedByName()
    {
        // Insert out of alphabetical order
        await _repo.AddAsync(new Supplement { Name = "Zeta", Brand = "B", DailyDose = "1 pill" });
        await _repo.AddAsync(new Supplement { Name = "Alpha", Brand = "B", DailyDose = "1 pill" });
        await _repo.AddAsync(new Supplement { Name = "Mango", Brand = "B", DailyDose = "1 pill" });

        var rows = await _repo.GetAllAsync();
        var names = rows.Select(r => r.Name).ToList();

        CollectionAssert.AreEqual(new[] { "Alpha", "Mango", "Zeta" }, names);
    }

    [TestMethod]
    public async Task DeleteMultiple_RemovesSupplementsWithNutrients()
    {
        // Arrange – create supplements with nutrients and prescribed doses
        var nutrientRepo = new SupplementNutrientRepository(Connection);
        var doseRepo = new PrescribedDoseRepository(Connection);

        var supp1Id = await _repo.AddAsync(new Supplement
        {
            Name = "WithNutrients1",
            Brand = "Brand",
            DailyDose = "1 pill"
        });
        var supp2Id = await _repo.AddAsync(new Supplement
        {
            Name = "WithNutrients2",
            Brand = "Brand",
            DailyDose = "1 pill"
        });
        var supp3Id = await _repo.AddAsync(new Supplement
        {
            Name = "NoNutrients",
            Brand = "Brand",
            DailyDose = "1 pill"
        });

        await nutrientRepo.AddAsync(new SupplementNutrient
        {
            SupplementId = supp1Id,
            GenericName = "Vitamin C",
            SpecificForm = "Ascorbic Acid",
            Dosage = "500mg"
        });
        await nutrientRepo.AddAsync(new SupplementNutrient
        {
            SupplementId = supp1Id,
            GenericName = "Zinc",
            SpecificForm = "Zinc Picolinate",
            Dosage = "15mg"
        });
        await nutrientRepo.AddAsync(new SupplementNutrient
        {
            SupplementId = supp2Id,
            GenericName = "Vitamin D",
            SpecificForm = "Cholecalciferol",
            Dosage = "1000IU"
        });

        // Add a family member and prescribed dose for supp1
        var familyRepo = new FamilyRepository(Connection);
        var familyId = await familyRepo.AddAsync(new FamilyMember { Name = "Test", DisplayName = "Test" });
        await doseRepo.AddAsync(new PrescribedDose
        {
            FamilyMemberId = familyId,
            SupplementId = supp1Id,
            Dosage = "500mg",
            Instructions = "Take daily",
            FrequencyPerDay = 1
        });

        // Act – delete all three (including the two with nutrients and one with prescribed dose)
        await _repo.DeleteAsync([supp1Id, supp2Id, supp3Id]);

        // Assert – all supplements gone
        var allSupps = await _repo.GetAllAsync();
        Assert.AreEqual(0, allSupps.Count);

        // Assert – all nutrients gone
        var nutrients1 = await nutrientRepo.GetBySupplementIdAsync(supp1Id);
        Assert.AreEqual(0, nutrients1.Count);

        var nutrients2 = await nutrientRepo.GetBySupplementIdAsync(supp2Id);
        Assert.AreEqual(0, nutrients2.Count);

        // Assert – prescribed dose gone
        var doses = await doseRepo.GetAllAsync();
        Assert.AreEqual(0, doses.Count);
    }
}