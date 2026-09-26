using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using YamlDotNet.RepresentationModel;

namespace VitaTrack.ArchitectureTests;

/// <summary>
/// Enforces the ADR-0006 cross-slice invariant against the explicit
/// table→slice map in shards.yaml: every SQL table referenced from a slice's
/// core files must be declared in that slice's `tables` list. Raw SQL against
/// an undeclared (typically another slice's) table fails — the TD-005 defect
/// class. String literals that look like SQL statements are scanned; log
/// messages that merely contain words like "from" are not.
/// </summary>
[TestClass]
public class CrossSliceSqlTests
{
    private static readonly Regex VerbatimLiteral = new(
        "@\"(?:[^\"]|\"\")*\"",
        RegexOptions.Compiled);

    private static readonly Regex RegularLiteral = new(
        "\"(?:[^\"\\\\\\r\\n]|\\\\.)*\"",
        RegexOptions.Compiled);

    private static readonly Regex SqlStatement = new(
        @"\bSELECT\b[\s\S]*?\bFROM\b|DELETE\s+FROM\b|INSERT\s+INTO\b|\bUPDATE\s+\w+\s+SET\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex TableReference = new(
        @"\b(?:FROM|JOIN|INTO|UPDATE)\s+([A-Za-z_][A-Za-z_0-9]*)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    [TestMethod]
    public void Sql_References_Only_Tables_Declared_By_The_Slice()
    {
        var repoRoot = RepoLocator.Root();
        var errors = new List<string>();

        foreach (var (sliceId, corePaths, tables) in LoadSlices(repoRoot, errors))
        {
            var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var relativePath in corePaths)
            {
                var fullPath = Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(fullPath))
                {
                    errors.Add($"shard '{sliceId}': artifact '{relativePath}' resolves to no file.");
                    continue;
                }
                foreach (var sql in ExtractSqlStrings(File.ReadAllText(fullPath)))
                    foreach (var table in ExtractTableReferences(sql))
                        referenced.Add(table);
            }

            foreach (var table in referenced.Where(t => !tables.Contains(t, StringComparer.OrdinalIgnoreCase)))
                errors.Add($"shard '{sliceId}': SQL references table '{table}' not declared in its 'tables' list.");
        }

