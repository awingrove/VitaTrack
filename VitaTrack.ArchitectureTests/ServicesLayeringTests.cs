using Microsoft.VisualStudio.TestTools.UnitTesting;
using NetArchTest.Rules;
using System.Reflection;
using VitaTrack.Infrastructure.Services;
using TestResult = NetArchTest.Rules.TestResult;

namespace VitaTrack.ArchitectureTests;

[TestClass]
public class ServicesLayeringTests
{
    private static readonly Assembly InfrastructureAssembly = typeof(ISupplementNutrientService).Assembly;

    /// <summary>
    /// AGENTS.md: business logic lives in Services; SQL stays in Data repositories.
    /// Services must reach the database only through repository interfaces.
    /// </summary>
    [TestMethod]
    public void Services_DoNotDependOnDataAssemblies()
    {
        var result = Types.InAssembly(InfrastructureAssembly)
            .That().ResideInNamespace("VitaTrack.Infrastructure.Services")
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
