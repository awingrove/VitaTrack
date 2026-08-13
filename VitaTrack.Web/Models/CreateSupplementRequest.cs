using System.ComponentModel.DataAnnotations;
using VitaTrack.Infrastructure.Models;

namespace VitaTrack.Web.Models;

public class CreateSupplementRequest
{
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

    public Supplement ToSupplement() => new()
    {
        Name = Name,
        Brand = Brand,
        DailyDose = DailyDose,
        ManufacturerUrl = ManufacturerUrl,
        Cost = Cost
    };
}