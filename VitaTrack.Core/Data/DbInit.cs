using System.Data;
using Dapper;
using VitaTrack.Core.Primitives;

namespace VitaTrack.Core.Data;

public static class DbInit
{
    public static void EnsureCreated(IDbConnection db, bool seedData = true)
    {
        db.Open();

        db.Execute(@"
            CREATE TABLE IF NOT EXISTS FamilyMembers (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                DisplayName TEXT NOT NULL,
                AvatarUrl TEXT NULL
            );");

        db.Execute(@"
            CREATE TABLE IF NOT EXISTS Supplements (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                Brand TEXT NOT NULL,
                DailyDose TEXT NOT NULL,
                ManufacturerUrl TEXT NULL,
                NutritionJson TEXT NULL,
                SwapSuggestion TEXT NULL,
                Cost REAL NULL,
                ServingsPerBottle REAL NULL,
                Currency TEXT NULL DEFAULT 'GBP'
            );");

        db.Execute(@"
            CREATE TABLE IF NOT EXISTS SupplementNutrients (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                SupplementId INTEGER NOT NULL,
                GenericName TEXT NOT NULL,
                SpecificForm TEXT NOT NULL,
                Dosage TEXT NOT NULL,
                FOREIGN KEY (SupplementId) REFERENCES Supplements(Id)
            );");

        // Create PrescribedDoses table if it doesn't exist
        db.Execute(@"
            CREATE TABLE IF NOT EXISTS PrescribedDoses (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                FamilyMemberId INTEGER NOT NULL,
                SupplementId INTEGER NOT NULL,
                StartDate TEXT NULL,
                EndDate TEXT NULL,
                Multiplier REAL NOT NULL,
                Instructions TEXT NOT NULL,
                FOREIGN KEY (FamilyMemberId) REFERENCES FamilyMembers(Id),
                FOREIGN KEY (SupplementId) REFERENCES Supplements(Id)
            );");
        // Migrate PrescribedDoses: Dosage (TEXT) -> Multiplier (REAL), drop FrequencyPerDay.
        // Runs only for databases created with the old schema; fresh databases already have Multiplier.
        var freqCol = db.QuerySingle<int>(
            "SELECT COUNT(*) FROM pragma_table_info('PrescribedDoses') WHERE name = 'FrequencyPerDay';");
        var dosageType = db.QuerySingle<string>(
            "SELECT COALESCE((SELECT type FROM pragma_table_info('PrescribedDoses') WHERE name = 'Dosage'), '');");
        if (freqCol > 0 || dosageType == "TEXT")
        {
            db.Execute(@"
                CREATE TABLE PrescribedDoses_new (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    FamilyMemberId INTEGER NOT NULL,
                    SupplementId INTEGER NOT NULL,
                    StartDate TEXT NULL,
                    EndDate TEXT NULL,
                    Multiplier REAL NOT NULL DEFAULT 1.0,
                    Instructions TEXT NOT NULL,
                    FOREIGN KEY (FamilyMemberId) REFERENCES FamilyMembers(Id),
                    FOREIGN KEY (SupplementId) REFERENCES Supplements(Id)
                );");
            db.Execute(@"
                INSERT INTO PrescribedDoses_new (Id, FamilyMemberId, SupplementId, StartDate, EndDate, Multiplier, Instructions)
                SELECT Id, FamilyMemberId, SupplementId, StartDate, EndDate, 1.0, Instructions FROM PrescribedDoses;");
            db.Execute("DROP TABLE PrescribedDoses;");
            db.Execute("ALTER TABLE PrescribedDoses_new RENAME TO PrescribedDoses;");
        }

        // Add ParentNutrientId column if it doesn't exist (self-reference for nutrient blends)
        var parentCol = db.QuerySingle<int>("SELECT COUNT(*) FROM pragma_table_info('SupplementNutrients') WHERE name = 'ParentNutrientId';");
        if (parentCol == 0)
        {
            db.Execute("ALTER TABLE SupplementNutrients ADD COLUMN ParentNutrientId INTEGER NULL;");
        }

        // Add ServingsPerBottle column if it doesn't exist (how many servings one bottle contains)
        var servingsCol = db.QuerySingle<int>("SELECT COUNT(*) FROM pragma_table_info('Supplements') WHERE name = 'ServingsPerBottle';");
        if (servingsCol == 0)
        {
            db.Execute("ALTER TABLE Supplements ADD COLUMN ServingsPerBottle REAL NULL;");
        }

        // Add Currency column if it doesn't exist (default GBP to match the historical UI).
        var currencyCol = db.QuerySingle<int>("SELECT COUNT(*) FROM pragma_table_info('Supplements') WHERE name = 'Currency';");
        if (currencyCol == 0)
        {
            db.Execute("ALTER TABLE Supplements ADD COLUMN Currency TEXT NULL DEFAULT 'GBP';");
        }

        // Normalize legacy dosage units (mcg/ug/μg -> µg, iu -> IU) so every unit of
        // measure has one designation in the database. Idempotent; runs each startup.
        foreach (var row in db.Query("SELECT Id, Dosage FROM SupplementNutrients WHERE Dosage IS NOT NULL AND Dosage <> '';"))
        {
            var normalized = Dosage.Normalize((string)row.Dosage);
            if (normalized != (string)row.Dosage)
            {
                db.Execute("UPDATE SupplementNutrients SET Dosage = @Dosage WHERE Id = @Id;",
                    new { Id = (long)row.Id, Dosage = normalized });
            }
        }

        // Insert sample data only if ALL tables are empty (fresh database)
        if (seedData)
        {
            var familyCount = db.QuerySingle<int>("SELECT COUNT(*) FROM FamilyMembers;");
            var supplementCount = db.QuerySingle<int>("SELECT COUNT(*) FROM Supplements;");
            var nutrientCount = db.QuerySingle<int>("SELECT COUNT(*) FROM SupplementNutrients;");
            var doseCount = db.QuerySingle<int>("SELECT COUNT(*) FROM PrescribedDoses;");

            if (familyCount == 0 && supplementCount == 0 && nutrientCount == 0 && doseCount == 0)
            {
                db.Execute(@"
                        INSERT INTO FamilyMembers (Name, DisplayName, AvatarUrl) VALUES 
                        ('Alice Smith', 'Alice', 'https://example.com/alice.jpg'),
                        ('Bob Johnson', 'Bob', 'https://example.com/bob.jpg'),
                        ('Carol Williams', 'Carol', 'https://example.com/carol.jpg')
                    ");

                db.Execute(@"
                        INSERT INTO Supplements (Name, Brand, DailyDose, NutritionJson, Cost, ServingsPerBottle) VALUES 
                        ('Vitamin C', 'NatureMade', '2 tablets', @nutrition1, 15.99, 60),
                        ('Fish Oil', 'Kirkland', '1 softgel', @nutrition2, 25.50, 120),
                        ('Multivitamin', 'Centrum', '1 tablet', @nutrition3, 19.99, 60)
                    ", new
                {
                    nutrition1 = "{\"vitamin_c\": 500, \"iron\": 0}",
                    nutrition2 = "{\"omega_3\": 1000, \"vitamin_d\": 200}",
                    nutrition3 = "{\"vitamin_a\": 900, \"vitamin_c\": 90, \"vitamin_d\": 20, \"iron\": 18, \"calcium\": 200}"
                });

                db.Execute(@"
                        INSERT INTO SupplementNutrients (SupplementId, GenericName, SpecificForm, Dosage) VALUES 
                        (1, 'Vitamin C', 'Ascorbic Acid', '500mg'),
                        (1, 'Iron', 'Ferrous Sulfate', '0mg'),
                        (2, 'Omega-3', 'Fish Oil', '1000mg'),
                        (2, 'Vitamin D', 'Cholecalciferol', '200IU'),
                        (3, 'Vitamin A', 'Retinyl Acetate', '900µg'),
                        (3, 'Vitamin C', 'Ascorbic Acid', '90mg'),
                        (3, 'Vitamin D', 'Cholecalciferol', '20µg'),
                        (3, 'Iron', 'Ferrous Fumarate', '18mg'),
                        (3, 'Calcium', 'Calcium Carbonate', '200mg')
                    ");

                db.Execute(@"
                        INSERT INTO PrescribedDoses (FamilyMemberId, SupplementId, StartDate, EndDate, Multiplier, Instructions) VALUES
                        (1, 1, NULL, NULL, 1.0, 'Take with breakfast'),
                        (1, 2, NULL, NULL, 1.0, 'Take with dinner'),
                        (2, 3, NULL, NULL, 1.0, 'Take in the morning')
                    ");
                db.Execute(@"
                        INSERT INTO SupplementNutrients (Id, SupplementId, GenericName, SpecificForm, Dosage, ParentNutrientId) VALUES
                        (9001, 3, 'Proprietary Blend', 'Blend', '500mg', NULL)
                    ");
                db.Execute(@"
                        INSERT INTO SupplementNutrients (SupplementId, GenericName, SpecificForm, Dosage, ParentNutrientId) VALUES 
                        (3, 'Pectin', 'Citrus', '200mg', 9001),
                        (3, 'Botanical Extract', 'Proprietary', '', 9001)
                    ");

            }
        }
    }
}