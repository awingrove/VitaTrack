using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using YamlDotNet.RepresentationModel;

namespace VitaTrack.ArchitectureTests;

/// <summary>
/// Keeps storymap.yaml honest: every task has a unique id and a navigable
/// entry_point, every test reference resolves to a real test, and no E2E spec
/// is orphaned from the map. A stale or lying story map is treated as a defect.
/// </summary>
[TestClass]
public class StoryMapConsistencyTests
{
    private static readonly Regex E2eRefPattern = new(@"^(?<spec>[a-z0-9-]+)::(?<fragment>.+)$", RegexOptions.Compiled);
    private static readonly Regex UnitRefPattern = new(@"^(?<class>\w+)\.(?<method>\w+)$", RegexOptions.Compiled);

    [TestMethod]
    public void StoryMap_IsValid_AndTestReferencesResolve()
    {
        var repoRoot = FindRepoRoot();
        var path = Path.Combine(repoRoot, "storymap.yaml");
        var yaml = new YamlStream();
        yaml.Load(new StringReader(File.ReadAllText(path)));
        var root = (YamlMappingNode)yaml.Documents[0].RootNode;

        var errors = new List<string>();

        AssertMetaHasLastUpdated(root, errors);
        var referencedSpecs = ValidateTasks(root, repoRoot, errors);
        ValidateNoOrphanSpecs(repoRoot, referencedSpecs, errors);

        Assert.IsTrue(errors.Count == 0,
            "storymap.yaml consistency failures:\n  - " + string.Join("\n  - ", errors));
    }

    private static void AssertMetaHasLastUpdated(YamlMappingNode root, List<string> errors)
    {
        if (!(root.Children.TryGetValue(new YamlScalarNode("meta"), out var metaNode) &&
              metaNode is YamlMappingNode meta &&
              meta.Children.ContainsKey(new YamlScalarNode("last_updated"))))
        {
            errors.Add("meta.last_updated is missing - refresh it when the map changes.");
        }
    }

    /// <returns>Set of spec stems referenced by at least one test entry.</returns>
    private static HashSet<string> ValidateTasks(YamlMappingNode root, string repoRoot, List<string> errors)
    {
        var activities = (YamlSequenceNode)root.Children[new YamlScalarNode("activities")];
        var seenTaskIds = new HashSet<string>();
        var unitSources = ReadAllUnitTestSources(repoRoot);
        var e2eDirectory = Path.Combine(repoRoot, "e2e-tests", "playwright", "tests");
        var referencedSpecs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var activity in activities.OfType<YamlMappingNode>())
        {
            var activityName = Scalar(activity, "name");
            var tasks = activity.Children.TryGetValue(new YamlScalarNode("tasks"), out var t)
                ? ((YamlSequenceNode)t).OfType<YamlMappingNode>()
                : Enumerable.Empty<YamlMappingNode>();

            foreach (var task in tasks)
            {
                var taskName = Scalar(task, "name");

                var id = Scalar(task, "id");
                if (string.IsNullOrEmpty(id))
                    errors.Add($"{activityName}/{taskName}: missing id.");
                else if (!seenTaskIds.Add(id))
                    errors.Add($"duplicate task id '{id}'.");

                var entryPoint = Scalar(task, "entry_point");
                if (string.IsNullOrWhiteSpace(entryPoint))
                    errors.Add($"{id ?? taskName}: missing entry_point.");
                else if (!entryPoint.StartsWith("n/a", StringComparison.OrdinalIgnoreCase) &&
                         !LooksNavigable(entryPoint))
                {
                    errors.Add($"{id}: entry_point '{entryPoint}' does not name a UI location.");
                }

                var stories = task.Children.TryGetValue(new YamlScalarNode("stories"), out var s)
                    ? ((YamlSequenceNode)s).OfType<YamlMappingNode>()
                    : Enumerable.Empty<YamlMappingNode>();

                foreach (var story in stories)
                {
                    if (!story.Children.TryGetValue(new YamlScalarNode("tests"), out var testsNode))
                        continue;

                    foreach (var testRef in ((YamlSequenceNode)testsNode).Select(AsTestRef))
                    {
                        ValidateTestRef(testRef, taskName, e2eDirectory, unitSources, referencedSpecs, errors);
                    }
                }
            }
        }

