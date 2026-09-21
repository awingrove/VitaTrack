namespace VitaTrack.Core.Primitives;

/// <summary>
/// A nutrient/dose amount with its unit, parsed from free text such as "500 mg".
/// Replaces the hand-combined <c>DosageParser.ParseAmount</c> + <c>Unit.Parse</c>
/// calls at every call site, so amount and unit travel as one typed value.
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

    public static Dosage Parse(string? raw)
        => new(DosageParser.ParseAmount(raw), Unit.Parse(raw));

    public override string ToString()
        => Unit.IsDefined ? Amount.ToString("0.##") + " " + Unit.ToString() : Amount.ToString("0.##");
}
