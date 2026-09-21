namespace VitaTrack.Core.Features.Dosing;

/// <summary>
/// A prescribed dose multiplier. Server is the authority on the valid range and
/// step; the client stepper UI (`multiplier-stepper.js`) only mirrors these rules.
/// </summary>
public readonly record struct DoseMultiplier
{
    public const decimal Min = 0.01m;
    public const decimal Max = 1000m;
    public const decimal Step = 0.25m;
    public static readonly DoseMultiplier Default = new(1m);

    public decimal Value { get; }

    public DoseMultiplier(decimal value)
    {
        if (value < Min || value > Max)
            throw new ArgumentOutOfRangeException(nameof(value), $"Multiplier must be between {Min} and {Max}.");
        Value = value;
    }

    public DoseMultiplier Stepped(decimal step = Step)
    {
        var rounded = Math.Round(Value / step, MidpointRounding.AwayFromZero) * step;
        return new DoseMultiplier(Math.Clamp(rounded, Min, Max));
    }

    public static bool TryValidate(decimal value, out string? error)
    {
        if (value < Min || value > Max)
        {
            error = $"Multiplier must be between {Min} and {Max}.";
            return false;
        }
        error = null;
        return true;
    }

    public static implicit operator decimal(DoseMultiplier multiplier) => multiplier.Value;
    public static explicit operator DoseMultiplier(decimal value) => new(value);
}
