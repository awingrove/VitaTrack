using System.ComponentModel.DataAnnotations;

namespace VitaTrack.Infrastructure.Models;

public class Supplement
{
    public int Id { get; set; }

    [Required]
    [StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [StringLength(200)]
    public string Brand { get; set; } = string.Empty;

    [Required]
    [StringLength(200)]
    public string DailyDose { get; set; } = string.Empty;

    [Url]
    [StringLength(500)]
    public string? ManufacturerUrl { get; set; }

    public string? NutritionJson { get; set; }
    public string? SwapSuggestion { get; set; }

    [Range(0.01, 99999)]
    public decimal? Cost { get; set; }
}