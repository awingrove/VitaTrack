using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VitaTrack.Infrastructure.Data;
using VitaTrack.Infrastructure.Models;

namespace VitaTrack.Tests
{
    [TestClass]
    public class SupplementNutrientRepositoryTests : SqliteTestBase
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
                Name = "Test Supplement",
                Brand = "TestBrand",
                DailyDose = "1 tablet",
                ManufacturerUrl = null,
                NutritionJson = "{}",
                SwapSuggestion = null,
                Cost = 10.00m
            };
            return await _supplementRepo.AddAsync(sup);
        }

        [TestMethod]
        public async Task Add_GetBySupplementId_GetById_Update_Works()
        {
            var supplementId = await SeedSupplementAsync();

            var nutrient = new SupplementNutrient
            {
                SupplementId = supplementId,
                GenericName = "Zinc",
                SpecificForm = "Zinc Picolinate",
                Dosage = "5mg"
            };

            // Act – Add
            var id = await _nutrientRepo.AddAsync(nutrient);
            Assert.IsTrue(id > 0);

            // Act – GetById
            var fetched = await _nutrientRepo.GetByIdAsync(id);
            Assert.IsNotNull(fetched);
            Assert.AreEqual(nutrient.GenericName, fetched!.GenericName);
            Assert.AreEqual(nutrient.SpecificForm, fetched.SpecificForm);
            Assert.AreEqual(nutrient.Dosage, fetched.Dosage);
            Assert.AreEqual(nutrient.SupplementId, fetched.SupplementId);

            // Act – GetBySupplementId
            var all = await _nutrientRepo.GetBySupplementIdAsync(supplementId);
            Assert.AreEqual(1, all.Count);
            Assert.AreEqual(nutrient.GenericName, all[0].GenericName);

            // Act – Update
            fetched.GenericName = "Magnesium";
            fetched.SpecificForm = "Magnesium Glycinate";
            fetched.Dosage = "200mg";
            await _nutrientRepo.UpdateAsync(fetched);
            var updated = await _nutrientRepo.GetByIdAsync(id);
            Assert.IsNotNull(updated);
            Assert.AreEqual("Magnesium", updated!.GenericName);
            Assert.AreEqual("Magnesium Glycinate", updated.SpecificForm);
            Assert.AreEqual("200mg", updated.Dosage);
        }

        [TestMethod]
        public async Task GetBySupplementId_ReturnsEmpty_WhenNoNutrients()
        {
            var supplementId = await SeedSupplementAsync();
            var all = await _nutrientRepo.GetBySupplementIdAsync(supplementId);
            Assert.AreEqual(0, all.Count);
        }

        [TestMethod]
        public async Task GetBySupplementId_ReturnsOnlyNutrientsForThatSupplement()
        {
            var sup1Id = await SeedSupplementAsync();
            var sup2Id = await SeedSupplementAsync();

            await _nutrientRepo.AddAsync(new SupplementNutrient
            {
                SupplementId = sup1Id,
                GenericName = "Zinc",
                SpecificForm = "Zinc Picolinate",
                Dosage = "5mg"
            });
            await _nutrientRepo.AddAsync(new SupplementNutrient
            {
                SupplementId = sup2Id,
                GenericName = "Vitamin C",
                SpecificForm = "Ascorbic Acid",
                Dosage = "500mg"
            });

            var sup1Nutrients = await _nutrientRepo.GetBySupplementIdAsync(sup1Id);
            var sup2Nutrients = await _nutrientRepo.GetBySupplementIdAsync(sup2Id);

            Assert.AreEqual(1, sup1Nutrients.Count);
            Assert.AreEqual("Zinc", sup1Nutrients[0].GenericName);
            Assert.AreEqual(1, sup2Nutrients.Count);
            Assert.AreEqual("Vitamin C", sup2Nutrients[0].GenericName);
        }

        [TestMethod]
        public async Task Delete_RemovesNutrient()
        {
            var supplementId = await SeedSupplementAsync();

            var nutrient = new SupplementNutrient
            {
                SupplementId = supplementId,
                GenericName = "Iron",
                SpecificForm = "Ferrous Sulfate",
                Dosage = "18mg"
            };
            var id = await _nutrientRepo.AddAsync(nutrient);
            Assert.IsTrue(id > 0);

            // Act – Delete
            await _nutrientRepo.DeleteAsync(id);

            // Assert
            var deleted = await _nutrientRepo.GetByIdAsync(id);
            Assert.IsNull(deleted);

            var all = await _nutrientRepo.GetBySupplementIdAsync(supplementId);
            Assert.AreEqual(0, all.Count);
        }
    }
}