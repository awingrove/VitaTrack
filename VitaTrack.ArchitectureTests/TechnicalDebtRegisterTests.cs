using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using YamlDotNet.RepresentationModel;

namespace VitaTrack.ArchitectureTests;

/// <summary>
/// Enforces docs/factory/technical-debt.yaml: ids are unique and well-formed, an entry
/// carries the fields its side requires, and every path-shaped `where` on an open entry
/// still resolves. DL-004 lost an open entry to a merge conflict because nothing
/// machine-checked the register, so a dropped or duplicated entry is a red build.
/// </summary>
/// <remarks>
/// Rule 5 covers open entries only, by decision: a closed entry's `where` is historical
/// (TD-001 and TD-003 read "was ... (deleted)"), and the schema table marks `where`
/// optional for `closed` precisely because the file it names may no longer exist.
/// </remarks>
[TestClass]
public class TechnicalDebtRegisterTests
{
    private static readonly string[] OpenRequired = ["id", "title", "where", "what", "interest", "paydown"];

    private static readonly string[] ClosedRequired = ["id", "title", "closed", "note"];

    private static readonly string[] DefectRequired =
        ["id", "title", "found", "injection_stage", "detection_stage", "systemic_gap"];

    private static readonly string[] OpenForbidden = ["closed", "note"];

    private static readonly string[] PathRoots =
    [
        "VitaTrack.Web/", "VitaTrack.Core/", "VitaTrack.Tests/", "VitaTrack.ArchitectureTests/",
        "scripts/", "e2e-tests/", "docs/", ".github/", ".opencode/", "shards.yaml", "storymap.yaml"
    ];

    private static readonly Regex IdPattern = new(@"^(TD|DL)-\d{3}$", RegexOptions.Compiled);
    private static readonly Regex BacktickSpan = new(@"`([^`]*)`", RegexOptions.Compiled);

    // A `where` reference may carry a line number or a line *range* — TD-016 is the
    // one entry using `FileSizeTests.cs:45-63`, so both forms are stripped.
    private static readonly Regex LineSuffix = new(@":\d+(-\d+)?$", RegexOptions.Compiled);

    [TestMethod]
    public void Register_Has_Unique_Ids_And_Complete_Entries_With_Live_Where_Paths()
    {
        var repoRoot = FindRepoRoot();
        var errors = new List<string>();
        Validate(repoRoot, LoadRegister(repoRoot), errors);
        Assert.AreEqual(0, errors.Count,
            "technical-debt register failures:\n  - " + string.Join("\n  - ", errors));
    }

    [TestMethod]
    public void Validator_Rejects_DuplicateId_MissingPaydown_And_DeadWhere_Path()
    {
        var repoRoot = FindRepoRoot();
        const string malformed = """
            open:
              - id: TD-900
                title: >-
                  duplicate id
                where: >-
                  `VitaTrack.Core/Features/Nope/Nope.cs`
                what: >-
                  x
                interest: >-
                  x
                paydown: >-
                  x
              - id: TD-900
                title: >-
                  duplicate id again
                where: >-
                  `docs/factory/technical-debt.yaml`
                what: >-
                  x
                interest: >-
                  x
            closed: []
            defects: []
            """;

        var errors = new List<string>();
        Validate(repoRoot, Parse(malformed), errors);
        var report = string.Join(" | ", errors);

        Assert.IsTrue(Has(errors, "appears more than once", "TD-900"),
            "a duplicate id inside one collection must be reported: " + report);
        Assert.IsTrue(Has(errors, "missing required field 'paydown'"),
            "a missing required field must be reported: " + report);
        Assert.IsTrue(Has(errors, "does not exist"),
            "an unresolvable where path must be reported: " + report);
    }

    private static bool Has(List<string> errors, params string[] fragments)
        => errors.Any(e => fragments.All(f => e.Contains(f, StringComparison.Ordinal)));

