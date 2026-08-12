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
        var result = Types.InAssembly(WebAssembly)
            .That().ResideInNamespace("VitaTrack.Web.Controllers")
            .Should().NotHaveDependencyOn("System.Data")
            .And().NotHaveDependencyOn("Microsoft.Data.Sqlite")
            .And().NotHaveDependencyOn("Dapper")
            .GetResult();

        Assert.IsTrue(result.IsSuccessful, FormatFailures(result));
    }

    private static string FormatFailures(TestResult result)
    {
        if (result.IsSuccessful) return string.Empty;
        var failing = result.FailingTypeNames ?? new List<string>();
        return "Failing types:\n  " + string.Join("\n  ", failing);
    }
}