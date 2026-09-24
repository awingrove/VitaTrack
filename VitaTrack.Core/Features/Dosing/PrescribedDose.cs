using System.ComponentModel.DataAnnotations;

namespace VitaTrack.Core.Features.Dosing;

public class PrescribedDose
{
    public int Id { get; set; }

    [Range(1, int.MaxValue)]
    public int FamilyMemberId { get; set; }

    [Range(1, int.MaxValue)]
    public int SupplementId { get; set; }

    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }

    [Range(0.01, 1000)]
    public decimal Multiplier { get; set; } = 1m;

    [StringLength(500)]
    [DisplayFormat(ConvertEmptyStringToNull = false)]
    public string Instructions { get; set; } = string.Empty;

    public string? FamilyMemberName { get; set; }
    public string? SupplementName { get; set; }
    public string? SupplementBrand { get; set; }

    public DosePeriod Period => DosePeriod.From(this);

    public bool IsActiveOn(DateTime day) => Period.IsActiveOn(day);
}
