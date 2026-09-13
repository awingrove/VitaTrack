using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VitaTrack.Infrastructure.Data;
using VitaTrack.Infrastructure.Models;

namespace VitaTrack.Tests;

[TestClass]
public class SupplementNutrientRepositoryTests : SqliteTestBase
{
    private ISupplementNutrientRepository _nutrientRepo = null!;
    private ISupplementRepository _supplementRepo = null!;

    [TestInitialize]
    public void Setup()
    {
        _nutrientRepo = new SupplementNutrientRepository(Connection);
        _supplementRepo = new SupplementRepository(Connection);
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
    public async Task Add_GetBySupplementId_GetById_Update_Works()
    {
        var supplementId = await SeedSupplementAsync();

        var nutrient = new SupplementNutrient { SupplementId = supplementId, GenericName = "Zinc", SpecificForm = "Zinc Picolinate", Dosage = "5mg" };

        // Act – Add
        var id = await _nutrientRepo.AddAsync(nutrient);
        Assert.IsTrue(id > 0);

        // Act – GetById
        var fetched = await _nutrientRepo.GetByIdAsync(id);
        Assert.IsNotNull(fetched);
        Assert.AreEqual(nutrient.GenericName, fetched!.GenericName);
        Assert.AreEqual(nutrient.SpecificForm, fetched.SpecificForm);
        Assert.AreEqual(nutrient.Dosage, fetched.Dosage);
        Assert.AreEqual(nutrient.SupplementId, fetched.SupplementId);

        // Act – GetBySupplementId
        var all = await _nutrientRepo.GetBySupplementIdAsync(supplementId);
        Assert.AreEqual(1, all.Count);
        Assert.AreEqual(nutrient.GenericName, all[0].GenericName);

        // Act – Update
        fetched.GenericName = "Magnesium";
        fetched.SpecificForm = "Magnesium Glycinate";
        fetched.Dosage = "200mg";
        await _nutrientRepo.UpdateAsync(fetched);
        var updated = await _nutrientRepo.GetByIdAsync(id);
        Assert.IsNotNull(updated);
        Assert.AreEqual("Magnesium", updated!.GenericName);
        Assert.AreEqual("Magnesium Glycinate", updated.SpecificForm);
        Assert.AreEqual("200mg", updated.Dosage);
    }

    [TestMethod]
    public async Task GetBySupplementId_ReturnsEmpty_WhenNoNutrients()
    {
        var supplementId = await SeedSupplementAsync();
        var all = await _nutrientRepo.GetBySupplementIdAsync(supplementId);
        Assert.AreEqual(0, all.Count);
    }

    [TestMethod]
    public async Task GetBySupplementId_ReturnsOnlyNutrientsForThatSupplement()
    {
        var sup1Id = await SeedSupplementAsync();
        var sup2Id = await SeedSupplementAsync();

        await _nutrientRepo.AddAsync(new SupplementNutrient { SupplementId = sup1Id, GenericName = "Zinc", SpecificForm = "Zinc Picolinate", Dosage = "5mg" });
        await _nutrientRepo.AddAsync(new SupplementNutrient { SupplementId = sup2Id, GenericName = "Vitamin C", SpecificForm = "Ascorbic Acid", Dosage = "500mg" });

        var sup1Nutrients = await _nutrientRepo.GetBySupplementIdAsync(sup1Id);
        var sup2Nutrients = await _nutrientRepo.GetBySupplementIdAsync(sup2Id);

        Assert.AreEqual(1, sup1Nutrients.Count);
        Assert.AreEqual("Zinc", sup1Nutrients[0].GenericName);
        Assert.AreEqual(1, sup2Nutrients.Count);
        Assert.AreEqual("Vitamin C", sup2Nutrients[0].GenericName);
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

    [TestMethod]
    public async Task GetByParentId_ReturnsOnlyChildrenOfThatParent()
    {
        var supplementId = await SeedSupplementAsync();
        var parentId = await _nutrientRepo.AddAsync(new SupplementNutrient { SupplementId = supplementId, GenericName = "Proprietary Blend", SpecificForm = "Blend", Dosage = "500mg" });
        await _nutrientRepo.AddAsync(new SupplementNutrient { SupplementId = supplementId, GenericName = "Zinc", SpecificForm = "Picolinate", Dosage = string.Empty, ParentNutrientId = parentId });
        await _nutrientRepo.AddAsync(new SupplementNutrient { SupplementId = supplementId, GenericName = "Magnesium", SpecificForm = "Glycinate", Dosage = string.Empty, ParentNutrientId = parentId });
        await _nutrientRepo.AddAsync(new SupplementNutrient { SupplementId = supplementId, GenericName = "Standalone", SpecificForm = "Cholecalciferol", Dosage = "1000IU" });

        // Act
        var children = await _nutrientRepo.GetByParentIdAsync(parentId);

        // Assert – only the two children, not the standalone nutrient or the parent
        Assert.AreEqual(2, children.Count);
        CollectionAssert.AreEquivalent(new[] { "Zinc", "Magnesium" }, children.Select(c => c.GenericName).ToList());
    }

    [TestMethod]
    public async Task GetCountsBySupplementIds_EmptyAndMixedInput()
    {
        var sup1Id = await SeedSupplementAsync();
        var sup2Id = await SeedSupplementAsync();
        var sup3Id = await SeedSupplementAsync();
        await _nutrientRepo.AddAsync(new SupplementNutrient { SupplementId = sup1Id, GenericName = "Zinc", SpecificForm = "Picolinate", Dosage = "5mg" });
        await _nutrientRepo.AddAsync(new SupplementNutrient { SupplementId = sup1Id, GenericName = "Iron", SpecificForm = "Bisglycinate", Dosage = "18mg" });
        await _nutrientRepo.AddAsync(new SupplementNutrient { SupplementId = sup2Id, GenericName = "Vitamin C", SpecificForm = "Ascorbic Acid", Dosage = "500mg" });

        // Act – empty input returns an empty dictionary even though rows exist
        var empty = await _nutrientRepo.GetCountsBySupplementIdsAsync(new List<int>());
        Assert.AreEqual(0, empty.Count);

        // Act – mixed input: per-supplement counts only for supplements with rows
        var counts = await _nutrientRepo.GetCountsBySupplementIdsAsync(new List<int> { sup1Id, sup2Id, sup3Id });

        Assert.AreEqual(2, counts.Count);
        Assert.AreEqual(2, counts[sup1Id]);
        Assert.AreEqual(1, counts[sup2Id]);
        Assert.IsFalse(counts.ContainsKey(sup3Id));
    }

    [TestMethod]
    public async Task Update_PersistsParentNutrientIdChange()
    {
        var supplementId = await SeedSupplementAsync();
        var blendAId = await _nutrientRepo.AddAsync(new SupplementNutrient { SupplementId = supplementId, GenericName = "Blend A", SpecificForm = "Blend", Dosage = "500mg" });
        var blendBId = await _nutrientRepo.AddAsync(new SupplementNutrient { SupplementId = supplementId, GenericName = "Blend B", SpecificForm = "Blend", Dosage = "300mg" });
        var childId = await _nutrientRepo.AddAsync(new SupplementNutrient { SupplementId = supplementId, GenericName = "Zinc", SpecificForm = "Picolinate", Dosage = string.Empty, ParentNutrientId = blendAId });

        // Act – move the child from blend A to blend B and change its fields
        var child = await _nutrientRepo.GetByIdAsync(childId);
        Assert.IsNotNull(child);
        child!.GenericName = "Zinc Chelate";
        child.SpecificForm = "Bisglycinate";
        child.Dosage = "15mg";
        child.ParentNutrientId = blendBId;
        await _nutrientRepo.UpdateAsync(child);

        // Assert – all fields persisted, child now under blend B
        var updated = await _nutrientRepo.GetByIdAsync(childId);
        Assert.IsNotNull(updated);
        Assert.AreEqual("Zinc Chelate", updated!.GenericName);
        Assert.AreEqual("Bisglycinate", updated.SpecificForm);
        Assert.AreEqual("15mg", updated.Dosage);
        Assert.AreEqual(blendBId, updated.ParentNutrientId);
        Assert.AreEqual(0, (await _nutrientRepo.GetByParentIdAsync(blendAId)).Count);
        Assert.AreEqual(1, (await _nutrientRepo.GetByParentIdAsync(blendBId)).Count);
    }

}