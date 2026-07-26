namespace VitaTrack.Infrastructure.Models
{
    public class SupplementNutrient
    {
        public int Id { get; set; }
        public int SupplementId { get; set; }
        public string GenericName { get; set; } = string.Empty;  // e.g. "Zinc"
        public string SpecificForm { get; set; } = string.Empty;  // e.g. "Zinc Picolinate"
        public string Dosage { get; set; } = string.Empty;        // e.g. "5mg"

        public Supplement? Supplement { get; set; }
    }
}