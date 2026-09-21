using System.Text.RegularExpressions;

namespace VitaTrack.Core;

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

    private static readonly Regex UnitPattern = new(@"[^\d.]+", RegexOptions.Compiled);

    public static string ParseUnit(string? dosage)
    {
        if (string.IsNullOrWhiteSpace(dosage)) return string.Empty;
        var match = UnitPattern.Match(dosage);
        return match.Success ? match.Value.Trim() : string.Empty;
    }

    // Canonical unit designations: every microgram value is stored as "µg" (U+00B5),
    // international units as "IU". Unknown units pass through untouched.
    private static string CanonicalUnit(string unit) => unit.ToLowerInvariant() switch
    {
        "mg" => "mg",
        "µg" or "μg" or "ug" or "mcg" => "µg",
        "g" => "g",
        "ml" => "ml",
        "tsp" => "tsp",
        "iu" => "IU",
        _ => unit,
    };

    private static readonly Regex DosageShape = new(
        @"^(?<prefix>\s*)(?<amount>\d+(?:\.\d+)?)(?<gap>\s*)(?<unit>\D+)$",
        RegexOptions.Compiled);

    public static string NormalizeDosage(string? dosage)
    {
        if (string.IsNullOrWhiteSpace(dosage)) return dosage ?? string.Empty;
        var match = DosageShape.Match(dosage);
        if (!match.Success) return dosage;

        var unit = match.Groups["unit"].Value.Trim();
        var canonical = CanonicalUnit(unit);
        if (canonical == unit) return dosage;

        return $"{match.Groups["prefix"].Value}{match.Groups["amount"].Value}{match.Groups["gap"].Value}{canonical}";
    }
}
