using System.ComponentModel.DataAnnotations;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VitaTrack.Infrastructure.Models;

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
}
