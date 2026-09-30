using VitaTrack.Core.Features.Nutrients;
using VitaTrack.Core.Primitives;

namespace VitaTrack.Core.Features.Supplements;

/// <summary>
/// Pure builder for the comparison grid. Columns stay in input order. Rows
/// merge on <c>GenericName.Trim()</c> compared case-insensitively, with the
/// first sighting's spelling as the label; resolved blend children become rows
/// directly beneath their parent's row (children in first-seen order), orphan
/// children become top-level rows, and top-level rows sort alphabetically by
/// their matched key. Cell amounts pass through <see cref="Dosage.Normalize"/>
/// only — no second unit list, no conversion across unit families.
/// </summary>
public static class SupplementComparisonBuilder
{
    public static ComparisonGrid Build(
        IReadOnlyList<Supplement> columns,
        IReadOnlyDictionary<int, IReadOnlyList<SupplementNutrient>> nutrientsBySupplementId)
    {
        var union = BuildUnion(nutrientsBySupplementId);
        var groups = CollectRowGroups(columns, nutrientsBySupplementId, union);
        var rows = EmitRows(columns, nutrientsBySupplementId, union, groups);
        var comparisonColumns = columns
            .Select(c => new ComparisonColumn(c.Id, c.Name, c.Brand, c.DailyDose))
            .ToList();
        return new ComparisonGrid(comparisonColumns, rows);
    }

    private sealed class RowGroupIndex
    {
        public List<string> TopOrder { get; } = new();
        public Dictionary<string, string> TopLabels { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, List<string>> ChildOrder { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> ChildLabels { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<int, SupplementNutrient> BuildUnion(
        IReadOnlyDictionary<int, IReadOnlyList<SupplementNutrient>> nutrientsBySupplementId)
    {
        var union = new Dictionary<int, SupplementNutrient>();
        foreach (var nutrients in nutrientsBySupplementId.Values)
            foreach (var nutrient in nutrients)
                union.TryAdd(nutrient.Id, nutrient);
        return union;
    }

    private static RowGroupIndex CollectRowGroups(
        IReadOnlyList<Supplement> columns,
        IReadOnlyDictionary<int, IReadOnlyList<SupplementNutrient>> nutrientsBySupplementId,
        Dictionary<int, SupplementNutrient> union)
    {
        var groups = new RowGroupIndex();
        foreach (var column in columns)
        {
            if (!nutrientsBySupplementId.TryGetValue(column.Id, out var nutrients)) continue;
            foreach (var nutrient in nutrients)
            {
                var parent = ResolveParent(nutrient, union);
                if (parent == null)
                {
                    EnsureTopLevel(groups, KeyOf(nutrient), nutrient.GenericName);
                    continue;
                }

                var parentKey = KeyOf(parent);
                EnsureTopLevel(groups, parentKey, parent.GenericName);

                var childKey = KeyOf(nutrient);
                var composed = Compose(parentKey, childKey);
                if (groups.ChildLabels.ContainsKey(composed)) continue;

                if (!groups.ChildOrder.TryGetValue(parentKey, out var siblings))
                {
                    siblings = new List<string>();
                    groups.ChildOrder[parentKey] = siblings;
                }
                siblings.Add(childKey);
                groups.ChildLabels[composed] = nutrient.GenericName;
            }
        }
        return groups;
    }

    private static void EnsureTopLevel(RowGroupIndex groups, string key, string label)
    {
        if (groups.TopLabels.ContainsKey(key)) return;
        groups.TopOrder.Add(key);
        groups.TopLabels[key] = label;
    }

    private static List<ComparisonRow> EmitRows(
        IReadOnlyList<Supplement> columns,
        IReadOnlyDictionary<int, IReadOnlyList<SupplementNutrient>> nutrientsBySupplementId,
        Dictionary<int, SupplementNutrient> union,
        RowGroupIndex groups)
    {
        var rows = new List<ComparisonRow>();
        foreach (var topKey in groups.TopOrder.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
        {
            rows.Add(BuildRow(columns, nutrientsBySupplementId, union,
                groups.TopLabels[topKey], isBlendChild: false, parentScope: null, rowKey: topKey));

            if (!groups.ChildOrder.TryGetValue(topKey, out var children)) continue;
            foreach (var childKey in children)
            {
                rows.Add(BuildRow(columns, nutrientsBySupplementId, union,
                    groups.ChildLabels[Compose(topKey, childKey)], isBlendChild: true,
                    parentScope: topKey, rowKey: childKey));
            }
        }
        return rows;
    }

    private static ComparisonRow BuildRow(
        IReadOnlyList<Supplement> columns,
        IReadOnlyDictionary<int, IReadOnlyList<SupplementNutrient>> nutrientsBySupplementId,
        Dictionary<int, SupplementNutrient> union,
        string label,
        bool isBlendChild,
        string? parentScope,
        string rowKey)
    {
        var cells = columns
            .Select(c => nutrientsBySupplementId.TryGetValue(c.Id, out var nutrients)
                ? FindCell(nutrients, union, parentScope, rowKey)
                : null)
            .ToList();
        return new ComparisonRow(label, isBlendChild, cells);
    }

    private static ComparisonCell? FindCell(
        IReadOnlyList<SupplementNutrient> nutrients,
        Dictionary<int, SupplementNutrient> union,
        string? parentScope,
        string rowKey)
    {
        foreach (var nutrient in nutrients)
        {
            var parent = ResolveParent(nutrient, union);
            if (parentScope == null)
            {
                // A top-level row matches only top-level (or orphaned) nutrients,
                // so a blend child cannot fill it with its own key.
                if (parent != null || !KeyMatches(KeyOf(nutrient), rowKey)) continue;
            }
            else
            {
                // A child row matches within its parent group only.
                if (parent == null || !KeyMatches(KeyOf(parent), parentScope)) continue;
                if (!KeyMatches(KeyOf(nutrient), rowKey)) continue;
            }
            return new ComparisonCell(nutrient.SpecificForm, Dosage.Normalize(nutrient.Dosage));
        }
        return null;
    }

    private static SupplementNutrient? ResolveParent(SupplementNutrient nutrient, Dictionary<int, SupplementNutrient> union)
        => nutrient.ParentNutrientId is int parentId && union.TryGetValue(parentId, out var parent)
            ? parent
            : null;

    private static string KeyOf(SupplementNutrient nutrient) => nutrient.GenericName.Trim();

    private static bool KeyMatches(string key, string rowKey)
        => string.Equals(key, rowKey, StringComparison.OrdinalIgnoreCase);

    private static string Compose(string parentKey, string childKey) => parentKey + '\0' + childKey;
}
