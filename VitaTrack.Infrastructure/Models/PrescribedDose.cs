using System.ComponentModel.DataAnnotations;

namespace VitaTrack.Infrastructure.Models
{
    public class PrescribedDose
    {
        public int Id { get; set; }

        [Range(1, int.MaxValue)]
        public int FamilyMemberId { get; set; }

        [Range(1, int.MaxValue)]
        public int SupplementId { get; set; }

        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }

        [Required]
        [StringLength(200)]
        public string Dosage { get; set; } = string.Empty;

        [Required]
        [StringLength(500)]
        public string Instructions { get; set; } = string.Empty;

        [Range(0.1, 50)]
        public decimal FrequencyPerDay { get; set; } = 1m;

        public string? FamilyMemberName { get; set; }
        public string? SupplementName { get; set; }
    }
}