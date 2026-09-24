namespace VitaTrack.Core.Features.Reporting;

public interface IReportingService
{
    Task<NutrientReportData> GetNutrientReportDataAsync();
    Task<CostReportData> GetCostReportDataAsync();
}