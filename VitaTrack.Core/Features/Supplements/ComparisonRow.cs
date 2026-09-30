using System.Collections.Generic;

namespace VitaTrack.Core.Features.Supplements;

/// <summary>
/// One row of the comparison grid. <c>Cells</c> is positionally aligned with
/// <see cref="ComparisonGrid.Columns"/>; a <c>null</c> cell means the
/// supplement at that position does not contain the nutrient (rendered as an
/// em dash). <c>IsBlendChild</c> marks a row indented under its blend parent.
/// </summary>
public sealed record ComparisonRow(string Label, bool IsBlendChild, IReadOnlyList<ComparisonCell?> Cells);