        return referencedSpecs;
    }

    /// <summary>
    /// Test entries may be quoted scalars ("- unit: X.Y") or single-pair mappings
    /// ("- e2e: spec::title"). Normalize both to "type: value" strings.
    /// </summary>
    private static string AsTestRef(YamlNode node)
    {
        if (node is YamlScalarNode scalar)
            return scalar.Value ?? string.Empty;

        if (node is YamlMappingNode { Children.Count: 1 } mapping)
        {
            var entry = mapping.Children.First();
            return $"{((YamlScalarNode)entry.Key).Value}: {((YamlScalarNode)entry.Value).Value}";
        }

        return string.Empty;
    }

    private static void ValidateTestRef(
        string testRef,
        string taskName,
        string e2eDirectory,
        string[] unitSources,
        HashSet<string> referencedSpecs,
        List<string> errors)
    {
        if (testRef.StartsWith("unit: ", StringComparison.Ordinal))
        {
            var value = testRef["unit: ".Length..].Trim();
            var match = UnitRefPattern.Match(value);
            if (!match.Success || !ResolvesToUnitTest(match.Groups["class"].Value, match.Groups["method"].Value, unitSources))
                errors.Add($"{taskName}: unit ref '{value}' does not resolve to a real test method.");
            return;
        }

        if (testRef.StartsWith("e2e: ", StringComparison.Ordinal))
        {
            var value = testRef["e2e: ".Length..].Trim();
            var match = E2eRefPattern.Match(value);
            if (!match.Success)
            {
                errors.Add($"{taskName}: e2e ref '{value}' must be '<spec-stem>::<title fragment>'.");
                return;
            }

            var stem = match.Groups["spec"].Value;
            var fragment = match.Groups["fragment"].Value;
            var specFile = Path.Combine(e2eDirectory, $"{stem}.spec.js");
            if (!File.Exists(specFile))
            {
                errors.Add($"{taskName}: e2e ref '{value}' - file {stem}.spec.js does not exist.");
                return;
            }

            if (!File.ReadAllText(specFile).Contains(fragment, StringComparison.Ordinal))
                errors.Add($"{taskName}: e2e ref '{value}' - title fragment not found in {stem}.spec.js.");

            referencedSpecs.Add(stem);
            return;
        }

        errors.Add($"{taskName}: test ref '{testRef}' must start with 'unit: ' or 'e2e: '.");
    }

    private static void ValidateNoOrphanSpecs(string repoRoot, HashSet<string> referencedSpecs, List<string> errors)
    {
        var e2eDirectory = Path.Combine(repoRoot, "e2e-tests", "playwright", "tests");
        foreach (var spec in Directory.GetFiles(e2eDirectory, "*.spec.js"))
        {
            var fileName = Path.GetFileName(spec);
            var stem = fileName[..^".spec.js".Length];
            if (!referencedSpecs.Contains(stem))
                errors.Add($"E2E spec '{stem}.spec.js' is not referenced by any story in the map.");
        }
    }

    private static bool LooksNavigable(string entryPoint) =>
        Regex.IsMatch(entryPoint, @"\b(nav|index|row|page|button|link|modal|form|editor|bar)\b", RegexOptions.IgnoreCase);

    private static string Scalar(YamlMappingNode node, string key)
    {
        return node.Children.TryGetValue(new YamlScalarNode(key), out var v) && v is YamlScalarNode s
            ? s.Value ?? string.Empty
            : string.Empty;
    }

    private static bool ResolvesToUnitTest(string className, string methodName, string[] unitSources) =>
        unitSources.Any(source => source.Contains($"class {className}") && Regex.IsMatch(source, $@"\s{methodName}\("));

    private static string[] ReadAllUnitTestSources(string repoRoot)
    {
        var dir = Path.Combine(repoRoot, "VitaTrack.Tests");
        return Directory.GetFiles(dir, "*.cs").Select(File.ReadAllText).ToArray();
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
