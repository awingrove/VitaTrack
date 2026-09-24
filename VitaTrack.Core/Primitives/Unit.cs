namespace VitaTrack.Core.Primitives;

/// <summary>
/// A canonical nutrient/dose measurement unit (mg, µg, IU, g, ml, tsp). Wraps the
/// raw unit token and canonicalizes it on parse so aliases compare equal
/// (mcg == µg, iu == IU). Prefer this over a bare string so amounts and units are
/// never confused across slices.
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
    public static readonly Unit InternationalUnit = new("IU");

    public static Unit Parse(string? raw)
    {
        var token = DosageParser.ParseUnit(raw);
        return string.IsNullOrEmpty(token) ? default : new Unit(DosageParser.CanonicalizeUnit(token));
    }

    public override string ToString() => Symbol ?? string.Empty;
}
