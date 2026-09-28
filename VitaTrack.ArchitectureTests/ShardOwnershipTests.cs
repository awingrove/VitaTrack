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

    /// <summary>
    /// The artifact kinds a slice may claim, in the order they appear in shards.yaml.
    /// A key missing from a slice is a red build: `models` was added when
    /// <c>VitaTrack.Web/Models</c> was added to <see cref="ScanFeatureFiles"/>, and a
    /// bind model with no owner is exactly the orphan the ownership check exists for.
    /// </summary>
    private static readonly string[] ArtifactKinds =
        ["controller", "core", "views", "models", "js", "unit_tests", "e2e_specs"];

    /// <summary>
    /// Floor on the total number of files the manifest's slices resolve to
    /// (<c>controller</c> + <c>core</c> + <c>views</c> + <c>models</c> + <c>js</c> +
    /// <c>unit_tests</c> + <c>e2e_specs</c>); the allowlist is cross-cutting,
    /// not a slice claim, and is not counted. Today that total is 145, so
    /// 130 = 145 - 15 permits exactly the 15 paths the two smallest slices
    /// claim (SHELL 5, MF 11) to disappear before the floor speaks. That is the
    /// deliberate trade: emptying any of the other five slices' claim lists
    /// takes the total under the floor and fails here even when the files went
    /// with them, because a deleted file orphans nothing and the ownership
    /// check cannot see it. The floor is the only net for a slice whose lists
    /// were emptied outright — <c>[]</c> is a legal declaration, so the loud
    /// loaders stay quiet about it. Ratchet the floor up as the total grows;
    /// lower it only in the change that legitimately removes claimed files.
    /// </summary>
    private const int ClaimedArtifactFloor = 130;

    [TestMethod]
    public void Shards_AreConsistent_AndNoFeatureFileIsOrphaned()
    {
        var repoRoot = RepoLocator.Root();
        var errors = new List<string>();

        var (sliceClaims, allowlist, sliceIds, _) = LoadShards(repoRoot, errors);

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

    /// <summary>
    /// Sentinel for the rule above, which passes vacuously when the claimed
    /// file set is empty: no claims means no double-claims, no orphans, and a
    /// green result. The count clause catches mass shrinkage; the per-slice
    /// clause states the invariant no aggregate count can — a slice that
    /// declares artifacts resolves at least one — and refuses a declaration
    /// that resolves to nothing. Both are derived from the manifest: no slice
    /// id, path, or type is named, so nothing here rots when a slice is added,
    /// renamed, or split.
    /// </summary>
    [TestMethod]
    public void Shards_Claim_A_Non_Trivial_File_Set()
    {
        var repoRoot = RepoLocator.Root();
        // Loader failures (unresolvable declarations, missing keys) belong to
        // the rule above, which asserts on them; this method reports only the
        // sentinel, so a red run names exactly one failure mode.
        var (sliceClaims, _, _, declaredPatternCounts) = LoadShards(repoRoot, new List<string>());

        var errors = new List<string>();
        var totalClaims = sliceClaims.Values.Sum(claims => claims.Count);
        if (totalClaims <= ClaimedArtifactFloor)
            errors.Add($"slices claim {totalClaims} files in total, at or below the floor of "
                + $"{ClaimedArtifactFloor}: the manifest's file set is shrinking out from under "
                + "the ownership check.");

        foreach (var (sliceId, claims) in sliceClaims)
            if (declaredPatternCounts[sliceId] > 0 && claims.Count == 0)
                errors.Add($"shard '{sliceId}' declares {declaredPatternCounts[sliceId]} "
                    + "artifact(s) but resolves none of them.");

        Assert.AreEqual(0, errors.Count,
            "shard claimed-file sentinel failures:\n  - " + string.Join("\n  - ", errors));
    }

    private static (Dictionary<string, List<string>> SliceClaims, List<string> Allowlist,
        List<string> SliceIds, Dictionary<string, int> DeclaredPatternCounts)
        LoadShards(string repoRoot, List<string> errors)
    {
        var path = Path.Combine(repoRoot, "shards.yaml");
        Assert.IsTrue(File.Exists(path), "shards.yaml missing at repo root.");

        var yaml = new YamlStream();
        yaml.Load(new StringReader(File.ReadAllText(path)));
        var root = (YamlMappingNode)yaml.Documents[0].RootNode;

        var sliceClaims = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var declaredPatternCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var sliceIds = new List<string>();

        var slices = (YamlSequenceNode)root.Children[new YamlScalarNode("slices")];
        foreach (var slice in slices.OfType<YamlMappingNode>())
        {
            var id = Scalar(slice, "id");
            Assert.IsFalse(string.IsNullOrEmpty(id), "a shard is missing 'id'.");
            sliceIds.Add(id);

            var claims = new List<string>();
            var declaredPatterns = 0;
            foreach (var key in ArtifactKinds)
            {
                // Key presence, not list count, separates "declared as empty" from
                // "not declared": a slice with no JS says `js: []`; a slice that
                // forgot the key was never reviewed for JS files at all.
                if (!slice.Children.TryGetValue(new YamlScalarNode(key), out var node))
                {
                    errors.Add($"shard '{id}': missing '{key}' declaration (use [] when the slice has no such files).");
                    continue;
                }
                foreach (var raw in ((YamlSequenceNode)node).OfType<YamlScalarNode>())
                {
                    declaredPatterns++;
                    var pattern = raw.Value!;
                    var expanded = Expand(repoRoot, pattern);
                    if (expanded.Count == 0)
                        errors.Add($"shard '{id}': artifact '{pattern}' resolves to no file.");
                    claims.AddRange(expanded);
                }
            }
            sliceClaims[id] = claims;
            declaredPatternCounts[id] = declaredPatterns;
        }

        var allowlist = new List<string>();
        if (root.Children.TryGetValue(new YamlScalarNode("allowlist"), out var al))
            foreach (var raw in ((YamlSequenceNode)al).OfType<YamlScalarNode>())
            {
                var pattern = raw.Value!;
                var expanded = Expand(repoRoot, pattern);
                if (expanded.Count == 0)
                    errors.Add($"allowlist entry '{pattern}' resolves to no file.");
                allowlist.AddRange(expanded);
            }

        return (sliceClaims, allowlist, sliceIds, declaredPatternCounts);
    }

    private static void ValidateStoryMapIds(string repoRoot, List<string> sliceIds, List<string> errors)
    {
        var path = Path.Combine(repoRoot, "storymap.yaml");
        if (!File.Exists(path))
        {
            errors.Add("storymap.yaml missing at repo root: the shard id cross-check did not run.");
            return;
        }

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
            // Bind and view models. Scanned because a slice's models are as much its
            // code as its repository: the first slice to put a file here (SC's
            // SavedConnection projection) was invisible to this check until now.
            Path.Combine(repoRoot, "VitaTrack.Web", "Models"),
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
}
