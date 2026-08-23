using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace VitaTrack.Infrastructure.Models;

public class SupplementNutrient : IValidatableObject
{
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

    public Supplement? Supplement { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (ParentNutrientId == null)
        {
            if (string.IsNullOrWhiteSpace(Dosage))
            {
                yield return new ValidationResult("Top-level nutrients require a dosage.", new[] { nameof(Dosage) });
            }

            if (string.IsNullOrWhiteSpace(SpecificForm))
            {
                yield return new ValidationResult("Top-level nutrients require a specific form.", new[] { nameof(SpecificForm) });
            }
        }
    }
}
