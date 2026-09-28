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
    public string Verification { get; init; } = Unverified; // "verified" | Unverified
    public DateTimeOffset? VerifiedAt { get; init; }
    public bool IsActive { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }

    /// <summary>Written only by <see cref="ProbeConnectionHandler"/>, and only when the
    /// service answered a probe with a catalog this app could read.</summary>
    public const string Verified = "verified";

    /// <summary>The state a connection is written in whenever nothing has confirmed the
    /// credential: a fresh connect, a failed probe, and a re-probe that stopped
    /// answering. A probe adds the verified value beside this one; it does not add a
    /// second definition of the unverified one, and it never removes this one.</summary>
    public const string Unverified = "unverified";
}
