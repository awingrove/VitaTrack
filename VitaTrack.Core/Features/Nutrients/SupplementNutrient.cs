using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using VitaTrack.Core.Features.Supplements;
using VitaTrack.Core.Primitives;

namespace VitaTrack.Core.Features.Nutrients;

public class SupplementNutrient : IValidatableObject
{
    /// <summary>
    /// The one user-facing wording for a malformed dosage. Both write surfaces
    /// (this model's validation and <see cref="SupplementNutrientService"/>'s
    /// persist path) show the same text, so a drifting copy cannot reach a user
    /// as two different explanations of one rule.
    /// </summary>
    internal const string DosageShapeRequired =
        "Dosage must be a number with a recognized unit (mg, µg, g, ml, tsp, tbsp, tab, IU).";

    public int Id { get; set; }
    public int SupplementId { get; set; }

    [Required]
    [StringLength(200)]
    public string GenericName { get; set; } = string.Empty;

    [StringLength(200)]
    public string SpecificForm { get; set; } = string.Empty;

    [StringLength(200)]
    public string Dosage { get; set; } = string.Empty;

    public int? ParentNutrientId { get; set; }
    public SupplementNutrient? ParentNutrient { get; set; }

    /// <summary>
    /// The <see cref="Dosage"/> value object parsed from the free-text
    /// <see cref="Dosage"/> string column; the string column and Dapper
    /// mapping are unchanged.
    /// </summary>
    public Dosage ParsedDosage => Primitives.Dosage.Parse(Dosage);

    public Supplement? Supplement { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(GenericName))
        {
            yield return new ValidationResult("The GenericName field is required.", new[] { nameof(GenericName) });
        }

        if (ParentNutrientId == null)
        {
            if (string.IsNullOrWhiteSpace(Dosage))
            {
                yield return new ValidationResult("Top-level nutrients require a dosage.", new[] { nameof(Dosage) });
            }
            else if (!Primitives.Dosage.IsWellFormed(Dosage))
            {
                yield return new ValidationResult(DosageShapeRequired, new[] { nameof(Dosage) });
            }

            if (string.IsNullOrWhiteSpace(SpecificForm))
            {
                yield return new ValidationResult("Top-level nutrients require a specific form.", new[] { nameof(SpecificForm) });
            }
        }
        else if (!Primitives.Dosage.IsWellFormed(Dosage))
        {
            // Shape only: a blank dosage is legal for a blend child, so there is
            // deliberately no "required" rule on this branch — just the same shape
            // check the root path gets. It used to validate nothing at all, which a
            // crafted form body on Edit could walk straight past.
            yield return new ValidationResult(DosageShapeRequired, new[] { nameof(Dosage) });
        }
    }
}
