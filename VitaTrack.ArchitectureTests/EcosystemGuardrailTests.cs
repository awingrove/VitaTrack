using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Reflection;
using VitaTrack.Core.Data;
using VitaTrack.Core.Features.Supplements;
using VitaTrack.Web.Controllers;

namespace VitaTrack.ArchitectureTests;

/// <summary>
/// A banned dependency has to be caught whether the reference is direct or reached
/// through another assembly, which means the walk recurses. Asserting inside the
/// recursion made the rule unsound: the assertion is raised in a recursee, unwinds
/// into the <c>catch</c> that guards the <c>Assembly.Load</c>, and is discarded. The
/// walk therefore collects and the caller asserts, so nothing raised inside the
/// recursion can be swallowed.
/// </summary>
[TestClass]
public class EcosystemGuardrailTests
{
    private const string EntityFrameworkCore = "Microsoft.EntityFrameworkCore";

    /// <summary>
    /// A package the repositories use, so the walker's own tests have a banned name
    /// that really is in the graph. It is a sentinel, not a rule: nothing bans Dapper.
    /// </summary>
    private const string SentinelPackage = "Dapper";

    private const string AbsentPackage = "VitaTrack.NoSuchAssemblyAnywhere";

    [TestMethod]
    public void InfrastructureAssembly_DoesNotReferenceEntityFrameworkCore() =>
        AssertNoTransitiveDependency(typeof(SupplementRepository).Assembly, EntityFrameworkCore);

    [TestMethod]
    public void WebAssembly_DoesNotReferenceEntityFrameworkCore() =>
        AssertNoTransitiveDependency(typeof(SupplementController).Assembly, EntityFrameworkCore);

    /// <summary>
    /// The case the empty <c>catch</c> used to discard. <c>VitaTrack.Web</c> has no
    /// direct <c>Dapper</c> reference; <c>VitaTrack.Core</c> does. So the only
    /// violation in the graph sits one level down, where the old walker raised it from
    /// a recursee and threw it away. Reporting it is the whole point of the walk, so a
    /// collector that finds nothing here is a collector that is not walking.
    /// </summary>
    [TestMethod]
    public void Collector_FindsAViolationReachableOnlyThroughAnotherAssembly()
    {
        var violations = CollectViolations(typeof(SupplementController).Assembly, SentinelPackage);

        Assert.IsTrue(
            violations.Count > 0,
            "The transitive walk reported nothing, but VitaTrack.Web -> VitaTrack.Core -> "
            + $"{SentinelPackage} is in the graph. A violation found only below the root must still be reported.");
    }

    /// <summary>
    /// The same rule at depth 0, so the collector is pinned in both directions of the
    /// walk: a violation on the root's own reference list, and one below it.
    /// </summary>
    [TestMethod]
    public void Collector_FindsAViolationOnTheRootAssemblyItself()
    {
        var violations = CollectViolations(typeof(SupplementRepository).Assembly, SentinelPackage);

        Assert.IsTrue(
            violations.Count > 0,
            $"VitaTrack.Core references {SentinelPackage} directly, so the collector must report it; it reported nothing.");
    }

    /// <summary>
    /// The negative direction. Without this, a collector that matched everything would
    /// satisfy the two tests above and ban nothing meaningfully.
    /// </summary>
    [TestMethod]
    public void Collector_ReportsNothingForANameNoAssemblyHas()
    {
        var violations = CollectViolations(typeof(SupplementController).Assembly, AbsentPackage);

        Assert.AreEqual(0, violations.Count, FormatViolations(violations, AbsentPackage));
    }

    private static void AssertNoTransitiveDependency(Assembly root, string bannedName)
    {
        var violations = CollectViolations(root, bannedName);
        Assert.AreEqual(0, violations.Count, FormatViolations(violations, bannedName));
    }

    private static List<string> CollectViolations(Assembly root, string bannedName)
    {
        var violations = new List<string>();
        CollectViolations(root, bannedName, violations, new HashSet<string>());
        return violations;
    }

    private static void CollectViolations(
        Assembly asm, string bannedName, List<string> violations, HashSet<string> visited)
    {
        var name = asm.GetName().Name ?? asm.FullName ?? asm.Location;
        if (!visited.Add(name)) return;

        foreach (var refName in asm.GetReferencedAssemblies())
        {
            // Recorded before the load is attempted, so an assembly that cannot be
            // loaded still cannot hide a reference it is already known to have.
            if (IsBanned(refName.Name, bannedName))
                violations.Add($"Assembly {name} references banned {refName.Name}");

            try
            {
                var loaded = Assembly.Load(refName.FullName ?? refName.Name ?? string.Empty);
                CollectViolations(loaded, bannedName, violations, visited);
            }
            catch
            {
                // An assembly outside the test host's resolution context cannot be walked.
                // Its references are already recorded above; nothing is asserted in here,
                // so there is no failure for this catch to swallow.
            }
        }
    }

    private static bool IsBanned(string? referenceName, string bannedName) =>
        referenceName != null && referenceName.StartsWith(bannedName, StringComparison.OrdinalIgnoreCase);

    private static string FormatViolations(List<string> violations, string bannedName) =>
        violations.Count == 0
            ? string.Empty
            : $"No assembly in the closure should reference {bannedName}. Found: {string.Join("; ", violations)}";
}
