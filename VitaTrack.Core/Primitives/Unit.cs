using System.Text.RegularExpressions;

namespace VitaTrack.Core.Primitives;

/// <summary>
/// A canonical nutrient/dose measurement unit (mg, µg, IU, g, ml, tsp, tbsp, tab).
/// Wraps the raw unit token and canonicalizes it on parse so aliases compare equal
/// (mcg == µg, iu == IU, tablets == tab). Prefer this over a bare string so amounts
/// and units are never confused across slices, and so free text ("3 capsules") can
/// never be mistaken for a unit.
/// </summary>
public readonly record struct Unit
{
    public string Symbol { get; }

    private Unit(string symbol) => Symbol = symbol;

    public bool IsDefined => !string.IsNullOrEmpty(Symbol);

    public static readonly Unit Milligram = new("mg");
    public static readonly Unit Microgram = new("µg");
    public static readonly Unit Gram = new("g");
    public static readonly Unit Milliliter = new("ml");
    public static readonly Unit Teaspoon = new("tsp");
    public static readonly Unit Tablespoon = new("tbsp");
    public static readonly Unit Tablet = new("tab");
    public static readonly Unit InternationalUnit = new("IU");

    private static readonly Regex UnitTokenPattern = new(@"[^\d.]+", RegexOptions.Compiled);

    /// <summary>
    /// Whether <paramref name="raw"/> carries a unit token at all, as decided by
    /// <see cref="UnitTokenPattern"/>. Lets a caller tell "no unit at all" (which is
    /// legal — a bare "500") apart from "a unit that is not one of ours" (which is
    /// not) without re-deriving the pattern. <see cref="Parse"/> collapses both cases
    /// onto an undefined <see cref="Unit"/>, so this is the only way to separate them.
    /// </summary>
    internal static bool HasUnitToken(string? raw)
        => !string.IsNullOrWhiteSpace(raw) && UnitTokenPattern.IsMatch(raw);

    public static Unit Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return default;
        var match = UnitTokenPattern.Match(raw);
        if (!match.Success) return default;
        var canonical = Canonicalize(match.Value.Trim());
        return canonical is null ? default : new Unit(canonical);
    }

    /// <summary>
    /// The single definition of the recognized set. Returns <c>null</c> for anything
    /// outside it, so membership is decided here and nowhere else and an unrecognized
    /// token is never passed through as though it were a unit.
    /// </summary>
    internal static string? Canonicalize(string token) => token.ToLowerInvariant() switch
    {
        "mg" => "mg",
        "µg" or "μg" or "ug" or "mcg" => "µg",
        "g" => "g",
        "ml" => "ml",
        "tsp" => "tsp",
        "tbsp" => "tbsp",
        "tab" or "tablet" or "tablets" => "tab",
        "iu" => "IU",
        _ => null,
    };

    public override string ToString() => Symbol ?? string.Empty;
}
