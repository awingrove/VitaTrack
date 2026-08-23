using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VitaTrack.Web.Controllers;

namespace VitaTrack.ArchitectureTests;

/// <summary>
/// Every public GET action must be reachable from inside the app: an inbound
/// link/tag-helper in a view, an inbound HTMX hx-get, or a controller redirect.
/// Direct-URL-only pages are defects - E2E tests navigating by raw URL hide them,
/// so test specs are deliberately NOT counted as references.
/// </summary>
[TestClass]
public class UiReachabilityTests
{
    private static readonly Assembly WebAssembly = typeof(SupplementController).Assembly;

    /// <summary>Actions that are infrastructure plumbing, not user navigation.</summary>
    private static readonly HashSet<(string Controller, string Action)> Allowlist =
        new()
        {
            ("Home", "Error"), // exception handler target registered in Program.cs
        };

    [TestMethod]
    public void GetActions_AreReferencedFromInsideTheApp()
    {
        var repoRoot = FindRepoRoot();
        var actions = DiscoverGetActions();
        Assert.IsTrue(actions.Count > 0, "No GET actions discovered - discovery logic broken.");

        var references = CollectInboundReferences(repoRoot);

        var orphans = actions
            .Where(a => !references.Contains((a.Controller.ToLowerInvariant(), a.Action.ToLowerInvariant())))
            .Where(a => !Allowlist.Contains((a.Controller, a.Action)))
            .Select(a => $"{a.Controller}/{a.Action}")
            .ToList();

        Assert.IsTrue(
            orphans.Count == 0,
            "Orphan GET actions - no view link, hx-get, or redirect points at them: " +
            string.Join(", ", orphans));
    }

    private static List<(string Controller, string Action)> DiscoverGetActions()
    {
        var controllerTypes = WebAssembly.GetTypes()
            .Where(t => t.IsPublic && !t.IsAbstract && t.Name.EndsWith("Controller"))
            .ToList();

        var result = new List<(string, string)>();
        foreach (var type in controllerTypes)
        {
            var controllerName = type.Name[..^"Controller".Length];
            var methods = type.GetMethods(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

            foreach (var method in methods)
            {
                if (!typeof(IActionResult).IsAssignableFrom(method.ReturnType)) continue;
                if (method.GetCustomAttribute<NonActionAttribute>() != null) continue;
                if (method.GetCustomAttribute<HttpPostAttribute>() != null) continue;
                if (method.GetCustomAttribute<HttpPutAttribute>() != null) continue;
                if (method.GetCustomAttribute<HttpDeleteAttribute>() != null) continue;
                if (method.IsSpecialName) continue;

                var actionName = method.GetCustomAttribute<ActionNameAttribute>()?.Name ?? method.Name;
                result.Add((controllerName, actionName));
            }
        }

        return result;
    }

    private static HashSet<(string, string)> CollectInboundReferences(string repoRoot)
    {
        var references = new HashSet<(string, string)>();

        var viewFiles = Directory.GetFiles(Path.Combine(repoRoot, "VitaTrack.Web", "Views"), "*.cshtml", SearchOption.AllDirectories);
        var controllerFiles = Directory.GetFiles(Path.Combine(repoRoot, "VitaTrack.Web", "Controllers"), "*.cs");
        var jsFiles = Directory.Exists(Path.Combine(repoRoot, "VitaTrack.Web", "wwwroot"))
            ? Directory.GetFiles(Path.Combine(repoRoot, "VitaTrack.Web", "wwwroot"), "*.js", SearchOption.AllDirectories)
            : Array.Empty<string>();

        foreach (var view in viewFiles)
        {
            var text = File.ReadAllText(view);
            var defaultController = Path.GetFileName(Path.GetDirectoryName(view));
            if (defaultController == "Shared") defaultController = null;

            // asp-controller + asp-action inside one tag (either attribute order)
            foreach (Match m in Regex.Matches(text, @"asp-controller=""(\w+)""[^>]*asp-action=""(\w+)"""))
                references.Add((m.Groups[1].Value.ToLowerInvariant(), m.Groups[2].Value.ToLowerInvariant()));
            foreach (Match m in Regex.Matches(text, @"asp-action=""(\w+)""[^>]*asp-controller=""(\w+)"""))
                references.Add((m.Groups[2].Value.ToLowerInvariant(), m.Groups[1].Value.ToLowerInvariant()));

            // asp-action alone inherits the view folder's controller
            if (defaultController != null)
                foreach (Match m in Regex.Matches(text, @"asp-action=""(\w+)"""))
                    references.Add((defaultController.ToLowerInvariant(), m.Groups[1].Value.ToLowerInvariant()));

            CollectUrlReferences(text, references);
        }

        foreach (var js in jsFiles)
            CollectUrlReferences(File.ReadAllText(js), references);

        foreach (var controllerFile in controllerFiles)
        {
            var text = File.ReadAllText(controllerFile);
            var controllerName = Path.GetFileNameWithoutExtension(controllerFile)[..^"Controller".Length];

            // RedirectToAction(nameof(Action)...) / RedirectToAction("Action"...) target this controller
            foreach (Match m in Regex.Matches(text, @"RedirectToAction\(\s*(?:nameof\()(\w+)\)?"))
                references.Add((controllerName.ToLowerInvariant(), m.Groups[1].Value.ToLowerInvariant()));

            // RedirectToAction("Action", "Controller") cross-controller
            foreach (Match m in Regex.Matches(text, @"RedirectToAction\(\s*""(\w+)""\s*,\s*""(\w+)"""))
                references.Add((m.Groups[2].Value.ToLowerInvariant(), m.Groups[1].Value.ToLowerInvariant()));

            CollectUrlReferences(text, references);
        }

        var programCs = Path.Combine(repoRoot, "VitaTrack.Web", "Program.cs");
        if (File.Exists(programCs))
            CollectUrlReferences(File.ReadAllText(programCs), references);

        return references;
    }

    private static void CollectUrlReferences(string text, HashSet<(string, string)> references)
    {
        foreach (var controllerType in WebAssembly.GetTypes()
                     .Where(t => t.Name.EndsWith("Controller")))
        {
            var controller = controllerType.Name[..^"Controller".Length];
            // Matches /Controller, /Controller/Action, /Controller/Action/1, /Controller?id=...
            foreach (Match m in Regex.Matches(text, $@"(?i)/{controller}(?:/(\w+))?(?=\?|/|\b|""|')"))
            {
                var segment = m.Groups[1].Success ? m.Groups[1].Value : "Index";
                references.Add((controller.ToLowerInvariant(), segment.ToLowerInvariant()));
            }
        }
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
