using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VitaTrack.Core.Data;
using VitaTrack.Core.Models;

namespace VitaTrack.Tests;

// Dosage unit normalization is enforced at the repository boundary: every write
// canonicalizes the unit designation (mcg/ug/μg -> µg, iu -> IU).
[TestClass]
public class SupplementNutrientDosageNormalizationTests : SqliteTestBase
{
    private ISupplementNutrientRepository _nutrientRepo = null!;
    private ISupplementRepository _supplementRepo = null!;

    [TestInitialize]
    public void Setup()
    {
        _nutrientRepo = new SupplementNutrientRepository(Connection);
        _supplementRepo = new SupplementRepository(Connection);
    }

    private async Task<int> SeedSupplementAsync()
    {
        var sup = new Supplement
        {
            Name = "Normalization Supplement",
            Brand = "TestBrand",
            DailyDose = "1 tablet",
            NutritionJson = "{}",
            Cost = 10.00m
        };
        return await _supplementRepo.AddAsync(sup);
    }

    [TestMethod]
    public async Task AddAsync_NormalizesDosageUnits()
    {
        var supplementId = await SeedSupplementAsync();

        var id = await _nutrientRepo.AddAsync(new SupplementNutrient
        {
            SupplementId = supplementId,
            GenericName = "Vitamin D",
            SpecificForm = "Cholecalciferol",
            Dosage = "20mcg"
        });

        var fetched = await _nutrientRepo.GetByIdAsync(id);
        Assert.IsNotNull(fetched);
        Assert.AreEqual("20µg", fetched!.Dosage);
    }

    [TestMethod]
    public async Task AddAsync_PreservesCanonicalDosage()
    {
        var supplementId = await SeedSupplementAsync();

        var id = await _nutrientRepo.AddAsync(new SupplementNutrient
        {
            SupplementId = supplementId,
            GenericName = "Vitamin C",
            SpecificForm = "Ascorbic Acid",
            Dosage = "1.5 µg"
        });

        var fetched = await _nutrientRepo.GetByIdAsync(id);
        Assert.IsNotNull(fetched);
        Assert.AreEqual("1.5 µg", fetched!.Dosage);
    }

    [TestMethod]
    public async Task UpdateAsync_NormalizesDosageUnits()
    {
        var supplementId = await SeedSupplementAsync();
        var id = await _nutrientRepo.AddAsync(new SupplementNutrient
        {
            SupplementId = supplementId,
            GenericName = "Vitamin D",
            SpecificForm = "Cholecalciferol",
            Dosage = "20µg"
        });

        var nutrient = await _nutrientRepo.GetByIdAsync(id);
        nutrient!.Dosage = "400iu";
        await _nutrientRepo.UpdateAsync(nutrient);

        var fetched = await _nutrientRepo.GetByIdAsync(id);
        Assert.IsNotNull(fetched);
        Assert.AreEqual("400IU", fetched!.Dosage);
    }
}
