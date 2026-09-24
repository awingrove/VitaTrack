namespace VitaTrack.Core.Features.Supplements;

internal interface ICsvRowRule
{
    // Applies one validation/parsing rule to the row context. Returns null to
    // continue to the next rule; a non-null CsvParseError stops the chain.
    CsvParseError? Apply(CsvRowContext context);
}
