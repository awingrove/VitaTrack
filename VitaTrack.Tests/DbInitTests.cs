using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VitaTrack.Infrastructure.Data;

namespace VitaTrack.Tests;

[TestClass]
public class DbInitTests
{
    private static SqliteConnection OpenConnection()
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        return conn;
    }

    private static IList<dynamic> Columns(SqliteConnection conn, string table) =>
        conn.Query($"SELECT name, type FROM pragma_table_info('{table}');").ToList();

    private static bool HasColumn(SqliteConnection conn, string table, string column) =>
        Columns(conn, table).Any(c => c.name == column);

    [TestMethod]
    public void EnsureCreated_SeedsFreshDatabase()
    {
        using var conn = OpenConnection();

        DbInit.EnsureCreated(conn);

        Assert.AreEqual(3, conn.QuerySingle<int>("SELECT COUNT(*) FROM FamilyMembers;"));
        Assert.AreEqual(3, conn.QuerySingle<int>("SELECT COUNT(*) FROM Supplements;"));
        Assert.AreEqual(3, conn.QuerySingle<int>("SELECT COUNT(*) FROM PrescribedDoses;"));
        Assert.AreEqual(12, conn.QuerySingle<int>("SELECT COUNT(*) FROM SupplementNutrients;"));

        var doses = conn.Query("SELECT Multiplier, Instructions FROM PrescribedDoses;").ToList();
        Assert.IsTrue(doses.All(d => d.Multiplier == 1.0), "Every seeded dose must have Multiplier 1.0");
        Assert.IsTrue(doses.All(d => !string.IsNullOrEmpty((string)d.Instructions)));

        var blend = conn.QuerySingle(
            "SELECT GenericName, ParentNutrientId FROM SupplementNutrients WHERE Id = 9001;");
        Assert.AreEqual("Proprietary Blend", (string)blend.GenericName);
        Assert.IsTrue(blend.ParentNutrientId is DBNull or null, "Blend parent must have NULL ParentNutrientId");

        Assert.AreEqual(2,
            conn.QuerySingle<int>("SELECT COUNT(*) FROM SupplementNutrients WHERE ParentNutrientId = 9001;"));
    }

    [TestMethod]
    public void EnsureCreated_SeedDataFalse_CreatesTablesButNoRows()
    {
        using var conn = OpenConnection();

        DbInit.EnsureCreated(conn, seedData: false);

        foreach (var table in new[] { "FamilyMembers", "Supplements", "SupplementNutrients", "PrescribedDoses" })
        {
            Assert.AreEqual(1, conn.QuerySingle<int>(
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @table;",
                new { table }), $"Table {table} must exist");
            Assert.AreEqual(0, conn.QuerySingle<int>($"SELECT COUNT(*) FROM {table};"),
                $"Table {table} must be empty");
        }
    }

    [TestMethod]
    public void EnsureCreated_DoesNotSeed_WhenAnyTableHasData()
    {
        using var conn = OpenConnection();

        DbInit.EnsureCreated(conn, seedData: false);
        conn.Execute(
            "INSERT INTO FamilyMembers (Name, DisplayName, AvatarUrl) VALUES ('Test', 'Test', NULL);");

        DbInit.EnsureCreated(conn);

        Assert.AreEqual(1, conn.QuerySingle<int>("SELECT COUNT(*) FROM FamilyMembers;"),
            "Existing member must be preserved");
        Assert.AreEqual(0, conn.QuerySingle<int>("SELECT COUNT(*) FROM Supplements;"),
            "Seeding must be skipped when any table is non-empty");
        Assert.AreEqual(0, conn.QuerySingle<int>("SELECT COUNT(*) FROM SupplementNutrients;"));
        Assert.AreEqual(0, conn.QuerySingle<int>("SELECT COUNT(*) FROM PrescribedDoses;"));
    }

    [TestMethod]
    public void EnsureCreated_MigratesLegacyPrescribedDoses()
    {
        using var conn = OpenConnection();
        CreateLegacySchema(conn, withFrequencyPerDay: false);
        conn.Execute(
            "INSERT INTO PrescribedDoses (FamilyMemberId, SupplementId, StartDate, EndDate, Dosage, Instructions) " +
            "VALUES (1, 1, '2026-01-01', NULL, '500mg', 'Take with food');");

        DbInit.EnsureCreated(conn, seedData: false);

        var columns = Columns(conn, "PrescribedDoses");
        Assert.IsFalse(columns.Any(c => c.name == "Dosage"), "Legacy Dosage column must be dropped");
        var multiplier = columns.Single(c => c.name == "Multiplier");
        Assert.AreEqual("REAL", (string)multiplier.type);

        var row = conn.QuerySingle(
            "SELECT Id, FamilyMemberId, SupplementId, StartDate, EndDate, Multiplier, Instructions FROM PrescribedDoses;");
        Assert.AreEqual(1, (long)row.Id);
        Assert.AreEqual(1, (long)row.FamilyMemberId);
        Assert.AreEqual(1, (long)row.SupplementId);
        Assert.AreEqual("2026-01-01", (string)row.StartDate);
        Assert.AreEqual(1.0, (double)row.Multiplier);
        Assert.AreEqual("Take with food", (string)row.Instructions);
    }

    [TestMethod]
    public void EnsureCreated_MigratesLegacyPrescribedDoses_WithFrequencyPerDay()
    {
        using var conn = OpenConnection();
        CreateLegacySchema(conn, withFrequencyPerDay: true);
        conn.Execute(
            "INSERT INTO PrescribedDoses (FamilyMemberId, SupplementId, StartDate, EndDate, Dosage, Instructions, FrequencyPerDay) " +
            "VALUES (1, 1, NULL, '2026-02-01', '1000mg', 'Before bed', 2);");

        DbInit.EnsureCreated(conn, seedData: false);

        var columns = Columns(conn, "PrescribedDoses");
        Assert.IsFalse(columns.Any(c => c.name == "FrequencyPerDay"), "FrequencyPerDay must be dropped");
        Assert.IsFalse(columns.Any(c => c.name == "Dosage"));
        Assert.AreEqual("REAL", (string)columns.Single(c => c.name == "Multiplier").type);

        var row = conn.QuerySingle(
            "SELECT EndDate, Multiplier, Instructions FROM PrescribedDoses;");
        Assert.AreEqual("2026-02-01", (string)row.EndDate);
        Assert.AreEqual(1.0, (double)row.Multiplier);
        Assert.AreEqual("Before bed", (string)row.Instructions);
    }

    [TestMethod]
    public void EnsureCreated_MigrationIdempotent_OnCurrentSchema()
    {
        using var conn = OpenConnection();

        DbInit.EnsureCreated(conn, seedData: false);
        conn.Execute("INSERT INTO FamilyMembers (Name, DisplayName) VALUES ('Alice', 'Alice');");
        conn.Execute("INSERT INTO Supplements (Name, Brand, DailyDose) VALUES ('VitC', 'Brand', '1 tablet');");
        conn.Execute(
            "INSERT INTO PrescribedDoses (FamilyMemberId, SupplementId, StartDate, EndDate, Multiplier, Instructions) " +
            "VALUES (1, 1, NULL, NULL, 1.0, 'Once daily');");

        DbInit.EnsureCreated(conn, seedData: false);

        var columns = Columns(conn, "PrescribedDoses");
        Assert.IsFalse(columns.Any(c => c.name == "Dosage"));
        Assert.IsFalse(columns.Any(c => c.name == "FrequencyPerDay"));
        Assert.AreEqual("REAL", (string)columns.Single(c => c.name == "Multiplier").type);
        Assert.AreEqual(1, conn.QuerySingle<int>("SELECT COUNT(*) FROM PrescribedDoses;"),
            "Second run must not lose or duplicate rows");
    }

    [TestMethod]
    public void EnsureCreated_AddsParentNutrientIdAndServingsPerBottle_ToLegacyTables()
    {
        using var conn = OpenConnection();
        conn.Execute(@"
            CREATE TABLE FamilyMembers (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                DisplayName TEXT NOT NULL,
                AvatarUrl TEXT NULL
            );");
        conn.Execute(@"
            CREATE TABLE Supplements (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                Brand TEXT NOT NULL,
                DailyDose TEXT NOT NULL,
                ManufacturerUrl TEXT NULL,
                NutritionJson TEXT NULL,
                SwapSuggestion TEXT NULL,
                Cost REAL NULL
            );");
        conn.Execute(@"
            CREATE TABLE SupplementNutrients (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                SupplementId INTEGER NOT NULL,
                GenericName TEXT NOT NULL,
                SpecificForm TEXT NOT NULL,
                Dosage TEXT NOT NULL,
                FOREIGN KEY (SupplementId) REFERENCES Supplements(Id)
            );");

        Assert.IsFalse(HasColumn(conn, "SupplementNutrients", "ParentNutrientId"));
        Assert.IsFalse(HasColumn(conn, "Supplements", "ServingsPerBottle"));

        DbInit.EnsureCreated(conn, seedData: false);

        var nutrientColumns = Columns(conn, "SupplementNutrients");
        Assert.IsTrue(nutrientColumns.Any(c => c.name == "ParentNutrientId"));
        Assert.AreEqual("INTEGER", (string)nutrientColumns.Single(c => c.name == "ParentNutrientId").type);

        var supplementColumns = Columns(conn, "Supplements");
        Assert.IsTrue(supplementColumns.Any(c => c.name == "ServingsPerBottle"));
        Assert.AreEqual("REAL", (string)supplementColumns.Single(c => c.name == "ServingsPerBottle").type);
    }

    private static void CreateLegacySchema(SqliteConnection conn, bool withFrequencyPerDay)
    {
        conn.Execute(@"
            CREATE TABLE FamilyMembers (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                DisplayName TEXT NOT NULL,
                AvatarUrl TEXT NULL
            );");
        conn.Execute(@"
            CREATE TABLE Supplements (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                Brand TEXT NOT NULL,
                DailyDose TEXT NOT NULL,
                ManufacturerUrl TEXT NULL,
                NutritionJson TEXT NULL,
                SwapSuggestion TEXT NULL,
                Cost REAL NULL
            );");
        conn.Execute(@"
            INSERT INTO FamilyMembers (Name, DisplayName, AvatarUrl) VALUES ('Legacy', 'Legacy', NULL);");
        conn.Execute(@"
            INSERT INTO Supplements (Name, Brand, DailyDose) VALUES ('Legacy Supplement', 'LegacyBrand', '1 tablet');");
        conn.Execute(@"
            CREATE TABLE PrescribedDoses (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                FamilyMemberId INTEGER NOT NULL,
                SupplementId INTEGER NOT NULL,
                StartDate TEXT NULL,
                EndDate TEXT NULL,
                Dosage TEXT NOT NULL,
                Instructions TEXT NOT NULL
            );");
        if (withFrequencyPerDay)
        {
            conn.Execute("ALTER TABLE PrescribedDoses ADD COLUMN FrequencyPerDay REAL NOT NULL DEFAULT 1;");
        }
    }
}
