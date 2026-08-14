using System.ComponentModel.DataAnnotations;
using VitaTrack.Infrastructure.Models;

namespace VitaTrack.Web.Models;

public class ConfirmSupplementRequest
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

    [Range(0.01, 99999)]
    public decimal? Cost { get; set; }

    public string? SwapSuggestion { get; set; }

    public List<SupplementNutrientDto>? Nutrients { get; set; }

    public Supplement ToSupplement() => new()
    {
        Id = Id,
        Name = Name,
        Brand = Brand,
        DailyDose = DailyDose,
        ManufacturerUrl = ManufacturerUrl,
        Cost = Cost,
        SwapSuggestion = SwapSuggestion
    };
}