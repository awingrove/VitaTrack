using System.Globalization;
using System.Text.RegularExpressions;

namespace VitaTrack.Core.Primitives;

/// <summary>
/// A nutrient/dose amount with its unit, parsed from free text such as "500 mg".
/// Owns amount extraction, the tolerant <see cref="TryParse"/> for best-effort
/// callers, and <see cref="Normalize"/> — the single write-choke canonicalizer — so
/// amount and unit travel as one typed value instead of being recombined by hand at
/// each call site.
/// </summary>
public readonly record struct Dosage
{
    public decimal Amount { get; }
    public Unit Unit { get; }

    public Dosage(decimal amount, Unit unit)
    {
        Amount = amount;
        Unit = unit;
    }

    public bool IsDefined => Amount != 0m || Unit.IsDefined;

    private static readonly Regex AmountPattern = new(@"[\d]+\.?\d*", RegexOptions.Compiled);

    private static readonly Regex DosageShape = new(
        @"^(?<prefix>\s*)(?<amount>\d+(?:\.\d+)?)(?<gap>\s*)(?<unit>\D+)$",
        RegexOptions.Compiled);

    public static Dosage Parse(string? raw) => new(ParseAmount(raw), Unit.Parse(raw));

    /// <summary>
    /// Canonicalizes a dosage string for storage while preserving the user's spacing.
    /// Deliberately not <c>Parse(x).ToString()</c>: that would force a single space and
    /// trim trailing zeros, rewriting every persisted row.
    /// </summary>
    public static string Normalize(string? dosage)
    {
        if (string.IsNullOrWhiteSpace(dosage)) return dosage ?? string.Empty;
        var match = DosageShape.Match(dosage);
        if (!match.Success) return dosage;

        var unit = match.Groups["unit"].Value.Trim();
        var canonical = Unit.Canonicalize(unit);
        if (canonical is null || canonical == unit) return dosage;

        return $"{match.Groups["prefix"].Value}{match.Groups["amount"].Value}{match.Groups["gap"].Value}{canonical}";
    }

    /// <summary>
    /// Tolerant parse for best-effort callers: false when the text carries no amount
    /// at all. The unit may still be undefined — "3 capsules" parses to 3 with no unit.
    /// </summary>
    public static bool TryParse(string? raw, out Dosage dosage)
    {
        var hasAmount = TryParseAmount(raw, out var amount);
        dosage = new Dosage(amount, Unit.Parse(raw));
        return hasAmount;
    }

    private static decimal ParseAmount(string? raw)
        => TryParseAmount(raw, out var amount) ? amount : 0m;

    private static bool TryParseAmount(string? raw, out decimal amount)
    {
        amount = 0m;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        return decimal.TryParse(AmountPattern.Match(raw).Value, out amount);
    }

    public override string ToString()
    {
        var amount = Amount.ToString("0.##", CultureInfo.InvariantCulture);
        return Unit.IsDefined ? amount + " " + Unit.ToString() : amount;
    }
}
