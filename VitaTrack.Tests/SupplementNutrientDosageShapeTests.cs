using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;
using System.Threading.Tasks;
using VitaTrack.Core.Features.Nutrients;

namespace VitaTrack.Tests;

/// <summary>
/// The dosage-shape rule as <see cref="SupplementNutrientService"/> enforces it on the
/// LLM/Review persist path. <see cref="SupplementNutrientValidationTests"/> covers the same
/// predicate on the CRUD surface; a rule verified on one surface proves nothing about the
/// other, so both are pinned here and there.
/// </summary>
[TestClass]
public class SupplementNutrientDosageShapeTests
{
    private static readonly NullLogger<SupplementNutrientService> NullLogger =
        NullLogger<SupplementNutrientService>.Instance;

    [TestMethod]
    public async Task PersistHierarchy_TopLevelMalformedDosage_FailsWithTheSharedShapeMessage()
    {
        var repo = new MockRepo();
        var svc = new SupplementNutrientService(repo, NullLogger);

        var result = await svc.PersistHierarchyAsync(1,
        [
            new SupplementNutrientDto { GenericName = "Vit C", SpecificForm = "Ascorbic", Dosage = "3 capsules" },
            new SupplementNutrientDto { GenericName = "Zinc", SpecificForm = "Picolinate", Dosage = "15mg" }
        ]);

        var failure = result.Failures.Single(f => f.GenericName == "Vit C");
        Assert.AreEqual(SupplementNutrient.DosageShapeRequired, failure.Error,
            "the service reports the same constant the CRUD surface shows, not a copy of it");
        Assert.AreEqual("Zinc", result.Saved.Single().GenericName, "a malformed root must not be persisted");
        Assert.IsFalse(repo.Added.Any(n => n.GenericName == "Vit C"));
    }

    [TestMethod]
    public async Task PersistHierarchy_TopLevelDosageWithNoAmount_Fails()
    {
        var repo = new MockRepo();
        var svc = new SupplementNutrientService(repo, NullLogger);

        var result = await svc.PersistHierarchyAsync(1,
            [new SupplementNutrientDto { GenericName = "Vit C", SpecificForm = "Ascorbic", Dosage = "one tablet" }]);

        Assert.AreEqual("Vit C", result.Failures.Single().GenericName);
        Assert.AreEqual(0, result.Saved.Count);
    }

    [TestMethod]
    public async Task PersistHierarchy_ChildMalformedDosage_FailsAndTheRestOfTheBlendStillSaves()
    {
        var repo = new MockRepo();
        var svc = new SupplementNutrientService(repo, NullLogger);
        var blend = new SupplementNutrientDto
        {
            GenericName = "Proprietary Blend",
            SpecificForm = "Blend",
            Dosage = "500mg",
            Children =
            [
                new SupplementNutrientDto { GenericName = "Zinc", SpecificForm = "Picolinate", Dosage = "3 capsules" },
                new SupplementNutrientDto { GenericName = "Magnesium", SpecificForm = "Glycinate", Dosage = "200mg" },
                new SupplementNutrientDto { GenericName = "Pectin", SpecificForm = "Citrus" }
            ]
        };

        var result = await svc.PersistHierarchyAsync(1, [blend]);

        // The child path used to validate nothing, so this dosage reached the nutrient
        // report as though "capsules" were a unit.
        var failure = result.Failures.Single(f => f.GenericName == "Zinc");
        Assert.AreEqual(SupplementNutrient.DosageShapeRequired, failure.Error);
        Assert.IsFalse(repo.Added.Any(n => n.GenericName == "Zinc"), "a malformed child must not be persisted");
        Assert.AreEqual(3, result.Saved.Count, "the parent, the good sibling, and the blank-dosage child still save");
        Assert.IsTrue(result.Saved.Any(n => n.GenericName == "Pectin"), "a blank child dosage is legal");
    }

    [TestMethod]
    public async Task PersistHierarchy_ChildDosageIsShapeCheckedWithoutBecomingRequired()
    {
        var repo = new MockRepo();
        var svc = new SupplementNutrientService(repo, NullLogger);

        var result = await svc.PersistHierarchyAsync(1,
        [
            new SupplementNutrientDto
            {
                GenericName = "Proprietary Blend", Dosage = "500mg",
                Children = [new SupplementNutrientDto { GenericName = "Zinc", Dosage = "   " }]
            }
        ]);

        Assert.AreEqual(0, result.Failures.Count,
            "whitespace is blank, and blank is legal for a blend child");
        Assert.AreEqual(2, result.Saved.Count);
    }

    [TestMethod]
    public async Task PersistHierarchy_AmountOnlyAndAliasedDosagesStillSave()
    {
        var repo = new MockRepo();
        var svc = new SupplementNutrientService(repo, NullLogger);

        var result = await svc.PersistHierarchyAsync(1,
        [
            new SupplementNutrientDto { GenericName = "A", SpecificForm = "F", Dosage = "1" },
            new SupplementNutrientDto { GenericName = "B", SpecificForm = "F", Dosage = "500" },
            new SupplementNutrientDto { GenericName = "C", SpecificForm = "F", Dosage = "0mg" },
            new SupplementNutrientDto { GenericName = "D", SpecificForm = "F", Dosage = "500mcg" },
            new SupplementNutrientDto { GenericName = "E", SpecificForm = "F", Dosage = "1 tablet" }
        ]);

        Assert.AreEqual(0, result.Failures.Count, string.Join("; ", result.Failures.Select(f => f.Error)));
        Assert.AreEqual(5, result.Saved.Count);
    }
}
