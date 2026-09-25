using System.Globalization;
using System.Text.RegularExpressions;

namespace VitaTrack.Core.Primitives;

/// <summary>
/// A nutrient/dose amount with its unit, parsed from free text such as "500 mg".
/// Owns amount extraction and the tolerant <see cref="TryParse"/> for best-effort
/// callers, so amount and unit travel as one typed value instead of being
/// recombined by hand at each call site.
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

    public static Dosage Parse(string? raw) => new(ParseAmount(raw), Unit.Parse(raw));

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
