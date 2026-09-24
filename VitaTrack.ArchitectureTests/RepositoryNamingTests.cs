using Microsoft.VisualStudio.TestTools.UnitTesting;
using NetArchTest.Rules;
using System.Reflection;
using VitaTrack.Core.Data;
using VitaTrack.Core.Features.Supplements;
using TestResult = NetArchTest.Rules.TestResult;

namespace VitaTrack.ArchitectureTests;

[TestClass]
public class RepositoryNamingTests
{
    [TestMethod]
    public void ConcreteClassesInInfrastructureData_AreNamedRepository_OrAreKnownExceptions()
    {
        var result = Types.InAssembly(typeof(SupplementRepository).Assembly)
            .That().ResideInNamespace("VitaTrack.Core.Data")
            .And().AreClasses()
            .And().DoNotHaveName("DbInit")
            .Should().HaveNameEndingWith("Repository")
            .GetResult();

        Assert.IsTrue(result.IsSuccessful, FormatFailures(result));
    }

    [TestMethod]
    public void RepositoryImplementations_ResideInDataOrFeatureSlice()
    {
        var asm = typeof(SupplementRepository).Assembly;
        var allowedPrefixes = new[] { "VitaTrack.Core.Data", "VitaTrack.Core.Features" };

        var failures = asm.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract)
            .Where(t => t.Name.EndsWith("Repository"))
            .Where(t => !allowedPrefixes.Any(p => t.Namespace == p || (t.Namespace ?? string.Empty).StartsWith(p + ".")))
            .Select(t => t.FullName!)
            .ToList();

        Assert.AreEqual(0, failures.Count,
            "Repository implementations must live in VitaTrack.Core.Data or a VitaTrack.Core.Features slice:\n  " + string.Join("\n  ", failures));
    }

    private static string FormatFailures(TestResult result)
    {
        if (result.IsSuccessful) return string.Empty;
        var failing = result.FailingTypeNames ?? new List<string>();
        return "Failing types:\n  " + string.Join("\n  ", failing);
    }
}