using Microsoft.VisualStudio.TestTools.UnitTesting;
using NetArchTest.Rules;
using System.Reflection;
using VitaTrack.Web.Controllers;
using TestResult = NetArchTest.Rules.TestResult;

namespace VitaTrack.ArchitectureTests;

[TestClass]
public class WebLayerDependencyTests
{
    private static readonly Assembly WebAssembly = typeof(SupplementController).Assembly;

    [TestMethod]
    public void WebControllers_DoNotDependOnDataAssemblies()
    {
        var result = Controllers()
            .Should().NotHaveDependencyOn("System.Data")
            .And().NotHaveDependencyOn("Microsoft.Data.Sqlite")
            .And().NotHaveDependencyOn("Dapper")
            .GetResult();

        Assert.IsTrue(result.IsSuccessful, FormatFailures(result));
    }

    /// <summary>
    /// The same non-vacuity sentinel the Core rule carries. A rule whose selection is empty
    /// passes without testing anything, so the namespace is pinned to a known controller by
    /// name — a bare Count > 0 would still pass if the net shrank to some other single type.
    /// </summary>
    [TestMethod]
    public void WebControllers_SelectionIsNotVacuous()
    {
        var selected = SelectedTypeNames(Controllers());

        Assert.IsTrue(
            selected.Contains(typeof(HomeController).Name),
            $"The Web layering rule selected {selected.Count} type(s) and none of them is {typeof(HomeController).Name}; "
            + "a rule that selects nothing passes without testing anything.");
    }

    private static PredicateList Controllers() =>
        Types.InAssembly(WebAssembly)
            .That().ResideInNamespace("VitaTrack.Web.Controllers");

    private static List<string> SelectedTypeNames(PredicateList types) =>
        types.GetTypes().Select(t => t?.Name).OfType<string>().ToList();

    private static string FormatFailures(TestResult result)
    {
        if (result.IsSuccessful) return string.Empty;
        var failing = result.FailingTypeNames ?? new List<string>();
        return "Failing types:\n  " + string.Join("\n  ", failing);
    }
}
