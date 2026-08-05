namespace VitaTrack.Infrastructure;

public class VitaTrackOptions
{
    public string? ApiKey { get; set; }
    public string? BaseUrl { get; set; }
    public string? Model { get; set; }
    public int MaxTokens { get; set; } = 16384;
    public string? ReasoningEffort { get; set; }
    public double Temperature { get; set; } = 1.0;
}
