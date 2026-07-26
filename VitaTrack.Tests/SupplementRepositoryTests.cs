using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VitaTrack.Infrastructure.Data;
using VitaTrack.Infrastructure.Models;

namespace VitaTrack.Tests
{
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
    }
}