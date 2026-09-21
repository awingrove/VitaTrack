using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace VitaTrack.ArchitectureTests;

/// <summary>
/// Keeps types small. The metric is the *complete type* — every partial part across
/// files — not individual files. Splitting one class into partials in separate files
/// to dodge a per-file cap is itself a violation: the intent is to prevent a type
/// from mushrooming in complexity, not to enforce a gameable file count.
/// </summary>
[TestClass]
public class FileSizeTests
{
    private const int HardLimit = 300;

    // Pre-existing debt already tracked in the rollout backlog (split SupplementController
    // into the Supplements slice's handlers). Remove this entry when that slice lands.
    private static readonly HashSet<string> KnownTypeDebt = new(StringComparer.Ordinal)
    {
        "VitaTrack.Web.Controllers.SupplementController"
    };

    [TestMethod]
    public void NoCsFile_Exceeds300Lines()
    {
        var solutionRoot = FindSolutionRoot();
        Assert.IsNotNull(solutionRoot, "Could not locate VitaTrack.sln");

        var violations = new List<string>();
        foreach (var path in EnumerateCsFiles(solutionRoot))
        {
            var lines = File.ReadLines(path).Count();
            if (lines > HardLimit)
                violations.Add($"{Path.GetRelativePath(solutionRoot, path)}: {lines} lines");
        }

        Assert.AreEqual(0, violations.Count,
            $"Files exceeding {HardLimit}-line limit (AGENTS.md split trigger):\n  " + string.Join("\n  ", violations));
    }

    [TestMethod]
    public void NoCompleteType_Exceeds300LinesIncludingPartials()
    {
        var solutionRoot = FindSolutionRoot();
        Assert.IsNotNull(solutionRoot, "Could not locate VitaTrack.sln");

        var totals = new Dictionary<string, (int Lines, List<string> Files)>();

        foreach (var path in EnumerateCsFiles(solutionRoot))
        {
            var lines = File.ReadLines(path).Count();
            var ns = ParseNamespace(File.ReadAllText(path));
            foreach (var typeName in ParseTypeNames(File.ReadAllText(path)))
            {
                var key = $"{ns}.{typeName}";
                if (!totals.TryGetValue(key, out var entry))
                    totals[key] = (lines, new List<string> { path });
                else
                {
                    entry.Lines += lines;
                    entry.Files.Add(path);
                    totals[key] = entry;
                }
            }
        }

        var violations = new List<string>();
        foreach (var (key, entry) in totals)
        {
            if (entry.Lines <= HardLimit) continue;
            if (KnownTypeDebt.Contains(key)) continue; // tracked debt; not a new regression
            var files = string.Join(", ", entry.Files.Select(f => Path.GetRelativePath(solutionRoot, f)));
            violations.Add($"{key}: {entry.Lines} lines across [{files}] (partials included)");
        }

        Assert.AreEqual(0, violations.Count,
            $"Complete types (including partials) exceeding {HardLimit}-line split trigger — split responsibilities:\n  " + string.Join("\n  ", violations));
    }

    private static IEnumerable<string> EnumerateCsFiles(string root)
    {
        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "bin", "obj", "node_modules", "playwright-report", "test-results", "TestResults"
        };
        foreach (var path in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            if (path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Any(segment => excluded.Contains(segment)))
                continue;
            yield return path;
        }
    }

    private static readonly Regex NamespacePattern = new(@"^\s*namespace\s+([\w.]+)", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex TypePattern = new(@"(?:record\s+)?(?:class|interface)\s+(\w+)", RegexOptions.Compiled);

    private static string ParseNamespace(string text)
    {
        var match = NamespacePattern.Match(text);
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    private static IEnumerable<string> ParseTypeNames(string text)
    {
        foreach (Match m in TypePattern.Matches(text))
            yield return m.Groups[1].Value;
    }

    private static string? FindSolutionRoot()
    {
        var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        while (dir != null)
        {
            if (Directory.GetFiles(dir, "*.sln").Length > 0) return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }
        return null;
    }
}
