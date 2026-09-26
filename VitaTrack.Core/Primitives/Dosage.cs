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
    /// Whether <paramref name="raw"/> is a dosage shape the app will accept: it must
    /// carry an amount, and any unit it carries must be one of the recognized symbols.
    /// The single predicate both write surfaces enforce, so a form and the LLM/Review
    /// path cannot disagree about what a dosage is.
    /// <para>
    /// Blank is well-formed — a blend child legitimately has no dosage of its own — so
    /// the "required" rules stay with the surfaces that impose them. An amount with no
    /// unit is also well-formed: the defect this closes is a <em>wrong</em> unit, not a
    /// missing one. What it rejects is an amount with a unit token that
    /// <see cref="Unit.Canonicalize"/> does not recognize ("3 capsules", "50 mg/kg",
    /// "20%DV"), and text with no amount at all ("one tablet") — the same two shapes
    /// <see cref="Unit.Parse"/> reports as an undefined unit, which is why the two
    /// cannot drift.
    /// </para>
    /// </summary>
    public static bool IsWellFormed(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return true;
        if (!TryParseAmount(raw, out _)) return false;
        return !Unit.HasUnitToken(raw) || Unit.Parse(raw).IsDefined;
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

        // Invariant, not CurrentCulture: a stored amount is always written with a "." decimal
        // separator, but in a locale like de-DE "." is the *group* separator and the default
        // NumberStyles allows thousands — so a bare TryParse reads "1.5" as fifteen. That
        // silently corrupts the amount rather than failing, which is the failure mode value
        // objects are supposed to rule out.
        return decimal.TryParse(AmountPattern.Match(raw).Value, NumberStyles.Number, CultureInfo.InvariantCulture, out amount);
    }

    public override string ToString()
    {
        var amount = Amount.ToString("0.##", CultureInfo.InvariantCulture);
        return Unit.IsDefined ? amount + " " + Unit.ToString() : amount;
    }
}
