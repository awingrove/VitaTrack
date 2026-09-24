using System.ComponentModel.DataAnnotations;

namespace VitaTrack.Core.Features.Family;

public class CreateFamilyMemberRequest
{
    [Required]
    [StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [StringLength(200)]
    public string DisplayName { get; set; } = string.Empty;

    [Url]
    [StringLength(500)]
    public string? AvatarUrl { get; set; }
}
