using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using VitaTrack.Web.Controllers;

namespace VitaTrack.ArchitectureTests;

[TestClass]
public class ControllerHygieneTests
{
    [TestMethod]
    public void Controllers_DoNotCatchException()
    {
        var solutionRoot = RepoLocator.Root();
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
            "Controllers must not `catch (Exception)` — per-row failures return failure-carrying result records; "
            + "exceptional system failures propagate to the global error handler (AGENTS.md §2.5):\n  "
            + string.Join("\n  ", violations));
    }
}