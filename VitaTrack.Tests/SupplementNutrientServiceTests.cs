using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VitaTrack.Core.Data;
using VitaTrack.Core.Models;
using VitaTrack.Core.Services;

namespace VitaTrack.Tests;

[TestClass]
public class SupplementNutrientServiceTests
{
    private static readonly ILogger<SupplementNutrientService> NullLogger =
        NullLogger<SupplementNutrientService>.Instance;

    [TestMethod]
    public async Task PersistHierarchy_StoresChildrenWithParentId()
    {
        var repo = new MockRepo();
        var svc = new SupplementNutrientService(repo, NullLogger);
        var blend = new SupplementNutrientDto
        {
            GenericName = "Proprietary Blend",
            SpecificForm = "Blend",
            Dosage = "500mg",
            Children = [new SupplementNutrientDto { GenericName = "Zinc", SpecificForm = "Picolinate" }]
        };

        var result = await svc.PersistHierarchyAsync(1, [blend]);

        Assert.AreEqual(2, result.Saved.Count);
        var child = repo.Added.Single(n => n.GenericName == "Zinc");
        Assert.IsTrue(child.ParentNutrientId > 0);
    }

    [TestMethod]
    public async Task PersistHierarchy_TopLevelMissingDosage_Fails()
    {
        var repo = new MockRepo();
        var svc = new SupplementNutrientService(repo, NullLogger);
        var result = await svc.PersistHierarchyAsync(
            1, [new SupplementNutrientDto { GenericName = "Vit C", SpecificForm = "Ascorbic" }]);
        Assert.IsTrue(result.Failures.Any(f => f.GenericName == "Vit C"));
    }

    [TestMethod]
    public async Task ReplaceAsync_DeletesExistingThenPersistsHierarchy()
    {
        var repo = new MockRepo();
        var svc = new SupplementNutrientService(repo, NullLogger);
        await repo.AddAsync(new SupplementNutrient { SupplementId = 1, GenericName = "Old", SpecificForm = "x", Dosage = "1mg" });

        var result = await svc.ReplaceAsync(1, [new SupplementNutrientDto
        {
            GenericName = "Proprietary Blend",
            SpecificForm = "Blend",
            Dosage = "500mg",
            Children = [new SupplementNutrientDto { GenericName = "Zinc", SpecificForm = "Picolinate" }]
        }]);

        var remaining = await repo.GetBySupplementIdAsync(1);
        Assert.IsFalse(remaining.Any(n => n.GenericName == "Old"));
        Assert.AreEqual(2, result.Saved.Count);
        Assert.IsTrue(repo.Added.Single(n => n.GenericName == "Zinc").ParentNutrientId > 0);
    }

    [TestMethod]
    public async Task PersistHierarchy_ChildRepoThrow_RecordedAndBatchContinues()
    {
        var repo = new MockRepo { ThrowFor = { "Zinc" } };
        var svc = new SupplementNutrientService(repo, NullLogger);
        var blend = new SupplementNutrientDto
        {
            GenericName = "Proprietary Blend",
            SpecificForm = "Blend",
            Dosage = "500mg",
            Children =
            [
                new SupplementNutrientDto { GenericName = "Zinc", SpecificForm = "Picolinate" },
                new SupplementNutrientDto { GenericName = "Magnesium", SpecificForm = "Glycinate" }
            ]
        };

        var result = await svc.PersistHierarchyAsync(1, [blend]);

        Assert.IsTrue(result.Failures.Any(f => f.GenericName == "Zinc"));
        Assert.AreEqual(2, result.Saved.Count);
        Assert.IsTrue(result.Saved.Any(n => n.GenericName == "Proprietary Blend"));
        Assert.IsTrue(result.Saved.Any(n => n.GenericName == "Magnesium"));
    }

    [TestMethod]
    public async Task ReplaceAsync_FlatListWithParentNutrientId_GroupsIntoHierarchy()
    {
        var repo = new MockRepo();
        var svc = new SupplementNutrientService(repo, NullLogger);

        List<SupplementNutrientDto> flat =
        [
            new SupplementNutrientDto { GenericName = "Proprietary Blend", SpecificForm = "Blend", Dosage = "500mg" },
            new SupplementNutrientDto { GenericName = "Zinc", SpecificForm = "Picolinate", ParentNutrientId = 0 },
            new SupplementNutrientDto { GenericName = "Magnesium", SpecificForm = "Glycinate", ParentNutrientId = 0 }
        ];

        var result = await svc.ReplaceAsync(1, flat);

        Assert.AreEqual(3, result.Saved.Count);
        var root = repo.Added.Single(n => n.GenericName == "Proprietary Blend");
        var zinc = repo.Added.Single(n => n.GenericName == "Zinc");
        var magnesium = repo.Added.Single(n => n.GenericName == "Magnesium");
        Assert.IsTrue(root.ParentNutrientId == null);
        Assert.AreEqual(root.Id, zinc.ParentNutrientId);
        Assert.AreEqual(root.Id, magnesium.ParentNutrientId);
    }

    [TestMethod]
    public async Task ReplaceAsync_ParentIndexOutOfRange_TreatedAsRoot()
    {
        var repo = new MockRepo();
        var svc = new SupplementNutrientService(repo, NullLogger);

        var result = await svc.ReplaceAsync(1,
            [new SupplementNutrientDto { GenericName = "Blend", Dosage = "500mg", ParentNutrientId = 99 }]);

        Assert.IsNull(result.Saved.Single().ParentNutrientId);
    }