    private static void Validate(string repoRoot, YamlMappingNode root, List<string> errors)
    {
        var open = Entries(root, "open");
        var closed = Entries(root, "closed");
        CheckIds(open, "TD", errors);
        CheckIds(closed, "TD", errors);
        CheckIds(Entries(root, "defects"), "DL", errors);

        var closedIds = closed.Select(e => Scalar(e, "id")).ToHashSet(StringComparer.Ordinal);
        foreach (var entry in open)
        {
            var id = Scalar(entry, "id");
            if (closedIds.Contains(id))
                errors.Add($"'{id}' appears in both open and closed.");
        }

        CheckFields(open, OpenRequired, errors);
        CheckFields(closed, ClosedRequired, errors);
        CheckFields(Entries(root, "defects"), DefectRequired, errors);
        foreach (var entry in open)
        {
            foreach (var field in OpenForbidden)
            {
                if (!string.IsNullOrEmpty(Scalar(entry, field)))
                    errors.Add($"open entry '{Label(entry)}' carries forbidden field '{field}'.");
            }
        }

        // Rule 5, open entries only — a closed entry's `where` names a historical file.
        foreach (var entry in open)
            CheckWherePaths(repoRoot, Scalar(entry, "where"), Label(entry), errors);
    }

    private static void CheckIds(List<YamlMappingNode> entries, string prefix, List<string> errors)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            var id = Scalar(entry, "id");
            if (!IdPattern.IsMatch(id))
                errors.Add($"'{id}' is not a well-formed {prefix}-NNN id.");
            if (!seen.Add(id))
                errors.Add($"'{id}' appears more than once in the same collection.");
        }
    }

    private static void CheckFields(List<YamlMappingNode> entries, string[] required, List<string> errors)
    {
        foreach (var entry in entries)
        {
            foreach (var field in required)
            {
                if (string.IsNullOrEmpty(Scalar(entry, field)))
                    errors.Add($"entry '{Label(entry)}' is missing required field '{field}'.");
            }
        }
    }

    private static void CheckWherePaths(string repoRoot, string where, string id, List<string> errors)
    {
        foreach (Match match in BacktickSpan.Matches(where))
        {
            var span = match.Groups[1].Value.TrimEnd('*');
            if (!PathRoots.Any(span.StartsWith)) continue;
            if (span.Contains(' ', StringComparison.Ordinal)) continue;
            if (span.EndsWith("/**", StringComparison.Ordinal)) continue;
            var relative = LineSuffix.Replace(span, string.Empty);
            // Directory.Exists is required, not redundant: TD-010's `where` names `.opencode/`,
            // a directory, and File.Exists returns false for one. Do not "simplify" this away.
            if (!File.Exists(Path.Combine(repoRoot, relative)) && !Directory.Exists(Path.Combine(repoRoot, relative)))
                errors.Add($"'{id}' where path '{relative}' does not exist.");
        }
    }

    private static List<YamlMappingNode> Entries(YamlMappingNode root, string key)
    {
        if (!root.Children.TryGetValue(new YamlScalarNode(key), out var node) || node is not YamlSequenceNode seq)
            return [];
        return seq.Children.OfType<YamlMappingNode>().ToList();
    }

    private static string Label(YamlMappingNode node)
    {
        var id = Scalar(node, "id");
        return string.IsNullOrEmpty(id) ? "<no id>" : id;
    }

    private static string Scalar(YamlMappingNode node, string key)
        => node.Children.TryGetValue(new YamlScalarNode(key), out var v) && v is YamlScalarNode s
            ? s.Value ?? string.Empty
            : string.Empty;

    private static YamlMappingNode LoadRegister(string repoRoot)
    {
        var path = Path.Combine(repoRoot, "docs", "factory", "technical-debt.yaml");
        Assert.IsTrue(File.Exists(path), "docs/factory/technical-debt.yaml missing.");
        return Parse(File.ReadAllText(path));
    }

    private static YamlMappingNode Parse(string yaml)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(yaml));
        return (YamlMappingNode)stream.Documents[0].RootNode;
    }

    // The 8th copy of this walk. A shared RepoLocator.Root() exists on the unmerged
    // guardrail-self-verification branch; adding one here would collide with it on merge.
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "VitaTrack.sln")))
            dir = dir.Parent;
        Assert.IsNotNull(dir, "Could not locate repo root (VitaTrack.sln not found).");
        return dir.FullName;
    }
}
