using System.ComponentModel.DataAnnotations;

namespace VitaTrack.Infrastructure.Models;

public class SupplementNutrient
{
    public int Id { get; set; }
    public int SupplementId { get; set; }

    [Required]
    [StringLength(200)]
    public string GenericName { get; set; } = string.Empty;

    [Required]
    [StringLength(200)]
    public string SpecificForm { get; set; } = string.Empty;

    [StringLength(200)]
    public string Dosage { get; set; } = string.Empty;

    public int? ParentNutrientId { get; set; }
    public SupplementNutrient? ParentNutrient { get; set; }

    public Supplement? Supplement { get; set; }
}