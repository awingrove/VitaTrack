using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Reflection;
using VitaTrack.Infrastructure.Data;
using VitaTrack.Web.Controllers;

namespace VitaTrack.ArchitectureTests;

[TestClass]
public class EcosystemGuardrailTests
{
    [TestMethod]
    public void InfrastructureAssembly_DoesNotReferenceEntityFrameworkCore()
    {
        AssertNoTransitiveDependency(typeof(SupplementRepository).Assembly, "Microsoft.EntityFrameworkCore", new HashSet<string>());
    }

    [TestMethod]
    public void WebAssembly_DoesNotReferenceEntityFrameworkCore()
    {
        AssertNoTransitiveDependency(typeof(SupplementController).Assembly, "Microsoft.EntityFrameworkCore", new HashSet<string>());
    }

    private static void AssertNoTransitiveDependency(Assembly asm, string bannedName, HashSet<string> visited)
    {
        var name = asm.GetName().Name ?? asm.FullName ?? asm.Location;
        if (!visited.Add(name)) return;

        var refs = asm.GetReferencedAssemblies();
        foreach (var refName in refs)
        {
            Assert.IsFalse(
                refName.Name != null && refName.Name.StartsWith(bannedName, StringComparison.OrdinalIgnoreCase),
                $"Assembly {name} references banned {refName.Name}");

            try
            {
                var loaded = Assembly.Load(refName.FullName ?? refName.Name ?? string.Empty);
                AssertNoTransitiveDependency(loaded, bannedName, visited);
            }
            catch
            {
            }
        }
    }
}