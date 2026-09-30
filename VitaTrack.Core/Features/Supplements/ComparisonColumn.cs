namespace VitaTrack.Core.Features.Supplements;

/// <summary>
/// One supplement's column in the comparison grid: identity plus the three
/// header lines (name, brand, and the free-text <c>DailyDose</c> shown as-is).
/// </summary>
public sealed record ComparisonColumn(int SupplementId, string Name, string Brand, string DailyDose);
