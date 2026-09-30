using System.Collections.Generic;

namespace VitaTrack.Core.Features.Supplements;

/// <summary>
/// The comparison result: columns in requested order, rows alphabetical by
/// matched key with blend children directly beneath their parent. Every row's
/// <c>Cells</c> aligns positionally with <c>Columns</c>.
/// </summary>
public sealed record ComparisonGrid(IReadOnlyList<ComparisonColumn> Columns, IReadOnlyList<ComparisonRow> Rows);
