using System.Collections.Generic;

namespace VitaTrack.Core.Features.Supplements;

/// <summary>
/// One nutrient's value in one supplement's column. <c>Dosage</c> is the raw
/// stored value passed through <c>Dosage.Normalize</c>, and may be empty for a
/// blend child that carries no dosage of its own.
/// </summary>
public sealed record ComparisonCell(string SpecificForm, string Dosage);
