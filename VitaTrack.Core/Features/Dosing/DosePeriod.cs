namespace VitaTrack.Core.Features.Dosing;

/// <summary>
/// The active window of a prescribed dose. Owns the "is this dose active on a given
/// day" rule — previously leaked into ReportingService.GetActiveDosesAsync.
/// </summary>
public readonly record struct DosePeriod
{
    public DateTime? Start { get; }
    public DateTime? End { get; }

    public DosePeriod(DateTime? start, DateTime? end)
    {
        Start = start;
        End = end;
    }

    public static DosePeriod From(PrescribedDose dose) => new(dose.StartDate, dose.EndDate);

    public bool IsActiveOn(DateTime day) =>
        (!Start.HasValue || Start.Value <= day) && (!End.HasValue || End.Value >= day);
}
