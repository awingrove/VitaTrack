using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VitaTrack.Core.Data;
using VitaTrack.Core.Features.Dosing;
using VitaTrack.Core.Features.Nutrients;
using VitaTrack.Core.Features.Supplements;
using VitaTrack.Core.Models;

namespace VitaTrack.Tests;

/// <summary>
/// Delete/cascade behavior of <see cref="SupplementNutrientRepository"/>:
/// single deletes, bulk deletes, blend parent-child cascades, and the
/// cross-slice <c>DeleteBySupplementIdsAsync</c> entry point.
/// </summary>
[TestClass]
public class SupplementNutrientRepositoryCascadeTests : SqliteTestBase
{
    private ISupplementNutrientRepository _nutrientRepo = null!;
    private ISupplementRepository _supplementRepo = null!;

    [TestInitialize]
    public void Setup()
    {
        _nutrientRepo = new SupplementNutrientRepository(Connection);
        _supplementRepo = new SupplementRepository(Connection, new SupplementNutrientRepository(Connection), new PrescribedDoseRepository(Connection));
    }

    private async Task<int> SeedSupplementAsync()
    {
        var sup = new Supplement
        {
            Name = "Test Supplement",
            Brand = "TestBrand",
            DailyDose = "1 tablet",
            ManufacturerUrl = null,
            NutritionJson = "{}",
            SwapSuggestion = null,
            Cost = 10.00m
        };
        return await _supplementRepo.AddAsync(sup);
    }

    [TestMethod]
    public async Task Delete_RemovesNutrient()
    {
        var supplementId = await SeedSupplementAsync();

        var nutrient = new SupplementNutrient { SupplementId = supplementId, GenericName = "Iron", SpecificForm = "Ferrous Sulfate", Dosage = "18mg" };
        var id = await _nutrientRepo.AddAsync(nutrient);
        Assert.IsTrue(id > 0);

        // Act – Delete
        await _nutrientRepo.DeleteAsync(id);

        // Assert
        var deleted = await _nutrientRepo.GetByIdAsync(id);
        Assert.IsNull(deleted);

        var all = await _nutrientRepo.GetBySupplementIdAsync(supplementId);
        Assert.AreEqual(0, all.Count);
    }

    [TestMethod]
    public async Task DeleteMultiple_RemovesSelectedNutrients()
    {
        var supplementId = await SeedSupplementAsync();
        var ids = new List<int>();

        for (int i = 0; i < 3; i++)
        {
            ids.Add(await _nutrientRepo.AddAsync(new SupplementNutrient { SupplementId = supplementId, GenericName = $"Nutrient{i}", SpecificForm = "Form", Dosage = "10mg" }));
        }

        await _nutrientRepo.DeleteAsync([ids[0], ids[2]]);

        var all = await _nutrientRepo.GetBySupplementIdAsync(supplementId);
        Assert.AreEqual(1, all.Count);
        Assert.AreEqual(ids[1], all[0].Id);
    }

    [TestMethod]
    public async Task Delete_ParentBlend_AlsoDeletesChildren()
    {
        var supplementId = await SeedSupplementAsync();

        var parent = new SupplementNutrient { SupplementId = supplementId, GenericName = "Proprietary Blend", SpecificForm = "Blend", Dosage = "500mg" };
        var parentId = await _nutrientRepo.AddAsync(parent);
        var child1 = new SupplementNutrient { SupplementId = supplementId, GenericName = "Zinc", SpecificForm = "Picolinate", Dosage = string.Empty, ParentNutrientId = parentId };
        var child2 = new SupplementNutrient { SupplementId = supplementId, GenericName = "Magnesium", SpecificForm = "Glycinate", Dosage = string.Empty, ParentNutrientId = parentId };
        await _nutrientRepo.AddAsync(child1);
        await _nutrientRepo.AddAsync(child2);

        // Act
        var affected = await _nutrientRepo.DeleteAsync(parentId);
        Assert.AreEqual(3, affected); // parent + 2 children

        // Assert – parent and all children gone
        Assert.IsNull(await _nutrientRepo.GetByIdAsync(parentId));
        Assert.AreEqual(0, (await _nutrientRepo.GetByParentIdAsync(parentId)).Count);
        Assert.AreEqual(0, (await _nutrientRepo.GetBySupplementIdAsync(supplementId)).Count);
    }

