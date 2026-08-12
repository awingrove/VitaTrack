using Microsoft.VisualStudio.TestTools.UnitTesting;
using NetArchTest.Rules;
using System.Reflection;
using VitaTrack.Infrastructure.Data;
using TestResult = NetArchTest.Rules.TestResult;

namespace VitaTrack.ArchitectureTests;

[TestClass]
public class RepositoryNamingTests
{
    [TestMethod]
    public void ConcreteClassesInInfrastructureData_AreNamedRepository_OrAreKnownExceptions()
    {
        var result = Types.InAssembly(typeof(SupplementRepository).Assembly)
            .That().ResideInNamespace("VitaTrack.Infrastructure.Data")
            .And().AreClasses()
            .And().DoNotHaveName("DbInit")
            .Should().HaveNameEndingWith("Repository")
            .GetResult();

        Assert.IsTrue(result.IsSuccessful, FormatFailures(result));
    }

    [TestMethod]
    public void RepositoryImplementations_ResideInInfrastructureData()
    {
        var result = Types.InAssembly(typeof(SupplementRepository).Assembly)
            .That().HaveNameEndingWith("Repository")
            .Should().ResideInNamespace("VitaTrack.Infrastructure.Data")
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