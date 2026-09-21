using System.Globalization;

namespace VitaTrack.Core.Primitives;

/// <summary>
/// A monetary amount with an explicit currency. The app previously hardcoded a "£"
/// prefix in views; carrying the currency on the value removes that assumption and
/// makes cost math type-safe across slices. Default currency is GBP to match the
/// existing UI; a Supplement.Currency column backs it in the database.
/// </summary>
public readonly record struct Money
{
    private static readonly Dictionary<string, string> CurrencySymbols = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GBP"] = "£",
        ["USD"] = "$",
        ["EUR"] = "€",
    };

    public decimal Amount { get; }
    public string Currency { get; }

    public Money(decimal amount, string currency)
    {
        Amount = amount;
        Currency = currency;
    }

    public bool IsDefined => Amount != 0m || !string.IsNullOrEmpty(Currency);

    public override string ToString()
    {
        var amount = Amount.ToString("0.00", CultureInfo.InvariantCulture);
        if (Currency == null) return amount;
        return CurrencySymbols.TryGetValue(Currency, out var s) ? s + amount : amount + " " + Currency;
    }

    public static Money operator +(Money left, Money right)
    {
        // Caller guarantees same currency for aggregations; mixed-currency addition is
        // out of scope (single-currency reports today).
        return new Money(left.Amount + right.Amount, left.Currency);
    }
}
