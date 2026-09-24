using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using YamlDotNet.RepresentationModel;

namespace VitaTrack.ArchitectureTests;

/// <summary>
/// Enforces the feature-shard ownership index (shards.yaml): every feature source
/// file belongs to exactly one shard (no orphans, no double-claim), every listed
/// artifact resolves to a real file, and shard ids are consistent with the
/// story-map task ids. The index is the agent's code↔feature map.
/// </summary>
[TestClass]
public class ShardOwnershipTests
{
    private static readonly HashSet<string> ExcludeDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", "node_modules", "playwright-report", "test-results", "TestResults"
    };

    [TestMethod]
    public void Shards_AreConsistent_AndNoFeatureFileIsOrphaned()
    {
        var repoRoot = FindRepoRoot();
        var errors = new List<string>();

        var (sliceClaims, allowlist, sliceIds) = LoadShards(repoRoot, errors);

        // No double-claim within slices.
        var claimCounts = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (sliceId, files) in sliceClaims)
            foreach (var f in files)
            {
                if (!claimCounts.TryGetValue(f, out var owners)) claimCounts[f] = owners = new();
                owners.Add(sliceId);
            }
        foreach (var (file, owners) in claimCounts.Where(kv => kv.Value.Count > 1))
            errors.Add($"file '{file}' claimed by multiple shards: {string.Join(", ", owners)}");

        // Allowlist must not overlap slice claims.
        foreach (var a in allowlist)
            if (claimCounts.ContainsKey(a))
                errors.Add($"allowlisted file '{a}' is also claimed by a shard.");

        var allOwned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in claimCounts.Keys) allOwned.Add(f);
        foreach (var a in allowlist) allOwned.Add(a);

        // Every scanned feature file is owned.
        foreach (var scanned in ScanFeatureFiles(repoRoot))
        {
            if (!allOwned.Contains(scanned))
                errors.Add($"orphan feature file (not in any shard or allowlist): {scanned}");
        }

        // Story-map id integrity.
        ValidateStoryMapIds(repoRoot, sliceIds, errors);

        Assert.AreEqual(0, errors.Count,
            "shard ownership failures:\n  - " + string.Join("\n  - ", errors));
    }

    private static (Dictionary<string, List<string>> SliceClaims, List<string> Allowlist, List<string> SliceIds)
        LoadShards(string repoRoot, List<string> errors)
    {
        var path = Path.Combine(repoRoot, "shards.yaml");
        Assert.IsTrue(File.Exists(path), "shards.yaml missing at repo root.");

        var yaml = new YamlStream();
        yaml.Load(new StringReader(File.ReadAllText(path)));
        var root = (YamlMappingNode)yaml.Documents[0].RootNode;

        var sliceClaims = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var sliceIds = new List<string>();

        var slices = (YamlSequenceNode)root.Children[new YamlScalarNode("slices")];
        foreach (var slice in slices.OfType<YamlMappingNode>())
        {
            var id = Scalar(slice, "id");
            Assert.IsFalse(string.IsNullOrEmpty(id), "a shard is missing 'id'.");
            sliceIds.Add(id);

            var claims = new List<string>();
            foreach (var key in new[] { "controller", "core", "views", "js", "unit_tests", "e2e_specs" })
            {
                if (!slice.Children.TryGetValue(new YamlScalarNode(key), out var node)) continue;
                foreach (var raw in ((YamlSequenceNode)node).OfType<YamlScalarNode>())
                {
                    var pattern = raw.Value!;
                    var expanded = Expand(repoRoot, pattern);
                    if (expanded.Count == 0)
                        errors.Add($"shard '{id}': artifact '{pattern}' resolves to no file.");
                    claims.AddRange(expanded);
                }
            }
            sliceClaims[id] = claims;
        }

        var allowlist = new List<string>();
        if (root.Children.TryGetValue(new YamlScalarNode("allowlist"), out var al))
            foreach (var raw in ((YamlSequenceNode)al).OfType<YamlScalarNode>())
                allowlist.AddRange(Expand(repoRoot, raw.Value!));

        return (sliceClaims, allowlist, sliceIds);
    }

    private static void ValidateStoryMapIds(string repoRoot, List<string> sliceIds, List<string> errors)
    {
        var path = Path.Combine(repoRoot, "storymap.yaml");
        if (!File.Exists(path)) return;

        var yaml = new YamlStream();
        yaml.Load(new StringReader(File.ReadAllText(path)));
        var root = (YamlMappingNode)yaml.Documents[0].RootNode;
        var activities = (YamlSequenceNode)root.Children[new YamlScalarNode("activities")];

        var prefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var activity in activities.OfType<YamlMappingNode>())
        {
            if (!activity.Children.TryGetValue(new YamlScalarNode("tasks"), out var t)) continue;
            foreach (var task in ((YamlSequenceNode)t).OfType<YamlMappingNode>())
            {
                var id = Scalar(task, "id");
                if (string.IsNullOrEmpty(id)) continue;
                var prefix = id.Split('-')[0];
                prefixes.Add(prefix);
            }
        }

        foreach (var sliceId in sliceIds)
            if (!prefixes.Contains(sliceId))
                errors.Add($"shard '{sliceId}' has no matching story-map task id.");
        foreach (var prefix in prefixes)
            if (!sliceIds.Contains(prefix))
                errors.Add($"story-map prefix '{prefix}' has no shard entry in shards.yaml.");
    }

    private static List<string> ScanFeatureFiles(string repoRoot)
    {
        var roots = new[]
        {
            Path.Combine(repoRoot, "VitaTrack.Web", "Controllers"),
            Path.Combine(repoRoot, "VitaTrack.Core"),
            Path.Combine(repoRoot, "VitaTrack.Web", "Views"),
            Path.Combine(repoRoot, "VitaTrack.Web", "wwwroot", "js"),
            Path.Combine(repoRoot, "VitaTrack.Tests"),
            Path.Combine(repoRoot, "e2e-tests", "playwright", "tests"),
        };

        var result = new List<string>();
        foreach (var root in roots)
        {
            if (!Directory.Exists(root)) continue;
            var ext = Path.GetFileName(root) == "tests" ? "*.spec.js"
                : Path.GetFileName(root) == "js" ? "*.js"
                : "*.cs";
            if (root.EndsWith("Views")) ext = "*.cshtml";
            foreach (var file in Directory.EnumerateFiles(root, ext, SearchOption.AllDirectories))
            {
                if (file.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                        .Any(seg => ExcludeDirs.Contains(seg)))
                    continue;
                result.Add(Path.GetRelativePath(repoRoot, file).Replace('\\', '/'));
            }
        }
        return result;
    }

    private static List<string> Expand(string repoRoot, string pattern)
    {
        var matched = new List<string>();
        if (pattern.EndsWith("/**"))
        {
            var dir = Path.Combine(repoRoot, pattern[..^3].Replace('/', Path.DirectorySeparatorChar));
            if (Directory.Exists(dir))
                matched.AddRange(Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                    .Select(f => Path.GetRelativePath(repoRoot, f).Replace('\\', '/')));
            return matched;
        }

        if (pattern.Contains('*'))
        {
            var dir = Path.GetDirectoryName(Path.Combine(repoRoot, pattern.Replace('/', Path.DirectorySeparatorChar)))!;
            var basePattern = Path.GetFileName(pattern).Replace("*", ".*");
            if (Directory.Exists(dir))
                matched.AddRange(Directory.EnumerateFiles(dir, "*", SearchOption.TopDirectoryOnly)
                    .Where(f => System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(f), basePattern))
                    .Select(f => Path.GetRelativePath(repoRoot, f).Replace('\\', '/')));
            return matched;
        }

        var exact = Path.Combine(repoRoot, pattern.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(exact))
            matched.Add(pattern);
        return matched;
    }

    private static string Scalar(YamlMappingNode node, string key)
    {
        return node.Children.TryGetValue(new YamlScalarNode(key), out var v) && v is YamlScalarNode s
            ? s.Value ?? string.Empty
            : string.Empty;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "VitaTrack.sln")))
            dir = dir.Parent;
        Assert.IsNotNull(dir, "Could not locate repo root (VitaTrack.sln not found).");
        return dir!.FullName;
    }
}
