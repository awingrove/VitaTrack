using System.ComponentModel.DataAnnotations;

namespace VitaTrack.Core.Features.Dosing;

public class CreateDoseRequest
{
    [Range(1, int.MaxValue)]
    public int FamilyMemberId { get; set; }

    [Range(1, int.MaxValue)]
    public int SupplementId { get; set; }

    [Range(0.01, 1000)]
    public decimal Multiplier { get; set; } = 1m;

    [StringLength(500)]
    public string Instructions { get; set; } = string.Empty;

    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
}
