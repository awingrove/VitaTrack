using VitaTrack.Core.Primitives;

namespace VitaTrack.Core.Features.Reporting;

public record SupplementCostRow(string Name, string Brand, Money UnitCost, Money MonthlyCost);
