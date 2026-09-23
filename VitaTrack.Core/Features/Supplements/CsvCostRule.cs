using System.Globalization;

namespace VitaTrack.Core.Features.Supplements;

internal sealed class CsvCostRule : ICsvRowRule
{
    public CsvParseError? Apply(CsvRowContext context)
    {
        if (string.IsNullOrWhiteSpace(context.CostText))
            return null;

        if (decimal.TryParse(context.CostText, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            if (parsed <= 0)
                return new CsvParseError(context.LineNumber, "Cost must be positive");
            context.Cost = parsed;
            return null;
        }

        return new CsvParseError(context.LineNumber, $"Invalid Cost value: '{context.CostText}'");
    }
}
