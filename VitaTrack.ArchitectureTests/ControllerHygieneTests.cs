using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using System.Reflection;
using VitaTrack.Web.Controllers;

namespace VitaTrack.ArchitectureTests;

[TestClass]
public class ControllerHygieneTests
{
    [TestMethod]
    public void Controllers_DoNotCatchException()
    {
        var solutionRoot = FindSolutionRoot();
        Assert.IsNotNull(solutionRoot);

        var controllersDir = Path.Combine(solutionRoot, "VitaTrack.Web", "Controllers");
        Assert.IsTrue(Directory.Exists(controllersDir), $"Controller dir missing: {controllersDir}");

        var violations = new List<string>();
        foreach (var path in Directory.EnumerateFiles(controllersDir, "*.cs"))
        {
            var text = File.ReadAllText(path);
            if (text.Contains("catch (Exception"))
            {
                violations.Add(Path.GetFileName(path));
            }
        }

        Assert.AreEqual(0, violations.Count,
            $"Controllers still use `catch (Exception)` — AGENTS.md mandates Result<T> over exception swallowing (see §2.5):\n  "
            + string.Join("\n  ", violations));
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