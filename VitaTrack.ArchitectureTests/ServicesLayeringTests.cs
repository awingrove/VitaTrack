using Microsoft.VisualStudio.TestTools.UnitTesting;
using NetArchTest.Rules;
using System.Reflection;
using VitaTrack.ArchitectureTests.Fakes;
using VitaTrack.Core.Features.Nutrients;
using VitaTrack.Core.Features.Reporting;
using TestResult = NetArchTest.Rules.TestResult;

namespace VitaTrack.ArchitectureTests;

[TestClass]
public class ServicesLayeringTests
{
    private static readonly Assembly CoreAssembly = typeof(ISupplementNutrientService).Assembly;

    /// <summary>
    /// AGENTS.md: business logic lives in the slice folders under VitaTrack.Core/Features/;
    /// SQL lives in VitaTrack.Core/Data and in the slice repositories themselves. Slice code
    /// must reach the database only through repository interfaces.
    /// </summary>
    [TestMethod]
    public void Services_DoNotDependOnDataAssemblies()
    {
        var result = ApplyDataAssemblyBans(SliceBusinessLogic());

        Assert.IsTrue(result.IsSuccessful, FormatFailures(result));
    }

    [TestMethod]
    public void Services_SelectionIsNotVacuous()
    {
        var selected = SelectedTypeNames(SliceBusinessLogic());

        Assert.IsTrue(
            selected.Contains(typeof(ReportingService).Name),
            $"The Core layering rule selected {selected.Count} type(s) and none of them is {typeof(ReportingService).Name}; "
            + "a rule that selects nothing passes without testing anything.");
    }

    [TestMethod]
    public void DataAssemblyBans_RejectATypeThatReachesTheDatabase()
    {
        Assert.IsTrue(
            SelectedTypeNames(Fakes()).Contains(typeof(SneakyDapperService).Name),
            "The negative-path selection is itself empty, so it proves nothing.");

        var result = ApplyDataAssemblyBans(Fakes());
        var failing = result.FailingTypeNames ?? new List<string>();

        Assert.IsFalse(result.IsSuccessful, "The data-assembly bans did not reject a type that uses Dapper and System.Data.");
        Assert.IsTrue(
            failing.Contains(typeof(SneakyDapperService).FullName!),
            $"{typeof(SneakyDapperService).FullName} was not among the failing types: {string.Join(", ", failing)}");
    }

    /// <summary>
    /// The selection: the whole Core assembly minus the three groups that are allowed to know
    /// about the database. The Repository suffix is the same line RepositoryNamingTests draws
    /// — the repositories are the layer that is allowed to write SQL. ServiceCollectionExtensions
    /// is the composition root, the one place that has to know both halves in order to wire them
    /// together. VitaTrack.Core.Primitives is deliberately NOT carved out: a value object must
    /// never touch the database, and excluding it would be the easiest way to lose that rule.
    /// </summary>
    private static PredicateList SliceBusinessLogic() =>
        Types.InAssembly(CoreAssembly)
            .That().ResideInNamespace("VitaTrack.Core")
            .And().DoNotResideInNamespace("VitaTrack.Core.Data")
            .And().DoNotResideInNamespace("VitaTrack.Core.Primitives")
            .And().DoNotHaveNameEndingWith("Repository")
            .And().DoNotHaveName("ServiceCollectionExtensions");

    private static PredicateList Fakes() =>
        Types.InAssembly(typeof(SneakyDapperService).Assembly)
            .That().ResideInNamespace("VitaTrack.ArchitectureTests.Fakes");

    private static TestResult ApplyDataAssemblyBans(PredicateList types) =>
        types.Should().NotHaveDependencyOn("System.Data")
            .And().NotHaveDependencyOn("Microsoft.Data.Sqlite")
            .And().NotHaveDependencyOn("Dapper")
            .GetResult();

    private static List<string> SelectedTypeNames(PredicateList types) =>
        types.GetTypes().Select(t => t?.Name).OfType<string>().ToList();

    private static string FormatFailures(TestResult result)
    {
        if (result.IsSuccessful) return string.Empty;
        var failing = result.FailingTypeNames ?? new List<string>();
        return "Failing types:\n  " + string.Join("\n  ", failing);
    }
}
