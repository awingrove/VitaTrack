namespace VitaTrack.Core.Features.ServiceConnections;

/// <summary>
/// A configured connection to an OpenAI-compatible service (credential row).
/// A connection is never seeded; the table starts empty.
/// </summary>
public sealed record ServiceConnection
{
    public int Id { get; init; }
    public string Service { get; init; } = string.Empty;
    public string BaseUrl { get; init; } = string.Empty;
    public string ApiKey { get; init; } = string.Empty;
    public string? Model { get; init; }
    public string? Variant { get; init; }
    public int MaxTokens { get; init; } = 16384;
    public double Temperature { get; init; } = 1.0;
    public string Verification { get; init; } = "unverified"; // "verified" | "unverified"
    public DateTimeOffset? VerifiedAt { get; init; }
    public bool IsActive { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}
