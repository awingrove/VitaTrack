using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VitaTrack.Infrastructure.Data;
using VitaTrack.Infrastructure.Models;
using VitaTrack.Infrastructure.Services;

namespace VitaTrack.Tests;

[TestClass]
public class SupplementNutrientServiceTests
{
    private static readonly ILogger<SupplementNutrientService> NullLogger =
        NullLogger<SupplementNutrientService>.Instance;

    [TestMethod]
    public async Task PersistHierarchy_StoresChildrenWithParentId()
    {
        var repo = new MockRepo();
        var svc = new SupplementNutrientService(repo, NullLogger);
        var blend = new SupplementNutrientDto
        {
            GenericName = "Proprietary Blend",
            SpecificForm = "Blend",
            Dosage = "500mg",
            Children = [new SupplementNutrientDto { GenericName = "Zinc", SpecificForm = "Picolinate" }]
        };

        var result = await svc.PersistHierarchyAsync(1, [blend]);

        Assert.AreEqual(2, result.Saved.Count);
        var child = repo.Added.Single(n => n.GenericName == "Zinc");
        Assert.IsTrue(child.ParentNutrientId > 0);
    }

    [TestMethod]
    public async Task PersistHierarchy_TopLevelMissingDosage_Fails()
    {
        var repo = new MockRepo();
        var svc = new SupplementNutrientService(repo, NullLogger);
        var result = await svc.PersistHierarchyAsync(
            1, [new SupplementNutrientDto { GenericName = "Vit C", SpecificForm = "Ascorbic" }]);
        Assert.IsTrue(result.Failures.Any(f => f.GenericName == "Vit C"));
    }

    [TestMethod]
    public async Task ReplaceAsync_DeletesExistingThenPersistsHierarchy()
    {
        var repo = new MockRepo();
        var svc = new SupplementNutrientService(repo, NullLogger);
        await repo.AddAsync(new SupplementNutrient { SupplementId = 1, GenericName = "Old", SpecificForm = "x", Dosage = "1mg" });

        var result = await svc.ReplaceAsync(1, [new SupplementNutrientDto
        {
            GenericName = "Proprietary Blend",
            SpecificForm = "Blend",
            Dosage = "500mg",
            Children = [new SupplementNutrientDto { GenericName = "Zinc", SpecificForm = "Picolinate" }]
        }]);

        var remaining = await repo.GetBySupplementIdAsync(1);
        Assert.IsFalse(remaining.Any(n => n.GenericName == "Old"));
        Assert.AreEqual(2, result.Saved.Count);
        Assert.IsTrue(repo.Added.Single(n => n.GenericName == "Zinc").ParentNutrientId > 0);
    }

    private sealed class MockRepo : ISupplementNutrientRepository
    {
        private int _nextId = 1;
        private readonly Dictionary<int, SupplementNutrient> _byId = new();
        private readonly Dictionary<int, List<SupplementNutrient>> _bySupplement = new();
        private readonly Dictionary<int, List<SupplementNutrient>> _byParent = new();

        public List<SupplementNutrient> Added { get; } = new();

        public Task<IReadOnlyList<SupplementNutrient>> GetBySupplementIdAsync(int supplementId)
        {
            var list = _bySupplement.TryGetValue(supplementId, out var v)
                ? v.Where(n => n.ParentNutrientId == null).ToList()
                : new List<SupplementNutrient>();
            return Task.FromResult<IReadOnlyList<SupplementNutrient>>(list);
        }

        public Task<IReadOnlyList<SupplementNutrient>> GetByParentIdAsync(int parentId)
        {
            var list = _byParent.TryGetValue(parentId, out var v)
                ? v.ToList()
                : new List<SupplementNutrient>();
            return Task.FromResult<IReadOnlyList<SupplementNutrient>>(list);
        }

        public Task<IDictionary<int, int>> GetCountsBySupplementIdsAsync(IEnumerable<int> supplementIds)
        {
            var dict = supplementIds.ToDictionary(id => id, id =>
                _bySupplement.TryGetValue(id, out var v) ? v.Count : 0);
            return Task.FromResult<IDictionary<int, int>>(dict);
        }

        public Task<SupplementNutrient?> GetByIdAsync(int id)
        {
            _byId.TryGetValue(id, out var n);
            return Task.FromResult(n);
        }

        public Task<int> AddAsync(SupplementNutrient nutrient)
        {
            nutrient.Id = _nextId++;
            _byId[nutrient.Id] = nutrient;
            Added.Add(nutrient);
            if (!_bySupplement.TryGetValue(nutrient.SupplementId, out var s))
            {
                s = new List<SupplementNutrient>();
                _bySupplement[nutrient.SupplementId] = s;
            }
            s.Add(nutrient);
            if (nutrient.ParentNutrientId.HasValue)
            {
                if (!_byParent.TryGetValue(nutrient.ParentNutrientId.Value, out var p))
                {
                    p = new List<SupplementNutrient>();
                    _byParent[nutrient.ParentNutrientId.Value] = p;
                }
                p.Add(nutrient);
            }
            return Task.FromResult(nutrient.Id);
        }

        public Task UpdateAsync(SupplementNutrient nutrient)
        {
            _byId[nutrient.Id] = nutrient;
            return Task.CompletedTask;
        }

        public Task<int> DeleteAsync(int id)
        {
            if (_byId.Remove(id, out var n))
            {
                if (_bySupplement.TryGetValue(n.SupplementId, out var s))
                {
                    s.RemoveAll(x => x.Id == id);
                }
                if (n.ParentNutrientId.HasValue && _byParent.TryGetValue(n.ParentNutrientId.Value, out var p))
                {
                    p.RemoveAll(x => x.Id == id);
                }
            }
            return Task.FromResult(1);
        }

        public async Task<int> DeleteAsync(IEnumerable<int> ids)
        {
            var count = 0;
            foreach (var id in ids)
            {
                count += await DeleteAsync(id);
            }
            return count;
        }
    }
}
