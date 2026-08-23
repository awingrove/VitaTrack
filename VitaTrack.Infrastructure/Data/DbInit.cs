using System.Data;
using Dapper;

namespace VitaTrack.Infrastructure.Data;

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
                ServingsPerBottle REAL NULL
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
                Dosage TEXT NOT NULL,
                Instructions TEXT NOT NULL,
                FOREIGN KEY (FamilyMemberId) REFERENCES FamilyMembers(Id),
                FOREIGN KEY (SupplementId) REFERENCES Supplements(Id)
            );");

        // Add FrequencyPerDay column if it doesn't exist
        var columnExists = db.QuerySingle<int>(@"
                SELECT COUNT(*) FROM pragma_table_info('PrescribedDoses') WHERE name = 'FrequencyPerDay';
            ");
        if (columnExists == 0)
        {
            db.Execute(@"
                    ALTER TABLE PrescribedDoses ADD COLUMN FrequencyPerDay REAL NOT NULL DEFAULT 1.0;
                ");
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
                        (3, 'Vitamin A', 'Retinyl Acetate', '900mcg'),
                        (3, 'Vitamin C', 'Ascorbic Acid', '90mg'),
                        (3, 'Vitamin D', 'Cholecalciferol', '20mcg'),
                        (3, 'Iron', 'Ferrous Fumarate', '18mg'),
                        (3, 'Calcium', 'Calcium Carbonate', '200mg')
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

                db.Execute(@"
                        INSERT INTO PrescribedDoses (FamilyMemberId, SupplementId, Dosage, Instructions, FrequencyPerDay) VALUES 
                        (1, 1, '500mg', 'Take with breakfast', 1.0),
                        (1, 2, '1 softgel', 'Take with dinner', 1.0),
                        (2, 3, '1 tablet', 'Take in the morning', 1.0)
                    ");
            }
        }
    }
}