    [TestMethod]
    public async Task AddAsync_PersistsRootsAndNestedChildren()
    {
        var repo = new MockRepo();
        var svc = new SupplementNutrientService(repo, NullLogger);
        var blend = new SupplementNutrientDto
        {
            GenericName = "Proprietary Blend",
            SpecificForm = "Blend",
            Dosage = "500mg",
            Children = [new SupplementNutrientDto { GenericName = "Zinc", SpecificForm = "Picolinate" }]
        };

        var result = await svc.AddAsync(1, [blend]);

        Assert.AreEqual(0, result.Failures.Count);
        Assert.AreEqual(2, result.Saved.Count);
        Assert.IsTrue(repo.Added.Single(n => n.GenericName == "Zinc").ParentNutrientId > 0);
    }

    [TestMethod]
    public async Task PersistHierarchy_InvalidRoots_SkippedOrFailedWithoutRepoCalls()
    {
        var repo = new MockRepo();
        var svc = new SupplementNutrientService(repo, NullLogger);

        var result = await svc.PersistHierarchyAsync(1,
        [
            new SupplementNutrientDto { GenericName = "   ", Dosage = "500mg" },
            new SupplementNutrientDto { GenericName = "Vit C", SpecificForm = "Ascorbic", Dosage = "   " },
            new SupplementNutrientDto { GenericName = "Zinc", SpecificForm = "Picolinate", Dosage = "15mg" }
        ]);

        Assert.AreEqual("Zinc", result.Saved.Single().GenericName);
        Assert.IsTrue(result.Failures.Any(f => f.GenericName == "Vit C" && f.Error.Contains("dosage")));
        Assert.AreEqual(1, repo.Added.Count);
    }

    [TestMethod]
    public async Task PersistHierarchy_MissingSpecificForm_DefaultsBlendForRootAndNAForChild()
    {
        var repo = new MockRepo();
        var svc = new SupplementNutrientService(repo, NullLogger);

        var result = await svc.PersistHierarchyAsync(1,
        [
            new SupplementNutrientDto
            {
                GenericName = "Proprietary Blend", Dosage = "500mg",
                Children = [new SupplementNutrientDto { GenericName = "Zinc" }]
            }
        ]);

        Assert.AreEqual("Blend", repo.Added.Single(n => n.GenericName == "Proprietary Blend").SpecificForm);
        Assert.AreEqual("N/A", repo.Added.Single(n => n.GenericName == "Zinc").SpecificForm);
        Assert.AreEqual(string.Empty, repo.Added.Single(n => n.GenericName == "Zinc").Dosage);
    }

    [TestMethod]
    public async Task PersistHierarchy_RootRepoThrow_RecordedAndOtherRootsPersisted()
    {
        var repo = new MockRepo { ThrowFor = { "Bad" } };
        var svc = new SupplementNutrientService(repo, NullLogger);

        var result = await svc.PersistHierarchyAsync(1,
            [new SupplementNutrientDto { GenericName = "Bad", Dosage = "10mg" }, new SupplementNutrientDto { GenericName = "Good", Dosage = "20mg" }]);

        Assert.AreEqual("simulated failure for Bad", result.Failures.Single().Error);
        Assert.AreEqual("Good", result.Saved.Single().GenericName);
    }

    [TestMethod]
    public async Task ReplaceAsync_ChildWithItsOwnChildren_FlattensGrandchildrenUnderSameParent()
    {
        var repo = new MockRepo();
        var svc = new SupplementNutrientService(repo, NullLogger);

        List<SupplementNutrientDto> flat =
        [
            new SupplementNutrientDto { GenericName = "Proprietary Blend", SpecificForm = "Blend", Dosage = "500mg" },
            new SupplementNutrientDto
            {
                GenericName = "Inner Blend",
                SpecificForm = "Blend",
                ParentNutrientId = 0,
                Children =
                [
                    new SupplementNutrientDto { GenericName = "Sub A", SpecificForm = "N/A" },
                    new SupplementNutrientDto { GenericName = "Sub B", SpecificForm = "N/A" }
                ]
            }
        ];

        var result = await svc.ReplaceAsync(1, flat);

        Assert.AreEqual(0, result.Failures.Count);
        var root = repo.Added.Single(n => n.GenericName == "Proprietary Blend");
        var inner = repo.Added.Single(n => n.GenericName == "Inner Blend");
        var subA = repo.Added.Single(n => n.GenericName == "Sub A");
        var subB = repo.Added.Single(n => n.GenericName == "Sub B");
        Assert.AreEqual(root.Id, inner.ParentNutrientId);
        Assert.AreEqual(root.Id, subA.ParentNutrientId);
        Assert.AreEqual(root.Id, subB.ParentNutrientId);
    }

    [TestMethod]
    public async Task ReplaceAsync_DeletesEveryExistingRowBeforePersisting()
    {
        var repo = new MockRepo();
        var svc = new SupplementNutrientService(repo, NullLogger);
        var old1 = await repo.AddAsync(new SupplementNutrient { SupplementId = 1, GenericName = "Old1", SpecificForm = "x", Dosage = "1mg" });
        var old2 = await repo.AddAsync(new SupplementNutrient { SupplementId = 1, GenericName = "Old2", SpecificForm = "x", Dosage = "1mg" });

        var result = await svc.ReplaceAsync(1,
            [new SupplementNutrientDto { GenericName = "Zinc", SpecificForm = "Picolinate", Dosage = "15mg" }]);

        CollectionAssert.AreEquivalent(new[] { old1, old2 }, repo.DeletedIds);
        Assert.AreEqual(1, result.Saved.Count);
    }
}
