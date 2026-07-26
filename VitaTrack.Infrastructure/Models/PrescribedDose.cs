using System;

namespace VitaTrack.Infrastructure.Models
{
    public class PrescribedDose
    {
        public int Id { get; set; }
        public int FamilyMemberId { get; set; }
        public int SupplementId { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime? EndDate { get; set; } // null means ongoing
        public string Dosage { get; set; } = string.Empty; // e.g., "500 mg"
        public string Instructions { get; set; } = string.Empty; // e.g., "Take with food"
        public int FrequencyPerDay { get; set; } = 1; // e.g., 1 for once daily, 2 for twice daily

        // Navigation properties (not required for Dapper, but useful for reference)
        public FamilyMember? FamilyMember { get; set; }
        public Supplement? Supplement { get; set; }
    }
}
