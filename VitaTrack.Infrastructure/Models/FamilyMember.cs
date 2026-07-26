namespace VitaTrack.Infrastructure.Models
{
    public class FamilyMember
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string? AvatarUrl { get; set; }
    }
}