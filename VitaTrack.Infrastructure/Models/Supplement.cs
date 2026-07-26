namespace VitaTrack.Infrastructure.Models
{
    public class Supplement
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Brand { get; set; } = string.Empty;
        public string DailyDose { get; set; } = string.Empty;
        public string? ManufacturerUrl { get; set; }
        public string? NutritionJson { get; set; }
        public string? SwapSuggestion { get; set; }
        public decimal? Cost { get; set; } // Cost of the supplement
    }
}