using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VitaTrack.Core.Data;
using VitaTrack.Core.Models;
using VitaTrack.Core.Services;

using VitaTrack.Core.Features.Nutrients;

namespace VitaTrack.Tests;

[TestClass]
public class SupplementNutrientServiceHierarchyTests
{
    private static SupplementNutrientService CreateService(FakeNutrientRepo repo) =>
        new(repo, NullLogger<SupplementNutrientService>.Instance);

    [TestMethod]
    public async Task AddAsync_PersistsHierarchyWithoutDeletingExisting()
    {
        var repo = new FakeNutrientRepo();
        repo.Seed(5, new SupplementNutrient { Id = 5, SupplementId = 1, GenericName = "Old", SpecificForm = "Form", Dosage = "10mg" });
        var svc = CreateService(repo);

        var result = await svc.AddAsync(1,
        [
            new SupplementNutrientDto { GenericName = "New Root", SpecificForm = "Form", Dosage = "20mg" }
        ]);

        Assert.AreEqual(1, result.Saved.Count);
        Assert.AreEqual(0, result.Failures.Count);
        Assert.AreEqual(0, repo.DeletedIds.Count, "AddAsync must not delete existing rows");
        Assert.AreEqual(1, repo.Added.Count(n => n.GenericName == "New Root"));
        Assert.IsNotNull(repo.GetById(5), "Pre-existing rows must remain untouched");
    }

    [TestMethod]
    public async Task NormalizeFlatToHierarchy_MergesChildrenOfNestedChildIntoParent()
    {
        var repo = new FakeNutrientRepo();
        var svc = CreateService(repo);

        // Flat list: root at index 0, a child at index 1 referencing index 0 as parent,
        // where that child itself carries a grandchild in its Children list.
        var grandchild = new SupplementNutrientDto { GenericName = "Grandchild", SpecificForm = "Form", Dosage = "1mg" };
        var nestedChild = new SupplementNutrientDto
        {
            GenericName = "Nested Child",
            SpecificForm = "Form",
            Dosage = "2mg",
            ParentNutrientId = 0,
            Children = [grandchild]
        };
        var root = new SupplementNutrientDto
        {
            GenericName = "Root",
            SpecificForm = "Blend",
            Dosage = "5mg"
        };

        var result = await svc.ReplaceAsync(1, [root, nestedChild]);

        // The nested child and its grandchild both end up persisted under the root.
        Assert.AreEqual(0, result.Failures.Count);
        var names = repo.Added.Select(n => n.GenericName).ToList();
        CollectionAssert.AreEquivalent(new[] { "Root", "Nested Child", "Grandchild" }, names);

        var rootId = repo.Added.Single(n => n.GenericName == "Root").Id;
        Assert.AreEqual(rootId, repo.Added.Single(n => n.GenericName == "Nested Child").ParentNutrientId);
        Assert.AreEqual(rootId, repo.Added.Single(n => n.GenericName == "Grandchild").ParentNutrientId,
            "Grandchild is re-parented onto the same root when its carrying child is nested");
    }

    private sealed class FakeNutrientRepo : ISupplementNutrientRepository
    {
        private int _nextId = 1;
        private readonly Dictionary<int, SupplementNutrient> _byId = new();

        public List<SupplementNutrient> Added { get; } = [];
        public List<int> DeletedIds { get; } = [];

        public void Seed(int id, SupplementNutrient nutrient) => _byId[id] = nutrient;
        public SupplementNutrient? GetById(int id) => _byId.TryGetValue(id, out var n) ? n : null;

        public Task<IReadOnlyList<SupplementNutrient>> GetBySupplementIdAsync(int supplementId) =>
            Task.FromResult<IReadOnlyList<SupplementNutrient>>(
                _byId.Values.Where(n => n.SupplementId == supplementId && n.ParentNutrientId == null).ToList());

        public Task<IReadOnlyList<SupplementNutrient>> GetByParentIdAsync(int parentId) =>
            Task.FromResult<IReadOnlyList<SupplementNutrient>>(
                _byId.Values.Where(n => n.ParentNutrientId == parentId).ToList());

        public Task<IDictionary<int, int>> GetCountsBySupplementIdsAsync(IEnumerable<int> supplementIds) =>
            Task.FromResult<IDictionary<int, int>>(
                supplementIds.ToDictionary(id => id, id => _byId.Values.Count(n => n.SupplementId == id)));

        public Task<SupplementNutrient?> GetByIdAsync(int id) =>
            Task.FromResult(GetById(id));

        public Task<int> AddAsync(SupplementNutrient nutrient)
        {
            nutrient.Id = _nextId++;
            _byId[nutrient.Id] = nutrient;
            Added.Add(nutrient);
            return Task.FromResult(nutrient.Id);
        }

        public Task UpdateAsync(SupplementNutrient nutrient)
        {
            _byId[nutrient.Id] = nutrient;
            return Task.CompletedTask;
        }

        public Task<int> DeleteAsync(int id)
        {
            DeletedIds.Add(id);
            return Task.FromResult(_byId.Remove(id) ? 1 : 0);
        }

        public Task<int> DeleteAsync(IEnumerable<int> ids)
        {
            var count = 0;
            foreach (var id in ids)
            {
                count += _byId.Remove(id) ? 1 : 0;
                DeletedIds.Add(id);
            }
            return Task.FromResult(count);
        }

        public Task DeleteBySupplementIdsAsync(IEnumerable<int> supplementIds)
        {
            var ids = supplementIds.ToHashSet();
            foreach (var id in _byId.Values.Where(n => ids.Contains(n.SupplementId)).Select(n => n.Id).ToList())
            {
                DeletedIds.Add(id);
                _byId.Remove(id);
            }
            return Task.CompletedTask;
        }
    }
}
