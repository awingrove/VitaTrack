using System.ComponentModel.DataAnnotations;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using VitaTrack.Core.Features.Nutrients;

namespace VitaTrack.Tests;

[TestClass]
public class SupplementNutrientValidationTests
{
    private static (bool IsValid, List<ValidationResult> Results) Validate(SupplementNutrient nutrient)
    {
        var context = new ValidationContext(nutrient);
        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(nutrient, context, results, validateAllProperties: true);
        return (isValid, results);
    }

    [TestMethod]
    public void Child_WithBlankDosage_AndParent_IsValid()
    {
        var nutrient = new SupplementNutrient
        {
            GenericName = "Milk Thistle",
            ParentNutrientId = 9001,
        };

        var (isValid, results) = Validate(nutrient);

        Assert.IsTrue(isValid, string.Join("; ", results.Select(r => r.ErrorMessage)));
    }

    [TestMethod]
    public void TopLevel_WithBlankDosage_IsInvalid()
    {
        var nutrient = new SupplementNutrient
        {
            GenericName = "Zinc",
            Dosage = "",
            SpecificForm = "Picolinate",
            ParentNutrientId = null,
        };

        var (_, results) = Validate(nutrient);

        Assert.IsTrue(results.Any(r => r.MemberNames.Contains(nameof(SupplementNutrient.Dosage))));
    }

    [TestMethod]
    public void TopLevel_WithBlankSpecificForm_IsInvalid()
    {
        var nutrient = new SupplementNutrient
        {
            GenericName = "Zinc",
            Dosage = "15mg",
            SpecificForm = "   ",
            ParentNutrientId = null,
        };

        var (isValid, results) = Validate(nutrient);

        Assert.IsFalse(isValid);
        Assert.IsTrue(results.Any(r => r.MemberNames.Contains(nameof(SupplementNutrient.SpecificForm))));
    }

    [TestMethod]
    public void EmptyGenericName_IsInvalid()
    {
        var nutrient = new SupplementNutrient
        {
            SupplementId = 1,
            GenericName = "",
            Dosage = "500mg",
            SpecificForm = "Ascorbic Acid"
        };

        var (isValid, results) = Validate(nutrient);

        Assert.IsFalse(isValid);
        Assert.IsTrue(results.Any(r => r.MemberNames.Contains(nameof(SupplementNutrient.GenericName))));
    }

    [TestMethod]
    public void Child_WithBlankGenericName_IsInvalid()
    {
        var nutrient = new SupplementNutrient
        {
            SupplementId = 1,
            GenericName = "   ",
            ParentNutrientId = 9001,
        };

        var (isValid, results) = Validate(nutrient);

        Assert.IsFalse(isValid);
        Assert.IsTrue(results.Any(r => r.MemberNames.Contains(nameof(SupplementNutrient.GenericName))));
    }

    [TestMethod]
    public void TopLevel_WithAMalformedDosage_IsInvalid()
    {
        var (isValid, results) = Validate(TopLevel("one tablet"));

        Assert.IsFalse(isValid);
        Assert.IsTrue(
            results.Any(r => r.MemberNames.Contains(nameof(SupplementNutrient.Dosage))
                && r.ErrorMessage == SupplementNutrient.DosageShapeRequired),
            string.Join("; ", results.Select(r => r.ErrorMessage)));
    }

    [TestMethod]
    public void TopLevel_WithAnUnrecognizedUnit_IsInvalid()
    {
        foreach (var dosage in new[] { "3 capsules", "500 mg with food", "5mg x2", "50 mg/kg", "20%DV", "abc" })
        {
            var (isValid, results) = Validate(TopLevel(dosage));

            Assert.IsFalse(isValid, $"'{dosage}' must not save");
            Assert.IsTrue(
                results.Any(r => r.MemberNames.Contains(nameof(SupplementNutrient.Dosage))
                    && r.ErrorMessage == SupplementNutrient.DosageShapeRequired),
                $"'{dosage}': {string.Join("; ", results.Select(r => r.ErrorMessage))}");
        }
    }

    [TestMethod]
    public void TopLevel_WithAWellFormedDosage_IsValid()
    {
        foreach (var dosage in new[] { "500mg", "500 mg", "200IU", "0mg", "1 tablet", "500" })
        {
            var (isValid, results) = Validate(TopLevel(dosage));

            Assert.IsTrue(isValid, $"'{dosage}' should save: {string.Join("; ", results.Select(r => r.ErrorMessage))}");
        }
    }

    [TestMethod]
    public void Child_WithAMalformedDosage_IsInvalid()
    {
        // The child branch used to validate nothing, so a crafted form body on Edit
        // could store a free-text dosage that then reached the nutrient report.
        foreach (var dosage in new[] { "one tablet", "3 capsules", "500 mg with food", "50 mg/kg", "20%DV" })
        {
            var (isValid, results) = Validate(Child(dosage));

            Assert.IsFalse(isValid, $"'{dosage}' must not save on a blend child");
            Assert.IsTrue(
                results.Any(r => r.MemberNames.Contains(nameof(SupplementNutrient.Dosage))
                    && r.ErrorMessage == SupplementNutrient.DosageShapeRequired),
                $"'{dosage}': {string.Join("; ", results.Select(r => r.ErrorMessage))}");
        }
    }

    [TestMethod]
    public void Child_WithABlankDosage_StaysValid()
    {
        // Shape-only on the child branch: a blend child legitimately has no dosage
        // of its own, so blank must not become a "required" failure here.
        foreach (var dosage in new[] { "", "   " })
        {
            var (isValid, results) = Validate(Child(dosage));

            Assert.IsTrue(isValid, $"'{dosage}': {string.Join("; ", results.Select(r => r.ErrorMessage))}");
        }
    }

    [TestMethod]
    public void DosageShapeRequired_NamesEveryRecognizedUnit()
    {
        // The message is user-facing and is shown on both write surfaces, so the set of
        // units it lists must stay the set Unit.Canonicalize recognizes.
        foreach (var symbol in new[] { "mg", "µg", "g", "ml", "tsp", "tbsp", "tab", "IU" })
        {
            StringAssert.Contains(SupplementNutrient.DosageShapeRequired, symbol);
        }
    }

    private static SupplementNutrient TopLevel(string dosage) => new()
    {
        GenericName = "Zinc",
        Dosage = dosage,
        SpecificForm = "Picolinate",
        ParentNutrientId = null,
    };

    private static SupplementNutrient Child(string dosage) => new()
    {
        GenericName = "Pectin",
        Dosage = dosage,
        SpecificForm = "Citrus",
        ParentNutrientId = 9001,
    };
}
