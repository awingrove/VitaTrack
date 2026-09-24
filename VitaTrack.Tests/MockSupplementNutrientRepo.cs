using VitaTrack.Core.Data;

using VitaTrack.Core.Features.Nutrients;

namespace VitaTrack.Tests;

/// <summary>
/// In-memory fake of <see cref="ISupplementNutrientRepository"/> for service tests:
/// simulates persistence, records Added/DeletedIds, and can throw on AddAsync
/// for names listed in <see cref="ThrowFor"/>.
/// </summary>
internal sealed class MockRepo : ISupplementNutrientRepository
{
    private int _nextId = 1;
    private readonly Dictionary<int, SupplementNutrient> _byId = new();
    private readonly Dictionary<int, List<SupplementNutrient>> _bySupplement = new();
    private readonly Dictionary<int, List<SupplementNutrient>> _byParent = new();

    public List<SupplementNutrient> Added { get; } = new();
    public List<int> DeletedIds { get; } = new();

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

    public HashSet<string> ThrowFor { get; } = new();

    public Task<int> AddAsync(SupplementNutrient nutrient)
    {
        if (ThrowFor.Contains(nutrient.GenericName))
        {
            throw new InvalidOperationException($"simulated failure for {nutrient.GenericName}");
        }

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
        DeletedIds.Add(id);
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

    public Task DeleteBySupplementIdsAsync(IEnumerable<int> supplementIds)
    {
        var ids = supplementIds.ToHashSet();
        foreach (var id in _byId.Values.Where(n => ids.Contains(n.SupplementId)).Select(n => n.Id).ToList())
        {
            DeleteAsync(id);
        }
        return Task.CompletedTask;
    }
}
