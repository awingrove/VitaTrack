using System.Text.RegularExpressions;

namespace VitaTrack.Infrastructure;

public static class DosageParser
{
    private static readonly Regex AmountPattern = new(@"[\d]+\.?\d*", RegexOptions.Compiled);

    public static decimal ParseAmount(string? dosage)
    {
        if (string.IsNullOrWhiteSpace(dosage)) return 0;
        var match = AmountPattern.Match(dosage);
        if (decimal.TryParse(match.Value, out var val)) return val;
        return 0;
    }
}
