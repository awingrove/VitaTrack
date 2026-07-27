using System;

namespace VitaTrack.Infrastructure.Models
{
    public class PrescribedDose
    {
        public int Id { get; set; }
        public int FamilyMemberId { get; set; }
        public int SupplementId { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public string Dosage { get; set; } = string.Empty;
        public string Instructions { get; set; } = string.Empty;
        public int FrequencyPerDay { get; set; } = 1;

        // Display names populated via JOIN query (not stored)
        public string? FamilyMemberName { get; set; }
        public string? SupplementName { get; set; }
    }
}