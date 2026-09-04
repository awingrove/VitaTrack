using System.Data;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VitaTrack.Infrastructure.Data;

namespace VitaTrack.Tests;

[TestClass]
public class DbInitTests
{
    [TestMethod]
    public void EnsureCreated_SeedsAllTables_WhenFreshDatabase()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        DbInit.EnsureCreated(conn, seedData: true);

        var familyCount = conn.QuerySingle<int>("SELECT COUNT(*) FROM FamilyMembers;");
        var supplementCount = conn.QuerySingle<int>("SELECT COUNT(*) FROM Supplements;");
        var nutrientCount = conn.QuerySingle<int>("SELECT COUNT(*) FROM SupplementNutrients;");
        var doseCount = conn.QuerySingle<int>("SELECT COUNT(*) FROM PrescribedDoses;");

        Assert.IsTrue(familyCount >= 3, "Expected seeded family members");
        Assert.IsTrue(supplementCount >= 3, "Expected seeded supplements");
        Assert.IsTrue(nutrientCount >= 9, "Expected seeded nutrients");
        Assert.IsTrue(doseCount >= 3, "Expected seeded prescribed doses");
    }

    [TestMethod]
    public void EnsureCreated_SeedsBlendHierarchy()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        DbInit.EnsureCreated(conn, seedData: true);

        var blendChildren = conn.QuerySingle<int>(
            "SELECT COUNT(*) FROM SupplementNutrients WHERE ParentNutrientId IS NOT NULL;");

        Assert.IsTrue(blendChildren >= 2, "Expected seeded blend to have child nutrients");
    }

    [TestMethod]
    public void EnsureCreated_DoesNotDoubleSeed_WhenDataPresent()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        DbInit.EnsureCreated(conn, seedData: true);
        DbInit.EnsureCreated(conn, seedData: true);

        var familyCount = conn.QuerySingle<int>("SELECT COUNT(*) FROM FamilyMembers;");

        Assert.AreEqual(3, familyCount, "EnsureCreated must not duplicate seed data");
    }
}
