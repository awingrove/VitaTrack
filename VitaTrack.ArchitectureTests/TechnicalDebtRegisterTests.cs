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

    // A key opening a folded block scalar: four spaces of key indent, then `key: >-`.
    private static readonly Regex FoldedKeyPattern = new(@"^ {4}(?<key>\w+): >-\s*$", RegexOptions.Compiled);

    [TestMethod]
    public void Register_Has_Unique_Ids_And_Complete_Entries_With_Live_Where_Paths()
    {
        var repoRoot = RepoLocator.Root();
        var errors = new List<string>();
        Validate(repoRoot, LoadRegister(repoRoot), errors);
        Assert.AreEqual(0, errors.Count,
            "technical-debt register failures:\n  - " + string.Join("\n  - ", errors));
    }

    [TestMethod]
    public void Validator_Rejects_DuplicateId_MissingPaydown_And_DeadWhere_Path()
    {
        var repoRoot = RepoLocator.Root();
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

    /// <summary>
    /// Rule 6 — every folded scalar (<c>&gt;-</c>) in the register has its content lines at
    /// exactly six spaces: keys sit at four, so six is the only correct content indent.
    /// </summary>
    /// <remarks>
    /// This checks the <em>shape</em> of the file, not its meaning. It catches a malformed
    /// register — a hand-edited scalar whose content drifted off the six-space grid — and it
    /// runs independently of the parser so a bad indent is reported as an indent problem
    /// rather than surfacing as an opaque YamlDotNet message from another test.
    ///
    /// It does **not** catch a silently truncated field, and this remark exists to stop a
    /// later reader assuming it does. That failure was measured, not assumed: a 5-space
    /// continuation is a <c>ParserError</c> and a 4-space cut is a <c>ScannerError</c>, so an
    /// indent error is already loud without this rule. And a *dropped* continuation line —
    /// the genuinely silent case — leaves valid YAML with a shorter field, with no structural
    /// signal at all. Two content thresholds were measured and both fail: a flat 40-character
    /// per-field floor false-positives on 7 legitimate fields (the shortest is
    /// <c>TD-009.where</c> at 24), and a 200-character per-entry volume floor catches zero
    /// real truncations, because all 23 entries absorb a one-line drop. Tightening the volume
    /// floor to 217 would catch it on the tightest entry but leaves 44 characters of margin,
    /// so the first legitimately terse entry breaks the build.
    ///
    /// So truncation is undetectable by a threshold that does not also fail on healthy data.
    /// The honest mitigations are review at the point of edit — the register's header comment
    /// documents the six-space grid — and not pretending otherwise. It is a separate
    /// [TestMethod] because it reads the file as text rather than as a parsed node.
    /// </remarks>
    [TestMethod]
    public void Folded_Scalars_Indent_Their_Content_At_Six_Spaces()
    {
        var repoRoot = RepoLocator.Root();
        var path = Path.Combine(repoRoot, "docs", "factory", "technical-debt.yaml");
        var lines = File.ReadAllLines(path);

        var errors = new List<string>();
        var foldedKeys = 0;
        string? pendingKey = null;
        var pendingKeyLine = 0;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var keyMatch = FoldedKeyPattern.Match(line);
            if (keyMatch.Success)
            {
                pendingKey = keyMatch.Groups["key"].Value;
                pendingKeyLine = i + 1;
                foldedKeys++;
                continue;
            }

            if (pendingKey is null || line.Length == 0) continue;

            var indent = line.Length - line.TrimStart().Length;
            if (indent <= 4)
            {
                pendingKey = null;
                continue;
            }

            if (indent != 6)
                errors.Add($"{Path.GetFileName(path)}:{i + 1}: folded scalar '{pendingKey}' "
                    + $"(declared at line {pendingKeyLine}) has content at {indent} spaces, expected 6.");
        }

        Assert.AreEqual(0, errors.Count,
            "technical-debt register indentation failures:\n  - " + string.Join("\n  - ", errors));
        Assert.IsTrue(foldedKeys > 0,
            $"no folded scalar found in {path} — the rule is not inspecting anything, so it would pass vacuously.");
    }

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
}