        Assert.AreEqual(0, errors.Count,
            "cross-slice SQL failures:\n  - " + string.Join("\n  - ", errors));
    }

    /// <summary>
    /// Sentinel for the rule above: a slice that declares <c>tables</c> but
    /// claims no <c>core</c> files issues SQL that nothing will ever be scanned
    /// for, and the rule above still passes — "no files, no violations" is a
    /// green result (the TD-006 shape). Derived from the manifest rather than
    /// from a named slice, so it cannot rot when a slice is added, renamed, or
    /// legitimately split; a slice with <c>tables: []</c> issues no SQL and is
    /// vacuously exempt.
    /// </summary>
    [TestMethod]
    public void Sql_Slices_Claiming_Tables_Also_Claim_Core_Files()
    {
        // Loader failures (absent declarations, wildcards) belong to the rule
        // above, which asserts on them; this method reports only the sentinel,
        // so a red run names exactly one failure mode.
        var slices = LoadSlices(RepoLocator.Root(), new List<string>());

        var silent = slices
            .Where(s => s.Tables.Count > 0 && s.CorePaths.Count == 0)
            .Select(s => s.SliceId)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        Assert.AreEqual(0, silent.Count,
            "slices declaring 'tables' but claiming no 'core' files are scanning nothing: "
            + string.Join(", ", silent));
    }

    /// <summary>
    /// From C# source, yields every string literal that looks like a SQL
    /// statement. Prose log messages that merely contain words like "from"
    /// do not match the statement shape and are skipped.
    /// </summary>
    internal static IEnumerable<string> ExtractSqlStrings(string source)
        => VerbatimLiteral.Matches(source).Cast<Match>()
            .Concat(RegularLiteral.Matches(source).Cast<Match>())
            .Select(m => m.Value)
            .Where(l => SqlStatement.IsMatch(l));

    /// <summary>From a SQL statement, yields the referenced table names.</summary>
    internal static IEnumerable<string> ExtractTableReferences(string sql)
        => TableReference.Matches(sql).Cast<Match>().Select(m => m.Groups[1].Value);

    [TestMethod]
    public void Sql_Extractors_Parse_Exemplar_Literals_And_Ignore_Prose()
    {
        // Real SQL from the PD exemplar: multi-line verbatim literal, joins,
        // every statement shape — reached through the full source->tables path.
        var repoRoot = RepoLocator.Root();
        var doseRepo = File.ReadAllText(Path.Combine(repoRoot,
            "VitaTrack.Core", "Features", "Dosing", "PrescribedDoseRepository.cs"));
        var doseTables = ExtractSqlStrings(doseRepo)
            .SelectMany(ExtractTableReferences)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        CollectionAssert.AreEquivalent(
            new[] { "PrescribedDoses", "FamilyMembers", "Supplements" },
            doseTables);

        // Prose containing SQL keywords but no statement shape: not SQL.
        const string prose = "Could not load the row; the cache was stale and refresh failed from the fallback handler.";
        Assert.AreEqual(0, ExtractSqlStrings(prose).Count());

        // Single-statement forms parsed directly.
        CollectionAssert.AreEquivalent(
            new[] { "Supplements" },
            ExtractTableReferences("INSERT INTO Supplements (Name) VALUES (@Name); SELECT last_insert_rowid();").ToList());
        CollectionAssert.AreEquivalent(
            new[] { "PrescribedDoses" },
            ExtractTableReferences("DELETE FROM PrescribedDoses WHERE FamilyMemberId = @Id").ToList());
    }

    private static List<(string SliceId, List<string> CorePaths, List<string> Tables)> LoadSlices(
        string repoRoot, List<string> errors)
    {
        var path = Path.Combine(repoRoot, "shards.yaml");
        Assert.IsTrue(File.Exists(path), "shards.yaml missing at repo root.");

        var yaml = new YamlStream();
        yaml.Load(new StringReader(File.ReadAllText(path)));
        var root = (YamlMappingNode)yaml.Documents[0].RootNode;

        var slices = new List<(string, List<string>, List<string>)>();
        foreach (var slice in ((YamlSequenceNode)root.Children[new YamlScalarNode("slices")]).OfType<YamlMappingNode>())
        {
            var id = Scalar(slice, "id");
            Assert.IsFalse(string.IsNullOrEmpty(id), "a shard is missing 'id'.");

            if (!slice.Children.TryGetValue(new YamlScalarNode("tables"), out var tablesNode))
            {
                errors.Add($"shard '{id}': missing 'tables' declaration (use [] when the slice issues no SQL).");
                slices.Add((id, LoadCore(repoRoot, slice, id, errors), new List<string>()));
                continue;
            }

            var tables = tablesNode is YamlSequenceNode seq
                ? seq.OfType<YamlScalarNode>().Select(s => s.Value ?? string.Empty).ToList()
                : new List<string>();
            slices.Add((id, LoadCore(repoRoot, slice, id, errors), tables));
        }

        return slices;
    }

    /// <summary>
    /// The slice's <c>core</c> file list, and a loud failure for every way that
    /// list can quietly stop describing the slice: the key omitted entirely, a
    /// wildcard that would scan an unreviewed file set, or a path that no longer
    /// exists. Key <em>presence</em> is the absent-vs-empty discriminator, so
    /// <c>core: []</c> (SHELL) is legal and an omitted key is not.
    /// </summary>
    private static List<string> LoadCore(string repoRoot, YamlMappingNode slice, string sliceId, List<string> errors)
    {
        if (!slice.Children.TryGetValue(new YamlScalarNode("core"), out var node))
        {
            errors.Add($"shard '{sliceId}': missing 'core' declaration (use [] when the slice has no core files).");
            return new();
        }

        var patterns = ((YamlSequenceNode)node).OfType<YamlScalarNode>()
            .Select(s => s.Value ?? string.Empty)
            .ToList();

        foreach (var pattern in patterns.Where(p => p.Contains('*')))
            errors.Add($"shard '{sliceId}': 'core' pattern '{pattern}' is a wildcard; list the files explicitly so the scanned set is reviewable.");

        return patterns.Where(p => !p.Contains('*')).ToList();
    }

    private static string Scalar(YamlMappingNode node, string key)
        => node.Children.TryGetValue(new YamlScalarNode(key), out var v) && v is YamlScalarNode s
            ? s.Value ?? string.Empty
            : string.Empty;
}
