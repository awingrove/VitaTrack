using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using YamlDotNet.RepresentationModel;

namespace VitaTrack.ArchitectureTests;

/// <summary>
/// Enforces the shard metrics ledger (docs/factory/shard-metrics.yaml): entries are
/// unique, every entry maps to a real shard in shards.yaml, and required fields are
/// present. This keeps the productivity measurements (metrics.md) non-optional —
/// a shipped slice without a ledger entry is a defect, not an oversight.
/// </summary>
[TestClass]
public class ShardMetricsLedgerTests
{
    private static readonly string[] RequiredFields =
        ["agent", "human_interventions", "guardrail_failures", "fix_commits", "defects_escaped"];

    private static readonly string[] NumericUsageFields =
        ["tokens_input", "tokens_output", "tokens_reasoning", "tokens_cache_read", "cost_usd"];

    [TestMethod]
    public void Ledger_Entries_Resolve_To_Real_Shards_And_Carry_Required_Fields()
    {
        var repoRoot = FindRepoRoot();
        var errors = new List<string>();

        var sliceIds = LoadSliceIds(repoRoot);
        var entries = LoadLedgerEntries(repoRoot);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, fields) in entries)
        {
            if (string.IsNullOrEmpty(id))
            {
                errors.Add("a ledger entry is missing 'id'.");
                continue;
            }
            if (!seen.Add(id))
                errors.Add($"ledger entry '{id}' appears more than once.");
            if (!sliceIds.Contains(id, StringComparer.OrdinalIgnoreCase))
                errors.Add($"ledger entry '{id}' has no matching shard in shards.yaml.");
            foreach (var field in RequiredFields)
            {
                if (!fields.TryGetValue(field, out var value) || string.IsNullOrEmpty(value))
                    errors.Add($"ledger entry '{id}' is missing required field '{field}'.");
            }
            foreach (var field in RequiredFields.Skip(1))
            {
                if (fields.TryGetValue(field, out var value)
                    && (!int.TryParse(value, out var n) || n < 0))
                    errors.Add($"ledger entry '{id}' field '{field}' must be a non-negative integer.");
            }

            foreach (var field in NumericUsageFields)
            {
                if (fields.TryGetValue(field, out var value)
                    && (!decimal.TryParse(value, out var n) || n < 0))
                    errors.Add($"ledger entry '{id}' optional field '{field}' must be a non-negative number when present.");
            }
            if (fields.TryGetValue("agent_model", out var model) && string.IsNullOrWhiteSpace(model))
                errors.Add($"ledger entry '{id}' field 'agent_model' must be a non-empty provider/model when present.");
        }

        Assert.AreEqual(0, errors.Count,
            "shard metrics ledger failures:\n  - " + string.Join("\n  - ", errors));
    }

    private static List<string> LoadSliceIds(string repoRoot)
    {
        var root = (YamlMappingNode)LoadYaml(Path.Combine(repoRoot, "shards.yaml"));
        var slices = (YamlSequenceNode)root.Children[new YamlScalarNode("slices")];
        return slices.OfType<YamlMappingNode>()
            .Select(s => Scalar(s, "id"))
            .ToList();
    }

    private static List<(string Id, Dictionary<string, string> Fields)> LoadLedgerEntries(string repoRoot)
    {
        var path = Path.Combine(repoRoot, "docs", "factory", "shard-metrics.yaml");
        Assert.IsTrue(File.Exists(path), "docs/factory/shard-metrics.yaml missing.");

        var root = (YamlMappingNode)LoadYaml(path);
        Assert.IsTrue(root.Children.ContainsKey(new YamlScalarNode("shards")),
            "shard-metrics.yaml must have a top-level 'shards:' sequence.");
        var shards = (YamlSequenceNode)root.Children[new YamlScalarNode("shards")];
        return shards.OfType<YamlMappingNode>()
            .Select(entry => (
                Scalar(entry, "id"),
                entry.Children.Where(kv => kv.Key is YamlScalarNode && kv.Value is YamlScalarNode)
                    .ToDictionary(kv => ((YamlScalarNode)kv.Key).Value!, kv => ((YamlScalarNode)kv.Value!).Value ?? "")))
            .ToList();
    }

    private static YamlNode LoadYaml(string path)
    {
        var yaml = new YamlStream();
        yaml.Load(new StringReader(File.ReadAllText(path)));
        return yaml.Documents[0].RootNode;
    }

    private static string Scalar(YamlMappingNode node, string key)
        => node.Children.TryGetValue(new YamlScalarNode(key), out var v) && v is YamlScalarNode s
            ? s.Value ?? string.Empty
            : string.Empty;

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "VitaTrack.sln")))
            dir = dir.Parent;
        Assert.IsNotNull(dir, "Could not locate repo root (VitaTrack.sln not found).");
        return dir!.FullName;
    }
}