    [TestMethod]
    public async Task Delete_Child_KeepsParentAndSiblings()
    {
        var supplementId = await SeedSupplementAsync();

        var parent = new SupplementNutrient { SupplementId = supplementId, GenericName = "Proprietary Blend", SpecificForm = "Blend", Dosage = "500mg" };
        var parentId = await _nutrientRepo.AddAsync(parent);
        var child1 = new SupplementNutrient { SupplementId = supplementId, GenericName = "Zinc", SpecificForm = "Picolinate", Dosage = string.Empty, ParentNutrientId = parentId };
        var child1Id = await _nutrientRepo.AddAsync(child1);
        await _nutrientRepo.AddAsync(new SupplementNutrient { SupplementId = supplementId, GenericName = "Magnesium", SpecificForm = "Glycinate", Dosage = string.Empty, ParentNutrientId = parentId });

        // Act
        await _nutrientRepo.DeleteAsync(child1Id);

        // Assert
        Assert.IsNotNull(await _nutrientRepo.GetByIdAsync(parentId));
        var children = await _nutrientRepo.GetByParentIdAsync(parentId);
        Assert.AreEqual(1, children.Count);
        Assert.AreEqual("Magnesium", children[0].GenericName);
    }

    [TestMethod]
    public async Task DeleteMultiple_WithParentBlend_AlsoDeletesChildren()
    {
        var supplementId = await SeedSupplementAsync();

        var blendA = new SupplementNutrient { SupplementId = supplementId, GenericName = "Blend A", SpecificForm = "Blend", Dosage = "500mg" };
        var blendAId = await _nutrientRepo.AddAsync(blendA);
        await _nutrientRepo.AddAsync(new SupplementNutrient { SupplementId = supplementId, GenericName = "Zinc", SpecificForm = "Picolinate", Dosage = string.Empty, ParentNutrientId = blendAId });
        var standalone = new SupplementNutrient { SupplementId = supplementId, GenericName = "Vitamin D", SpecificForm = "Cholecalciferol", Dosage = "1000IU" };
        var standaloneId = await _nutrientRepo.AddAsync(standalone);

        // Act – delete blend A and the standalone nutrient
        var affected = await _nutrientRepo.DeleteAsync(new List<int> { blendAId, standaloneId });
        Assert.AreEqual(3, affected); // blend A + its child + standalone

        // Assert – only nothing remains; child of blend A cascaded away
        Assert.AreEqual(0, (await _nutrientRepo.GetBySupplementIdAsync(supplementId)).Count);
    }

    [TestMethod]
    public async Task DeleteBySupplementIds_RemovesBlendChildrenBeforeParents()
    {
        var supplementId = await SeedSupplementAsync();

        var blendId = await _nutrientRepo.AddAsync(new SupplementNutrient { SupplementId = supplementId, GenericName = "Blend", SpecificForm = "Blend", Dosage = "500mg" });
        await _nutrientRepo.AddAsync(new SupplementNutrient { SupplementId = supplementId, GenericName = "Zinc", SpecificForm = "Picolinate", Dosage = string.Empty, ParentNutrientId = blendId });

        var otherSupplementId = await SeedSupplementAsync();
        var otherNutrientId = await _nutrientRepo.AddAsync(new SupplementNutrient { SupplementId = otherSupplementId, GenericName = "Keep", SpecificForm = "Form", Dosage = "10mg" });

        // Act
        await _nutrientRepo.DeleteBySupplementIdsAsync([supplementId]);

        // Assert – target supplement's parent and cascaded child gone
        Assert.AreEqual(0, (await _nutrientRepo.GetBySupplementIdAsync(supplementId)).Count);

        // Assert – other supplement untouched
        Assert.IsNotNull(await _nutrientRepo.GetByIdAsync(otherNutrientId));
    }

    [TestMethod]
    public async Task Add_WithParentId_PersistsAndReadsBack()
    {
        var supplementId = await SeedSupplementAsync();

        var parent = new SupplementNutrient { SupplementId = supplementId, GenericName = "Proprietary Blend", SpecificForm = "Blend", Dosage = "500mg" };
        var parentId = await _nutrientRepo.AddAsync(parent);
        var child = new SupplementNutrient { SupplementId = supplementId, GenericName = "Zinc", SpecificForm = "Picolinate", Dosage = string.Empty, ParentNutrientId = parentId };
        await _nutrientRepo.AddAsync(child);

        var children = await _nutrientRepo.GetByParentIdAsync(parentId);
        Assert.AreEqual(1, children.Count);
        Assert.AreEqual("Zinc", children[0].GenericName);
    }

    [TestMethod]
    public async Task DeleteMultiple_EmptyListDeletesNothing()
    {
        var supplementId = await SeedSupplementAsync();
        await _nutrientRepo.AddAsync(new SupplementNutrient { SupplementId = supplementId, GenericName = "Keep", SpecificForm = "Form", Dosage = "10mg" });

        var affected = await _nutrientRepo.DeleteAsync(new List<int>());
        Assert.AreEqual(0, affected);

        var all = await _nutrientRepo.GetBySupplementIdAsync(supplementId);
        Assert.AreEqual(1, all.Count);
    }
}
