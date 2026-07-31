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

    [Required]
    [StringLength(200)]
    public string Dosage { get; set; } = string.Empty;

    public Supplement? Supplement { get; set; }
}