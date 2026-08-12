using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using System.Reflection;
using VitaTrack.Infrastructure.Data;

namespace VitaTrack.ArchitectureTests;

[TestClass]
public class FileSizeTests
{
    private const int HardLimit = 300;

    [TestMethod]
    public void NoCsFile_Exceeds300Lines()
    {
        var solutionRoot = FindSolutionRoot();
        Assert.IsNotNull(solutionRoot, "Could not locate VitaTrack.sln");

        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "bin", "obj", "node_modules", "playwright-report", "test-results", "TestResults"
        };

        var violations = new List<string>();
        foreach (var path in Directory.EnumerateFiles(solutionRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (IsExcluded(path, excluded)) continue;

            var lines = File.ReadLines(path).Count();
            if (lines > HardLimit)
            {
                violations.Add($"{Path.GetRelativePath(solutionRoot, path)}: {lines} lines");
            }
        }

        Assert.AreEqual(0, violations.Count,
            $"Files exceeding {HardLimit}-line limit (AGENTS.md hard limit):\n  " + string.Join("\n  ", violations));
    }

    private static bool IsExcluded(string path, HashSet<string> excluded)
    {
        foreach (var segment in path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (excluded.Contains(segment)) return true;
        }
        return false;
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